//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL camera capture end to end through a real ffmpeg: the synthetic high speed camera is encoded as a slow motion clip (stored at
//* 30 fps), a rig is calibrated from it, and the import stores only the rectified marker zones. Skipped when ffmpeg is not installed.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Camera;
using MB.FramePacing.Capture.Ffmpeg;
using MB.FramePacing.Capture.Synthetic;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  [Category("ffmpeg")]
  public class FfmpegCameraTests
  {
    private const double CameraFps = 1000;
    private const double StoredFps = 30;

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

    internal static SyntheticCamera CreateCamera(SyntheticCameraOptions? cameraOptions = null, int tearEvery = 0) =>
      new SyntheticCamera(
        new SyntheticScenario(
          new SyntheticScenarioOptions
          {
            CaptureFps = CameraFps,
            RefreshHz = 60,
            StartMarkerSeconds = 0.1,
            RunSeconds = 0.6,
            EndMarkerSeconds = 0.1,
            StallEvery = 7,
            TearEvery = tearEvery,
            RunId = 9,
            RunName = "camera",
          }
        ),
        cameraOptions ?? new SyntheticCameraOptions()
      );

    [Test]
    public void SlowMotionClip_CalibrateThenImport_StoresTheRectifiedZones()
    {
      var camera = CreateCamera();
      var clip = EncodeClip(camera);
      var media = MediaInput.Create(clip, new MediaInputOptions { RecordedFps = CameraFps }, m_directory);
      var options = media.ToCaptureOptions(m_ffmpeg);

      CameraRig rig;
      using (var source = FfmpegCaptureSource.Start(options, TimeSpan.FromSeconds(30)))
        rig = CameraCalibrator.Calibrate(source, new CameraCalibratorOptions { Seconds = 0.5 }, CancellationToken.None);
      foreach (var check in rig.Checks)
        TestContext.Out.WriteLine(check);
      Assert.That(rig.HasFailures, Is.False);
      Assert.That(rig.CameraFps, Is.EqualTo(CameraFps).Within(1), "the recorded fps must replace the clip's 30 fps timestamps");
      double expectedDelay = (camera.ZoneScanTicks(1) - camera.ZoneScanTicks(0)) / TimeSpan.TicksPerMillisecond;
      Assert.That(rig.ScanoutDelayMs, Is.EqualTo(expectedDelay).Within(1.0));

      using (var source = FfmpegCaptureSource.Start(options, TimeSpan.FromSeconds(30)))
        Assert.That(
          CameraCalibrator.Verify(rig, source, new CameraCalibratorOptions(), CancellationToken.None).Select(c => c.Level),
          Has.None.EqualTo(CameraCheckLevel.Fail)
        );

      var output = Path.Combine(m_directory, "capture");
      using (var source = FfmpegCaptureSource.Start(options with { Camera = rig }, TimeSpan.FromSeconds(30)))
      {
        Assert.That(source.Format.Width, Is.EqualTo(CameraZone.StoredSizePx));
        Assert.That(source.Format.Height, Is.EqualTo(2 * CameraZone.StoredSizePx));
        CaptureRunner.Run(
          source,
          new CaptureRunOptions
          {
            OutputDirectory = output,
            Camera = rig,
            RecordedFps = CameraFps,
          },
          null,
          CancellationToken.None
        );
      }

      // Every tile holds its marker axis aligned at the known origin with 4 px modules: the locked fast path must decode most captures
      int size = MarkerRenderer.MarkerSizePx(CameraZone.StoredPxPerModule);
      var decoder = new MarkerDecoder(sampleModuleGrid: true);
      var decoded = new int[2];
      int records = 0;
      using (var reader = new CaptureFileReader(Path.Combine(output, CaptureSessionInfo.FramesFileName)))
      {
        var image = reader.CreateFrameImage();
        for (long record = 0; record < reader.RecordCount; ++record)
        {
          reader.ReadRecord(record, image);
          ++records;
          for (int zone = 0; zone < 2; ++zone)
          {
            int y = (zone * CameraZone.StoredSizePx) + CameraZone.StoredMarkerOriginPx;
            var markerLock = new MarkerLock(new PixelRect(CameraZone.StoredMarkerOriginPx, y, size, size), CameraZone.StoredPxPerModule);
            if (decoder.DecodeLocked(image, markerLock).IsDecoded)
              ++decoded[zone];
          }
          var dump = Environment.GetEnvironmentVariable("MB_FRAMEPACING_TEST_DUMP");
          if (records == 300 && !string.IsNullOrEmpty(dump))
            PgmFile.Write(Path.Combine(dump, "camera-rectified.pgm"), image);
        }
      }
      TestContext.Out.WriteLine($"{records} records, decoded {decoded[0]} / {decoded[1]}");
      Assert.That(records, Is.EqualTo(camera.CaptureCount));
      Assert.That(decoded[0], Is.GreaterThan(records * 0.8));
      Assert.That(decoded[1], Is.GreaterThan(records * 0.8));

      var session = CaptureSessionInfo.TryLoad(output)!;
      Assert.That(session.Camera, Is.Not.Null);
      Assert.That(session.RecordedFps, Is.EqualTo(CameraFps));

      // The analysis recognises the camera capture and finds every presented frame of the run
      var report = CaptureAnalyzer.Analyze(output, new AnalysisOptions());
      var run = report.Timeline.Runs.Single();
      var truth = camera
        .Scenario.PresentedFrames.Where(f => f.Payload.Kind == MarkerKind.Frame && f.Payload.RunId == 9)
        .Select(f => f.Payload.FrameIndex);
      Assert.That(run.Frames.Select(f => f.FrameIndex), Is.EqualTo(truth));
      Assert.That(run.Camera!.ScanoutDelay.P50, Is.EqualTo(expectedDelay).Within(1.0));
      Assert.That(run.Camera.TornFrames, Is.EqualTo(0));
      Assert.That(
        File.ReadAllText(Path.Combine(output, CaptureAnalyzer.AnalysisDirectoryName, CaptureAnalyzer.SummaryFileName)),
        Does.Contain("VERY EXPERIMENTAL")
      );
    }

    /// <summary>Render the synthetic camera and store it losslessly as a slow motion clip: 30 fps timestamps, 1000 fps content.</summary>
    internal string EncodeClip(SyntheticCamera camera)
    {
      var images = Directory.CreateDirectory(Path.Combine(m_directory, "camera-images")).FullName;
      var frame = new GrayImage(camera.Options.CameraWidth, camera.Options.CameraHeight);
      for (long i = 0; i < camera.CaptureCount; ++i)
      {
        camera.Render(i, frame);
        PgmFile.Write(Path.Combine(images, $"frame{i}.pgm"), frame);
      }

      var clip = Path.Combine(m_directory, "camera.mkv");
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
          StoredFps.ToString(System.Globalization.CultureInfo.InvariantCulture),
          "-start_number",
          "0",
          "-i",
          Path.Combine(images, "frame%d.pgm"),
          "-c:v",
          "ffv1",
          clip,
        }
      )
        encode.ArgumentList.Add(arg);
      using var process = Process.Start(encode)!;
      string errors = process.StandardError.ReadToEnd();
      process.WaitForExit();
      Assert.That(process.ExitCode, Is.EqualTo(0), errors);
      Directory.Delete(images, true);
      return clip;
    }
  }
}
