//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Camera rig calibration and verification on the synthetic high speed camera, against its ground truth.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Linq;
using System.Threading;
using MB.FramePacing.Capture.Camera;
using MB.FramePacing.Capture.Synthetic;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class CameraCalibratorTests
  {
    private static readonly CameraCalibratorOptions g_options = new CameraCalibratorOptions { Seconds = 0.5 };

    private static SyntheticCamera CreateCamera(SyntheticCameraOptions? cameraOptions = null) =>
      new SyntheticCamera(
        new SyntheticScenario(
          new SyntheticScenarioOptions
          {
            CaptureFps = 1000,
            RefreshHz = 60,
            StartMarkerSeconds = 0.05,
            RunSeconds = 1,
            EndMarkerSeconds = 0.05,
          }
        ),
        cameraOptions ?? new SyntheticCameraOptions()
      );

    private static CameraFrameSet Collect(SyntheticCamera camera, double seconds) =>
      CameraFrameSet.Collect(new SyntheticCameraSource(camera), seconds, 1L << 30, TimeSpan.FromMinutes(1), CancellationToken.None);

    [Test]
    public void Calibrate_RecoversGeometryScanoutAndRefresh()
    {
      var camera = CreateCamera(new SyntheticCameraOptions { LensDistortion = 0 });
      var rig = CameraCalibrator.Calibrate(Collect(camera, g_options.Seconds), g_options);

      foreach (var check in rig.Checks)
        TestContext.Out.WriteLine(check);
      Assert.That(rig.HasFailures, Is.False);
      Assert.That(rig.Checks.Where(c => c.Level != CameraCheckLevel.Pass), Is.Empty);
      Assert.That(rig.Zones, Has.Count.EqualTo(2));

      // Zone 0 is the one the scanout reaches first: the TopLeft slot
      for (int zone = 0; zone < 2; ++zone)
      {
        foreach (var module in new ImagePoint[] { new(0, 0), new(25, 0), new(0, 25), new(25, 25) })
        {
          double error = ImagePoint.Distance(camera.ZoneModuleToObserved(zone, module), rig.Zones[zone].ModuleToCamera.Map(module));
          Assert.That(error, Is.LessThan(0.3), $"zone {zone} module {module}");
        }
      }

      double expectedDelay = camera.ToCameraTicks(camera.ZoneScanTicks(1) - camera.ZoneScanTicks(0)) / TimeSpan.TicksPerMillisecond;
      Assert.That(rig.ScanoutDelayMs, Is.EqualTo(expectedDelay).Within(1.0));
      Assert.That(rig.RefreshHz, Is.EqualTo(60).Within(0.5));
      Assert.That(rig.CameraFps, Is.EqualTo(1000).Within(1));
      Assert.That(rig.Zones[0].ModuleSizePx, Is.GreaterThan(3));
    }

    [Test]
    public void Rig_RoundTripsThroughJson()
    {
      var camera = CreateCamera();
      var rig = CameraCalibrator.Calibrate(Collect(camera, g_options.Seconds), g_options);

      var json = rig.ToJson();
      var loaded = CameraRig.FromJson(json);

      Assert.That(json, Does.Contain("VERY EXPERIMENTAL"));
      Assert.That(loaded.Zones, Has.Count.EqualTo(2));
      Assert.That(loaded.Zones[1].ModuleToCamera, Is.EqualTo(rig.Zones[1].ModuleToCamera));
      Assert.That(loaded.Checks.Select(c => c.Level), Is.EqualTo(rig.Checks.Select(c => c.Level)));
      Assert.That(loaded.ScanoutDelayMs, Is.EqualTo(rig.ScanoutDelayMs));
    }

    [Test]
    public void Verify_PassesForTheSameRig_FailsWhenTheCameraMoved()
    {
      var camera = CreateCamera();
      var rig = CameraCalibrator.Calibrate(Collect(camera, g_options.Seconds), g_options);

      var same = CameraCalibrator.Verify(rig, Collect(camera, 0.1), g_options);
      Assert.That(same.Select(c => c.Level), Is.All.EqualTo(CameraCheckLevel.Pass), string.Join("\n", same));

      // Nudge the camera: 2% to the right and a little rotation
      var moved = Homography.Multiply(new Homography(0.999, -0.02, 8, 0.02, 0.999, 3, 0, 0), camera.ScreenToCamera);
      var bumped = CreateCamera(new SyntheticCameraOptions { ScreenToCamera = moved });
      var checks = CameraCalibrator.Verify(rig, Collect(bumped, 0.1), g_options);
      Assert.That(checks.Any(c => c.Level == CameraCheckLevel.Fail), Is.True, string.Join("\n", checks));
    }

    [Test]
    public void Calibrate_WithTheMiddleTearingMarker_UsesTheTopAndBottomZones()
    {
      var camera = CreateCamera(new SyntheticCameraOptions { MiddleMarker = true, LensDistortion = 0 });

      var rig = CameraCalibrator.Calibrate(Collect(camera, g_options.Seconds), g_options);

      Assert.That(rig.HasFailures, Is.False, string.Join(Environment.NewLine, rig.Checks));
      Assert.That(rig.Zones, Has.Count.EqualTo(2));
      for (int zone = 0; zone < 2; ++zone)
      {
        var centre = new ImagePoint(12.5, 12.5);
        Assert.That(ImagePoint.Distance(camera.ZoneModuleToObserved(zone, centre), rig.Zones[zone].ModuleToCamera.Map(centre)), Is.LessThan(0.5));
      }
    }

    [Test]
    public void Verify_AcceptsStartMarkers()
    {
      var camera = CreateCamera();
      var rig = CameraCalibrator.Calibrate(Collect(camera, g_options.Seconds), g_options);
      var starting = new SyntheticCamera(
        new SyntheticScenario(
          new SyntheticScenarioOptions
          {
            CaptureFps = 1000,
            RefreshHz = 60,
            StartMarkerSeconds = 0.5,
            RunSeconds = 0.2,
          }
        ),
        new SyntheticCameraOptions()
      );

      var checks = CameraCalibrator.Verify(rig, Collect(starting, 0.1), g_options);

      Assert.That(checks.Select(c => c.Level), Is.All.EqualTo(CameraCheckLevel.Pass), string.Join(Environment.NewLine, checks));
    }

    [Test]
    public void Calibrate_FailsWhenOnlyOneMarkerIsVisible()
    {
      // The camera only sees the top half of the screen
      var screen = new[] { new ImagePoint(0, 0), new ImagePoint(288, 0), new ImagePoint(0, 240), new ImagePoint(288, 240) };
      var view = new[] { new ImagePoint(20, 15), new ImagePoint(340, 25), new ImagePoint(25, 470), new ImagePoint(335, 455) };
      Assert.That(Homography.TryFromPoints(screen, view, out var zoomed), Is.True);
      var camera = CreateCamera(new SyntheticCameraOptions { ScreenToCamera = zoomed });

      var rig = CameraCalibrator.Calibrate(Collect(camera, 0.2), g_options);

      Assert.That(rig.HasFailures, Is.True);
      Assert.That(rig.Checks.Single(c => c.Name == "zones").Level, Is.EqualTo(CameraCheckLevel.Fail));
    }
  }
}
