//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The synthetic high speed camera: its frames decode, the markers sit where the ground truth transform says, and the rolling scanout shows
//* the new frame in the top zone before the bottom zone.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using MB.FramePacing.Capture.Synthetic;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class SyntheticCameraTests
  {
    private static SyntheticCamera CreateCamera(SyntheticCameraOptions? cameraOptions = null, double fps = 1000, double refreshHz = 60) =>
      new SyntheticCamera(
        new SyntheticScenario(
          new SyntheticScenarioOptions
          {
            CaptureFps = fps,
            RefreshHz = refreshHz,
            StartMarkerSeconds = 0.1,
            RunSeconds = 0.6,
            EndMarkerSeconds = 0.1,
          }
        ),
        cameraOptions ?? new SyntheticCameraOptions()
      );

    /// <summary>The first capture whose exposure starts at least <paramref name="afterVsync"/> after a vsync, at or after <paramref name="seconds"/>.</summary>
    private static long CaptureAfterVsync(SyntheticCamera camera, double seconds, double afterVsync)
    {
      double refresh = camera.Scenario.RefreshIntervalTicks;
      double vsync = Math.Ceiling(seconds * TimeSpan.TicksPerSecond / refresh) * refresh;
      double target = vsync + (afterVsync * TimeSpan.TicksPerSecond);
      for (long i = 0; i < camera.CaptureCount; ++i)
      {
        if (camera.TrueTicks(i) >= target)
          return i;
      }
      throw new InvalidOperationException("The scenario is too short");
    }

    [Test]
    public void MidRefresh_TopZoneShowsTheNewFrame_BottomZoneTheOld()
    {
      var camera = CreateCamera(new SyntheticCameraOptions { LensDistortion = 0 });
      long capture = CaptureAfterVsync(camera, 0.4, 0.010);
      var frame = new GrayImage(camera.Options.CameraWidth, camera.Options.CameraHeight);
      camera.Render(capture, frame);
      Dump(frame, "camera-mid-refresh.pgm");

      var results = new MarkerDecoder(tryHarder: true).DecodeEach(frame, 2);

      Assert.That(results, Has.Count.EqualTo(2));
      int shown = camera.Scenario.PresentedIndexAtTicks((long)camera.TrueTicks(capture));
      Assert.That(results[0].Payload, Is.EqualTo(camera.Scenario.PresentedFrames[shown].Payload));
      Assert.That(results[1].Payload, Is.EqualTo(camera.Scenario.PresentedFrames[shown - 1].Payload));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void Geometry_MatchesTheTrueTransform(int zone)
    {
      var camera = CreateCamera(new SyntheticCameraOptions { LensDistortion = 0 });
      var frame = new GrayImage(camera.Options.CameraWidth, camera.Options.CameraHeight);
      camera.Render(CaptureAfterVsync(camera, 0.4, 0.010), frame);

      var results = new MarkerDecoder(tryHarder: true).DecodeEach(frame, 2);
      Assert.That(results, Has.Count.EqualTo(2));
      Assert.That(results[zone].Geometry.HasValue, Is.True);
      Assert.That(results[zone].Geometry!.Value.TryGetModuleToImage(MarkerRenderer.FrameQrModuleCount, out var measured), Is.True);

      // The raw detector points of a single frame; the camera calibrator refines them further
      var truth = camera.ZoneModuleToCamera(zone);
      foreach (var module in new ImagePoint[] { new(0, 0), new(25, 0), new(0, 25), new(25, 25), new(12.5, 12.5) })
        Assert.That(ImagePoint.Distance(truth.Map(module), measured.Map(module)), Is.LessThan(1.5), $"module {module}");
    }

    [TestCase(0, 0.0)]
    [TestCase(1, 0.0)]
    [TestCase(0, 0.03)]
    [TestCase(1, 0.03)]
    public void Refiner_ImprovesTheDetectorGeometry(int zone, double lensDistortion)
    {
      var camera = CreateCamera(new SyntheticCameraOptions { LensDistortion = lensDistortion });
      var frame = new GrayImage(camera.Options.CameraWidth, camera.Options.CameraHeight);
      camera.Render(CaptureAfterVsync(camera, 0.4, 0.010), frame);
      var results = new MarkerDecoder(tryHarder: true).DecodeEach(frame, 2);
      Assert.That(results, Has.Count.EqualTo(2));
      Assert.That(results[zone].Geometry!.Value.TryGetModuleToImage(MarkerRenderer.FrameQrModuleCount, out var detected), Is.True);

      var refined = HomographyRefiner.Refine(frame, detected, MarkerRenderer.GenerateModules(results[zone].Payload));

      double before = 0;
      double after = 0;
      foreach (var module in new ImagePoint[] { new(0, 0), new(25, 0), new(0, 25), new(25, 25), new(12.5, 12.5) })
      {
        var truth = camera.ZoneModuleToObserved(zone, module);
        before = Math.Max(before, ImagePoint.Distance(truth, detected.Map(module)));
        after = Math.Max(after, ImagePoint.Distance(truth, refined.ModuleToImage.Map(module)));
      }
      TestContext.Out.WriteLine(
        $"zone {zone} lens {lensDistortion}: detector {before:0.000} px, refined {after:0.000} px, black {refined.Black:0} white {refined.White:0} "
          + $"residual {refined.RelativeResidual:0.000} converged {refined.Converged}"
      );
      Assert.That(refined.Converged, Is.True);
      // Lens distortion bends the marker slightly, which one homography per zone can not follow exactly
      Assert.That(after, Is.LessThan(lensDistortion > 0 ? 0.4 : 0.25));
      Assert.That(refined.Black, Is.EqualTo(camera.Options.ScreenBlack).Within(15));
      Assert.That(refined.White, Is.EqualTo(camera.Options.ScreenWhite).Within(15));
    }

    [Test]
    public void RollingScanout_BottomZoneSeesEachFrameLater()
    {
      var camera = CreateCamera();
      var decoder = new MarkerDecoder(tryHarder: true);
      var frame = new GrayImage(camera.Options.CameraWidth, camera.Options.CameraHeight);
      var firstSeen = new[] { new Dictionary<ulong, long>(), new Dictionary<ulong, long>() };
      long begin = CaptureAfterVsync(camera, 0.2, 0);
      for (long i = begin; i < begin + 120; ++i)
      {
        camera.Render(i, frame);
        foreach (var result in decoder.DecodeEach(frame, 2))
        {
          if (!result.IsDecoded)
            continue;
          int zone = result.Bounds.Y < frame.Height / 2 ? 0 : 1;
          firstSeen[zone].TryAdd(result.Payload.FrameIndex, camera.CameraTicks(i));
        }
      }

      var delays = new List<double>();
      foreach (var (frameIndex, top) in firstSeen[0])
      {
        if (firstSeen[1].TryGetValue(frameIndex, out long bottom))
          delays.Add((bottom - top) / (double)TimeSpan.TicksPerMillisecond);
      }
      Assert.That(delays, Has.Count.GreaterThanOrEqualTo(5));
      delays.Sort();
      double median = delays[delays.Count / 2];
      double expected = (camera.ZoneScanTicks(1) - camera.ZoneScanTicks(0)) / TimeSpan.TicksPerMillisecond;
      Assert.That(median, Is.EqualTo(expected).Within(1.5), $"delays {string.Join(", ", delays)}");
    }

    /// <summary>Set MB_FRAMEPACING_TEST_DUMP to a folder to look at the rendered frames.</summary>
    private static void Dump(GrayImage image, string name)
    {
      var folder = Environment.GetEnvironmentVariable("MB_FRAMEPACING_TEST_DUMP");
      if (!string.IsNullOrEmpty(folder))
        PgmFile.Write(Path.Combine(folder, name), image);
    }
  }
}
