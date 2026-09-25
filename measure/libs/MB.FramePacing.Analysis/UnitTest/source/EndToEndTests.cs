//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Synthetic capture -> frames.mbfc -> CaptureAnalyzer. The analyzer must recover the synthetic ground truth exactly: every presented frame,
//* its first-seen capture time, skipped frame indices and the animation error caused by the injected stalls.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Synthetic;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  public class EndToEndTests
  {
    private string m_directory = string.Empty;

    [SetUp]
    public void SetUp()
    {
      m_directory = Path.Combine(Path.GetTempPath(), "mb-framepacing-tests", Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
      try
      {
        if (Directory.Exists(m_directory))
          Directory.Delete(m_directory, true);
      }
      catch (IOException) { }
    }

    private static List<(ulong FrameIndex, long AnimationTicks, long FirstSeenTicks)> ExpectedFrames(SyntheticScenario scenario)
    {
      var expected = new List<(ulong, long, long)>();
      for (long i = 0; i < scenario.CaptureCount; ++i)
      {
        int index = scenario.PresentedIndexAt(i);
        if (index < 0)
          continue;
        var payload = scenario.PresentedFrames[index].Payload;
        if (payload.Kind != MarkerKind.Frame)
          continue;
        if (expected.Count == 0 || expected[^1].Item1 != payload.FrameIndex)
          expected.Add((payload.FrameIndex, payload.AnimationTicks, scenario.CaptureTicks(i)));
      }
      return expected;
    }

    [TestCase(240.0, 60.0, 10, 0)]
    [TestCase(500.0, 144.0, 13, 29)]
    [TestCase(240.0, 144.0, 0, 11)]
    public void Analyzer_RecoversTheGroundTruth(double captureFps, double refreshHz, int stallEvery, int skipEvery)
    {
      var scenario = new SyntheticScenario(
        new SyntheticScenarioOptions
        {
          CaptureFps = captureFps,
          RefreshHz = refreshHz,
          RunSeconds = 2,
          StallEvery = stallEvery,
          SkipEvery = skipEvery,
          RunName = "e2e",
          RunId = 42,
        }
      );
      using (var source = new SyntheticCaptureSource(scenario, paced: false))
        CaptureRunner.Run(source, new CaptureRunOptions { OutputDirectory = m_directory, RingFrames = 8192 }, null, CancellationToken.None);

      var report = CaptureAnalyzer.Analyze(m_directory, new AnalysisOptions());

      Assert.That(report.Capture.TimeSource, Is.EqualTo(TimeSource.Device));
      Assert.That(report.Capture.Layout.ModuleSizePx, Is.EqualTo(scenario.Options.ModuleSizePx).Within(0.2));
      var run = report.Timeline.Runs.Single();
      Assert.That(run.RunId, Is.EqualTo(42));
      Assert.That(run.Name, Is.EqualTo("e2e"));
      Assert.That(run.StartTimeUtc, Is.EqualTo(scenario.StartMetadata.StartTimeUtc));
      Assert.That(run.HasStartMarker && run.HasEndMarker);
      Assert.That(run.Counts.Undecodable + run.Counts.Torn + run.Counts.NotRecorded, Is.Zero);

      var expected = ExpectedFrames(scenario);
      Assert.That(run.Frames.Select(f => f.FrameIndex), Is.EqualTo(expected.Select(e => e.FrameIndex)));
      for (int i = 0; i < expected.Count; ++i)
      {
        var frame = run.Frames[i];
        Assert.That(frame.FirstSeenTicks, Is.EqualTo(expected[i].FirstSeenTicks), $"frame {frame.FrameIndex}");
        if (i > 0)
        {
          long expectedError =
            (expected[i].AnimationTicks - expected[i - 1].AnimationTicks) - (expected[i].FirstSeenTicks - expected[i - 1].FirstSeenTicks);
          Assert.That(frame.AnimationErrorTicks, Is.EqualTo(expectedError), $"frame {frame.FrameIndex}");
        }
      }

      if (stallEvery > 0)
        Assert.That(run.Statistics.FramesWithAnimationError, Is.GreaterThan(0), "stalls must show up as animation error");
      else
        Assert.That(
          run.Statistics.AnimationErrorMs.Min,
          Is.GreaterThanOrEqualTo(-1000.0 / captureFps - 1e-6),
          "no stalls: only skips (positive) and quantisation"
        );

      // Reports
      var summaryPath = Path.Combine(report.OutputDirectory, CaptureAnalyzer.SummaryFileName);
      Assert.That(File.Exists(summaryPath));
      using (var summary = System.Text.Json.JsonDocument.Parse(File.ReadAllText(summaryPath)))
      {
        var histograms = summary.RootElement.GetProperty("runs")[0].GetProperty("histograms");
        long withError = run.Frames.LongCount(f => f.AnimationErrorTicks.HasValue);
        Assert.That(histograms.GetProperty("animationErrorMs").GetProperty("total").GetInt64(), Is.EqualTo(withError));
        Assert.That(histograms.GetProperty("displayDeltaMs").GetProperty("binWidthMs").GetDouble(), Is.EqualTo(report.CapturePeriodMs).Within(1e-9));
      }
      Assert.That(
        File.ReadLines(Path.Combine(report.OutputDirectory, CaptureAnalyzer.CapturesFileName)).Count(),
        Is.EqualTo(scenario.CaptureCount + 1)
      );
      Assert.That(File.ReadLines(Path.Combine(report.OutputDirectory, "run-42-frames.csv")).Count(), Is.EqualTo(expected.Count + 1));
    }

    [Test]
    public void Analyzer_TearingMarkers_DetectTornCaptures()
    {
      // Two markers per frame; for a few captures the lower one shows the next frame (a tear between them)
      var header = new CaptureFileHeader(200, 320, FrameRate.FromFps(240));
      Directory.CreateDirectory(m_directory);
      var framesPath = Path.Combine(m_directory, CaptureSessionInfo.FramesFileName);
      var frame = new GrayImage(header.Width, header.Height, 96);
      var record = new byte[header.RecordSize];
      int frames = 40;
      using (var writer = new CaptureFileWriter(framesPath, header))
      {
        for (int i = 0; i < frames; ++i)
        {
          ulong top = (ulong)(i / 4);
          ulong bottom = i % 4 == 3 ? top + 1 : top;
          var kind = i < 4 ? MarkerKind.SequenceStart : MarkerKind.Frame;
          StartMetadata? start = kind == MarkerKind.SequenceStart ? new StartMetadata(0, "tear") : null;
          Array.Fill(frame.Pixels, (byte)96);
          MarkerRenderer.Render(frame, new MarkerPayload(top, (long)top * 166_667, 1, kind), 12, 12, 3, metadata: start);
          if (kind == MarkerKind.Frame)
            MarkerRenderer.Render(frame, new MarkerPayload(bottom, (long)bottom * 166_667, 1, kind), 12, 200, 3);
          new CaptureRecordHeader(i, i * 41_667L, i * 41_667L, CaptureRecordFlags.None, header.PixelByteCount).Write(record);
          frame.Pixels.CopyTo(record, CaptureFileHeader.RecordHeaderSize);
          writer.WriteRecords(record);
        }
      }

      var report = CaptureAnalyzer.Analyze(m_directory, new AnalysisOptions());

      Assert.That(report.Capture.Layout.Locks, Has.Count.EqualTo(2));
      Assert.That(report.Capture.Rows.Count(r => r.Status == CaptureStatus.Torn), Is.EqualTo(9), "captures 7, 11, ... 39 are torn");
    }

    [TestCase(true, 20, true)]
    [TestCase(true, 1, false)]
    [TestCase(false, 20, false)]
    public void Analyzer_RegionCapture_WarnsWhenTheMarkerMayHaveMoved(bool region, int lostCaptures, bool expectWarning)
    {
      // Only the marker's region was stored (fast capture); the last captures lost the marker, as if the application moved it
      var header = new CaptureFileHeader(160, 160, FrameRate.FromFps(240), 1920, 1080, region ? new PixelRect(16, 16, 320, 320) : default);
      Directory.CreateDirectory(m_directory);
      var frame = new GrayImage(header.Width, header.Height, 96);
      var record = new byte[header.RecordSize];
      const int Captures = 40;
      using (var writer = new CaptureFileWriter(Path.Combine(m_directory, CaptureSessionInfo.FramesFileName), header))
      {
        for (int i = 0; i < Captures; ++i)
        {
          Array.Fill(frame.Pixels, (byte)96);
          if (i < Captures - lostCaptures)
            MarkerRenderer.Render(frame, new MarkerPayload((ulong)(i / 4), i / 4 * 166_667L, 1, MarkerKind.Frame), 8, 8, 3);
          new CaptureRecordHeader(i, i * 41_667L, i * 41_667L, CaptureRecordFlags.None, header.PixelByteCount).Write(record);
          frame.Pixels.CopyTo(record, CaptureFileHeader.RecordHeaderSize);
          writer.WriteRecords(record);
        }
      }

      var report = CaptureAnalyzer.Analyze(m_directory, new AnalysisOptions());

      Assert.That(report.Capture.Rows.Count(r => r.Status == CaptureStatus.Undecodable), Is.EqualTo(lostCaptures));
      Assert.That(report.Warnings.Any(w => w.Contains("may have moved", StringComparison.Ordinal)), Is.EqualTo(expectWarning));
    }
  }
}
