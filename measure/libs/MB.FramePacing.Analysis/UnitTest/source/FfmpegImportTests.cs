//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* End to end through a real ffmpeg: synthetic marker frames -> image folder / video file -> import -> analyze -> must match the ground truth.
//* Skipped when ffmpeg is not installed (see FfmpegLocator for where it is looked up).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Ffmpeg;
using MB.FramePacing.Capture.Synthetic;
using MB.FramePacing.Data;
using MB.FramePacing.MarkerDecoding;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  [Category("ffmpeg")]
  public class FfmpegImportTests
  {
    private string m_directory = string.Empty;
    private string m_ffmpeg = string.Empty;

    [SetUp]
    public void SetUp()
    {
      try
      {
        m_ffmpeg = FfmpegLocator.Find(null, FramePacingConfig.Load());
      }
      catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
      {
        Assert.Ignore("ffmpeg is not installed: " + ex.Message);
      }
      m_directory = Path.Combine(Path.GetTempPath(), "mb-framepacing-tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(m_directory);
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

    private static SyntheticScenario CreateScenario() =>
      new SyntheticScenario(
        new SyntheticScenarioOptions
        {
          CaptureFps = 240,
          RefreshHz = 240,
          RunSeconds = 1,
          StartMarkerSeconds = 0.25,
          EndMarkerSeconds = 0.25,
          StallEvery = 11,
          SkipEvery = 13,
          RunId = 5,
          SequenceTag = "ffmpeg import",
        }
      );

    /// <summary>Writes every synthetic capture as frame{N}.pgm (no zero padding, to exercise the natural ordering).</summary>
    private sealed class PgmSink(string directory, int width, int height) : IFrameSink
    {
      private readonly byte[] m_pixels = new byte[width * height];

      public int Count { get; private set; }

      public Span<byte> BeginFrame() => m_pixels;

      public void EndFrame(TickCount64 hostTime, DeviceTimestamp deviceTime, uint sourceDrops) =>
        PgmFile.Write(Path.Combine(directory, $"frame{Count++}.pgm"), new GrayImage(width, height, width, m_pixels));
    }

    /// <summary>
    /// Draws a sync marker into every synthetic capture before it goes on: the run id and frame index of the frame the capture shows,
    /// except at <paramref name="tornCapture"/>, where it names the next frame (as a tear between the two markers shows).
    /// </summary>
    private sealed class SyncMarkerSink(IFrameSink inner, SyntheticScenario scenario, int x, int y, long tornCapture) : IFrameSink
    {
      private readonly GrayImage m_frame = new GrayImage(scenario.Options.Width, scenario.Options.Height);
      private long m_capture;

      public Span<byte> BeginFrame() => m_frame.Pixels.AsSpan(0, m_frame.Width * m_frame.Height);

      public void EndFrame(TickCount64 hostTime, DeviceTimestamp deviceTime, uint sourceDrops)
      {
        var shown = scenario.PresentedFrames[scenario.PresentedIndexAt(m_capture)].Payload;
        ulong frameIndex = shown.FrameIndex + (m_capture == tornCapture ? 1UL : 0UL);
        MarkerRenderer.Render(
          m_frame,
          new MarkerPayload(MarkerKind.Sync, shown.RunId, frameIndex, MB.FramePacing.Marker.MarkerFlags.NoFlags, TimeSpan.Zero),
          x,
          y,
          scenario.Options.ModuleSizePx
        );
        ++m_capture;
        BeginFrame().CopyTo(inner.BeginFrame());
        inner.EndFrame(hostTime, deviceTime, sourceDrops);
      }
    }

    private static List<(ulong FrameIndex, long AnimationTicks)> ExpectedFrames(SyntheticScenario scenario)
    {
      var expected = new List<(ulong, long)>();
      for (long i = 0; i < scenario.CaptureCount; ++i)
      {
        var payload = scenario.PresentedFrames[scenario.PresentedIndexAt(i)].Payload;
        if (payload.Kind == MarkerKind.Frame && (expected.Count == 0 || expected[^1].Item1 != payload.FrameIndex))
          expected.Add((payload.FrameIndex, payload.AnimationTime.Ticks));
      }
      return expected;
    }

    private AnalysisReport ImportAndAnalyze(string input, MediaInputOptions mediaOptions)
    {
      var output = Path.Combine(m_directory, "capture");
      var media = MediaInput.Create(input, mediaOptions, output);
      using (var source = FfmpegCaptureSource.Start(media.ToCaptureOptions(m_ffmpeg), TimeSpan.FromSeconds(30)))
        CaptureRunner.Run(source, new CaptureRunOptions { OutputDirectory = output }, null, CancellationToken.None);
      return CaptureAnalyzer.Analyze(output, new AnalysisOptions());
    }

    /// <summary>Render the scenario and encode it losslessly into a 240 fps video with ffmpeg itself.</summary>
    private string EncodeVideo(SyntheticScenario scenario, Func<IFrameSink, IFrameSink>? draw = null)
    {
      var images = Directory.CreateDirectory(Path.Combine(m_directory, "images")).FullName;
      IFrameSink sink = new PgmSink(images, scenario.Options.Width, scenario.Options.Height);
      new SyntheticCaptureSource(scenario, paced: false).Run(draw?.Invoke(sink) ?? sink, new CaptureClock(), CancellationToken.None);

      var video = Path.Combine(m_directory, "markers.mkv");
      var encode = new ProcessStartInfo(m_ffmpeg)
      {
        UseShellExecute = false,
        RedirectStandardError = true,
        CreateNoWindow = true,
      };
      foreach (
        var arg in new[]
        {
          "-hide_banner",
          "-loglevel",
          "error",
          "-framerate",
          "240",
          "-start_number",
          "0",
          "-i",
          Path.Combine(images, "frame%d.pgm"),
          "-c:v",
          "ffv1",
          video,
        }
      )
        encode.ArgumentList.Add(arg);
      using (var process = Process.Start(encode)!)
      {
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.That(process.ExitCode, Is.Zero, errors);
      }
      return video;
    }

    private static void AssertMatchesGroundTruth(SyntheticScenario scenario, AnalysisReport report, double capturePeriodMs)
    {
      var run = report.Timeline.Runs.Single();
      Assert.That(run.RunId, Is.EqualTo(5));
      Assert.That(run.SequenceId, Is.EqualTo("ffmpeg import"));
      Assert.That(run.Counts.Undecodable + run.Counts.NotRecorded, Is.Zero);
      Assert.That(report.CapturePeriodMs, Is.EqualTo(capturePeriodMs).Within(0.01));
      var expected = ExpectedFrames(scenario);
      Assert.That(run.Frames.Select(f => (f.FrameIndex, f.AnimationTime.Ticks)), Is.EqualTo(expected));
    }

    [Test]
    public void ImageSequence_ImportsWithExactTimes()
    {
      var scenario = CreateScenario();
      var images = Directory.CreateDirectory(Path.Combine(m_directory, "images")).FullName;
      var sink = new PgmSink(images, scenario.Options.Width, scenario.Options.Height);
      new SyntheticCaptureSource(scenario, paced: false).Run(sink, new CaptureClock(), CancellationToken.None);

      var report = ImportAndAnalyze(images, new MediaInputOptions { Fps = 240 });

      Assert.That(report.Capture.Rows.Count, Is.EqualTo(sink.Count), "one capture per image, the concat list's repeated last entry is not recorded");
      Assert.That(report.Capture.TimeSource, Is.EqualTo(TimeSource.Device));
      AssertMatchesGroundTruth(scenario, report, 1000.0 / 240);
    }

    [Test]
    public void VideoFile_ImportsWithTheFilesTimestamps()
    {
      var scenario = CreateScenario();
      var video = EncodeVideo(scenario);

      var report = ImportAndAnalyze(video, new MediaInputOptions());

      // Matroska stores millisecond timestamps, so the period is 4 ms (+-1) rather than 4.167 ms; the frames must still all be there
      var run = report.Timeline.Runs.Single();
      Assert.That(run.Counts.Undecodable + run.Counts.NotRecorded, Is.Zero);
      Assert.That(run.Frames.Select(f => (f.FrameIndex, f.AnimationTime.Ticks)), Is.EqualTo(ExpectedFrames(scenario)));
    }

    /// <summary>Fast capture (--roi auto): locate the marker, store only its region downscaled, and still recover every frame.</summary>
    [Test]
    public void VideoFile_AutoRoi_StoresOnlyTheMarkerRegion()
    {
      var scenario = new SyntheticScenario(CreateScenario().Options with { Width = 640, Height = 360, ModuleSizePx = 6, OriginX = 64, OriginY = 48 });
      var video = EncodeVideo(scenario);
      var output = Path.Combine(m_directory, "capture");
      var options = MediaInput.Create(video, new MediaInputOptions(), output).ToCaptureOptions(m_ffmpeg);

      var located = FfmpegMarkerLocator.Locate(options, TimeSpan.FromSeconds(30), CancellationToken.None);

      Assert.That(located.SourceLock.Bounds.X, Is.EqualTo(64).Within(1));
      Assert.That(located.SourceLock.Bounds.Y, Is.EqualTo(48).Within(1));
      Assert.That(located.Crop.Factor, Is.EqualTo(2), "6 px modules are stored at 3 px");
      Assert.That(located.Scale, Is.EqualTo((located.Crop.StoredWidth, located.Crop.StoredHeight)));
      using (var source = FfmpegCaptureSource.Start(located.Apply(options), TimeSpan.FromSeconds(30)))
        CaptureRunner.Run(source, new CaptureRunOptions { OutputDirectory = output, KeepFrames = true }, null, CancellationToken.None);
      var report = CaptureAnalyzer.Analyze(output, new AnalysisOptions());

      var header = report.Capture.Header;
      Assert.That(header.Roi, Is.EqualTo(located.Crop.Roi));
      Assert.That((header.Width, header.Height), Is.EqualTo((located.Crop.StoredWidth, located.Crop.StoredHeight)));
      Assert.That(header.RecordSize, Is.EqualTo(located.RecordSize));
      Assert.That(located.RecordSize * 4, Is.LessThan(located.FullFrameRecordSize), "far less to write than whole frames");
      var framesLength = new FileInfo(Path.Combine(output, CaptureSessionInfo.FramesFileName)).Length;
      Assert.That(framesLength, Is.EqualTo(CaptureFileHeader.HeaderSize + (report.Capture.Rows.Count * (long)header.RecordSize)));
      Assert.That(report.Warnings, Has.None.Contains("moved"));

      var run = report.Timeline.Runs.Single();
      Assert.That(run.Counts.Undecodable + run.Counts.NotRecorded, Is.Zero);
      Assert.That(run.Frames.Select(f => (f.FrameIndex, f.AnimationTime.Ticks)), Is.EqualTo(ExpectedFrames(scenario)));
    }

    /// <summary>
    /// A recording with both markers: the two regions are located, stored as one frame (the main marker's on top) at 3 px per module, every
    /// frame is recovered and the tearing check still works: the capture whose sync marker names another frame is torn.
    /// </summary>
    [Test]
    public void VideoFile_BothMarkers_AreStoredAsOneStackedFrame_AndTearingIsChecked()
    {
      const int SyncX = 32;
      const int SyncY = 410;
      const long TornCapture = 90;
      var scenario = new SyntheticScenario(
        CreateScenario().Options with
        {
          Width = 960,
          Height = 640,
          ModuleSizePx = 6,
          OriginX = 32,
          OriginY = 32,
          RunSeconds = 0.5,
          StartMarkerSeconds = 0.1,
          EndMarkerSeconds = 0.1,
        }
      );
      var video = EncodeVideo(scenario, sink => new SyncMarkerSink(sink, scenario, SyncX, SyncY, TornCapture));
      var output = Path.Combine(m_directory, "capture");
      var options = MediaInput.Create(video, new MediaInputOptions(), output).ToCaptureOptions(m_ffmpeg);

      var located = FfmpegMarkerLocator.Locate(options, TimeSpan.FromSeconds(30), CancellationToken.None);

      Assert.That(located.SyncLock, Is.Not.Null, "the sync marker is found with the main marker");
      Assert.That((located.SyncLock!.Value.Bounds.X, located.SyncLock.Value.Bounds.Y), Is.EqualTo((SyncX, SyncY)));
      var crop = located.Crop;
      Assert.That((crop.Factor, crop.HasSyncRoi), Is.EqualTo((2, true)));
      var stacked = located.Apply(options);
      Assert.That(
        (stacked.Roi, stacked.SyncRoi, stacked.RoiDownscale, stacked.Scale),
        Is.EqualTo((crop.Roi, (PixelRect?)crop.SyncRoi, 2, ((int, int)?)null))
      );

      using (var source = FfmpegCaptureSource.Start(stacked, TimeSpan.FromSeconds(30)))
        CaptureRunner.Run(source, new CaptureRunOptions { OutputDirectory = output }, null, CancellationToken.None);
      var report = CaptureAnalyzer.Analyze(output, new AnalysisOptions());

      // One stored frame holds both regions, and the capture says which regions they are
      var header = report.Capture.Header;
      Assert.That((header.Width, header.Height), Is.EqualTo((crop.StoredWidth, crop.StoredHeight)));
      Assert.That(header.Roi, Is.EqualTo(crop.Roi));
      Assert.That((report.Session!.Roi, report.Session.SyncRoi), Is.EqualTo((crop.Roi.ToString(), crop.SyncRoi.ToString())));
      using (var data = new CaptureDataReader(Path.Combine(output, CaptureSessionInfo.DataFileName)))
        Assert.That(data.Header.SyncRegion, Is.EqualTo(new Rectangle(crop.SyncRoi.X, crop.SyncRoi.Y, crop.SyncRoi.Width, crop.SyncRoi.Height)));
      Assert.That(crop.StoredWidth * crop.StoredHeight * 10, Is.LessThan(960 * 640), "far less to read than whole frames");

      // Both markers are decoded from it, and the one capture whose markers disagree is torn
      Assert.That(report.Capture.Layout.Locks, Has.Count.EqualTo(2));
      Assert.That(report.Capture.Rows.Count(r => r.Status == CaptureStatus.Undecodable), Is.Zero);
      Assert.That(report.Capture.Rows.Where(r => r.Status == CaptureStatus.Torn).Select(r => r.CaptureIndex), Is.EqualTo(new[] { TornCapture }));
      Assert.That(report.Warnings, Has.None.Contains("moved"));
      var expected = ExpectedFrames(scenario).Select(f => f.FrameIndex).ToList();
      var found = report.Timeline.Runs.Single().Frames.Select(f => f.FrameIndex).ToList();
      Assert.That(found, Is.SubsetOf(expected));
      Assert.That(found, Has.Count.GreaterThanOrEqualTo(expected.Count - 1), "every frame, but perhaps the torn capture's");
    }
  }
}
