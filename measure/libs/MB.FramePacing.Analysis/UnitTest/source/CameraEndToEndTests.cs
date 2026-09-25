//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL camera capture end to end without ffmpeg: synthetic high speed camera -> rig calibration -> rectified zones -> recorder ->
//* analysis, against the synthetic ground truth (presented frames, display times, scanout delay, tears).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Camera;
using MB.FramePacing.Capture.Synthetic;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  public class CameraEndToEndTests
  {
    private string m_directory = string.Empty;

    [SetUp]
    public void SetUp()
    {
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

    private static SyntheticCamera CreateCamera(int tearEvery = 0) =>
      new SyntheticCamera(
        new SyntheticScenario(
          new SyntheticScenarioOptions
          {
            CaptureFps = 1000,
            RefreshHz = 60,
            StartMarkerSeconds = 0.1,
            RunSeconds = 0.6,
            EndMarkerSeconds = 0.1,
            StallEvery = 7,
            TearEvery = tearEvery,
            RunId = 3,
            RunName = "camera",
          }
        ),
        new SyntheticCameraOptions()
      );

    private AnalysisReport CaptureAndAnalyze(SyntheticCamera camera)
    {
      var frames = CameraFrameSet.Collect(new SyntheticCameraSource(camera), 0.5, 1L << 30, TimeSpan.FromMinutes(1), CancellationToken.None);
      var rig = CameraCalibrator.Calibrate(frames, new CameraCalibratorOptions());
      Assert.That(rig.HasFailures, Is.False, string.Join("\n", rig.Checks));

      var output = Path.Combine(m_directory, "capture");
      using (var source = new RectifyingCaptureSource(new SyntheticCameraSource(camera), rig))
        CaptureRunner.Run(source, new CaptureRunOptions { OutputDirectory = output, Camera = rig }, null, CancellationToken.None);
      return CaptureAnalyzer.Analyze(output, new AnalysisOptions());
    }

    /// <summary>The frames of the measured run, in display order.</summary>
    private static List<SyntheticPresentedFrame> RunFrames(SyntheticCamera camera) =>
      camera.Scenario.PresentedFrames.Where(f => f.Payload.Kind == MarkerKind.Frame && f.Payload.RunId == camera.Scenario.Options.RunId).ToList();

    [Test]
    public void VsyncOn_EveryFrameIsTimed_NoTears()
    {
      var camera = CreateCamera();
      var report = CaptureAndAnalyze(camera);

      Assert.That(report.Warnings, Has.Some.Contains("VERY EXPERIMENTAL"));
      Assert.That(report.Timeline.Runs, Has.Count.EqualTo(1));
      var run = report.Timeline.Runs[0];
      var truth = RunFrames(camera);
      Assert.That(run.Frames.Select(f => f.FrameIndex), Is.EqualTo(truth.Select(f => f.Payload.FrameIndex)));

      // Display deltas (camera clock) against the true presentation deltas: each first-seen time is good to about one camera period
      var errors = new List<double>();
      for (int i = 1; i < truth.Count; ++i)
      {
        double expected = camera.ToCameraTicks(truth[i].DisplayTicks - truth[i - 1].DisplayTicks);
        errors.Add(Math.Abs(run.Frames[i].DisplayDeltaTicks!.Value - expected) / TimeSpan.TicksPerMillisecond);
      }
      TestContext.Out.WriteLine($"display delta error: mean {errors.Average():0.000} ms, max {errors.Max():0.000} ms");
      Assert.That(errors.Max(), Is.LessThanOrEqualTo(2.0));
      Assert.That(errors.Average(), Is.LessThan(0.8));

      Assert.That(run.Camera, Is.Not.Null);
      double scanout = camera.ToCameraTicks(camera.ZoneScanTicks(1) - camera.ZoneScanTicks(0)) / TimeSpan.TicksPerMillisecond;
      Assert.That(run.Camera!.ScanoutDelay.P50, Is.EqualTo(scanout).Within(1.0));
      Assert.That(run.Camera.TornFrames, Is.EqualTo(0));
      Assert.That(run.Camera.SecondZoneOnlyFrames, Is.EqualTo(0));
      Assert.That(run.Frames.Count(f => f.Flags.HasFlag(PresentedFrameFlags.UncertainStart)), Is.EqualTo(0));
      Assert.That(run.Counts.Torn, Is.EqualTo(0), "zones that disagree are scanout progress, not torn captures");
    }

    [Test]
    public void VsyncOff_FramesPresentedBetweenTheZones_AreTears()
    {
      var camera = CreateCamera(tearEvery: 5);
      var report = CaptureAndAnalyze(camera);

      var run = report.Timeline.Runs.Single();
      // The synthetic tear happens half a refresh after vsync: below the top zone, above the bottom one
      var torn = RunFrames(camera)
        .Where(f => f.DisplayTicks % camera.Scenario.RefreshIntervalTicks != 0)
        .Select(f => f.Payload.FrameIndex)
        .ToHashSet();
      var flagged = run.Frames.Where(f => f.Flags.HasFlag(PresentedFrameFlags.Torn)).Select(f => f.FrameIndex).ToHashSet();
      TestContext.Out.WriteLine($"torn {torn.Count}, flagged {flagged.Count}, camera {run.Camera}");
      Assert.That(torn, Is.Not.Empty);
      // A torn frame that the next vsync replaces is only ever seen below the tear (second zone only); the others reach both zones and are
      // flagged. Nothing else may be.
      Assert.That(flagged, Is.SubsetOf(torn));
      Assert.That(run.Camera!.TornFrames + run.Camera.SecondZoneOnlyFrames, Is.EqualTo(torn.Count));
      var seen = run.Frames.Select(f => f.FrameIndex).ToHashSet();
      Assert.That(torn.Count(seen.Contains), Is.EqualTo(run.Camera.TornFrames));
    }
  }
}
