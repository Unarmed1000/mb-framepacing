//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* End to end through a real ffmpeg: synthetic marker frames -> image folder / video file -> import -> analyze -> must match the ground truth.
//* Skipped when ffmpeg is not installed (see FfmpegLocator for where it is looked up).
//*
//* (c) 2026 Mana Battery
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
using MB.FramePacing.Marker;
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
          RefreshHz = 60,
          RunSeconds = 1,
          StartMarkerSeconds = 0.25,
          EndMarkerSeconds = 0.25,
          StallEvery = 11,
          SkipEvery = 13,
          RunId = 5,
          RunName = "ffmpeg import",
        }
      );

    /// <summary>Writes every synthetic capture as frame{N}.pgm (no zero padding, to exercise the natural ordering).</summary>
    private sealed class PgmSink(string directory, int width, int height) : IFrameSink
    {
      private readonly byte[] m_pixels = new byte[width * height];

      public int Count { get; private set; }

      public Span<byte> BeginFrame() => m_pixels;

      public void EndFrame(long hostTicks, long deviceTicks, CaptureRecordFlags flags) =>
        PgmFile.Write(Path.Combine(directory, $"frame{Count++}.pgm"), new GrayImage(width, height, width, m_pixels));
    }

    private static List<(ulong FrameIndex, long AnimationTicks)> ExpectedFrames(SyntheticScenario scenario)
    {
      var expected = new List<(ulong, long)>();
      for (long i = 0; i < scenario.CaptureCount; ++i)
      {
        var payload = scenario.PresentedFrames[scenario.PresentedIndexAt(i)].Payload;
        if (payload.Kind == MarkerKind.Frame && (expected.Count == 0 || expected[^1].Item1 != payload.FrameIndex))
          expected.Add((payload.FrameIndex, payload.AnimationTicks));
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
    private string EncodeVideo(SyntheticScenario scenario)
    {
      var images = Directory.CreateDirectory(Path.Combine(m_directory, "images")).FullName;
      new SyntheticCaptureSource(scenario, paced: false).Run(
        new PgmSink(images, scenario.Options.Width, scenario.Options.Height),
        new CaptureClock(),
        CancellationToken.None
      );

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
      Assert.That(run.Name, Is.EqualTo("ffmpeg import"));
      Assert.That(run.Counts.Undecodable + run.Counts.NotRecorded, Is.Zero);
      Assert.That(report.CapturePeriodMs, Is.EqualTo(capturePeriodMs).Within(0.01));
      var expected = ExpectedFrames(scenario);
      Assert.That(run.Frames.Select(f => (f.FrameIndex, f.AnimationTicks)), Is.EqualTo(expected));
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
      Assert.That(run.Frames.Select(f => (f.FrameIndex, f.AnimationTicks)), Is.EqualTo(ExpectedFrames(scenario)));
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
        CaptureRunner.Run(source, new CaptureRunOptions { OutputDirectory = output }, null, CancellationToken.None);
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
      Assert.That(run.Frames.Select(f => (f.FrameIndex, f.AnimationTicks)), Is.EqualTo(ExpectedFrames(scenario)));
    }
  }
}
