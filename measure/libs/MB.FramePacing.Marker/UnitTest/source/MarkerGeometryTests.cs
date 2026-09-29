//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A marker filmed at an angle: the detector's finder and alignment points recover the module to camera transform.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class MarkerGeometryTests
  {
    private const int ScreenSize = 480;
    private const int Origin = 40;
    private const int ModulePx = 8;

    // A camera looking at the screen from the lower right: keystone, a little rotation, non-integer scale
    private static readonly ImagePoint[] g_moderateView = { new(30, 22), new(610, 40), new(40, 460), new(600, 440) };

    // A steeper view: ZXing's finder search predicts the alignment pattern too far off across a 41 module symbol and misses about half the
    // markers (doc/camera-status.md, known issues). Run explicitly while working on the camera detector.
    private static readonly ImagePoint[] g_steepView = { new(30, 22), new(610, 64), new(52, 452), new(588, 418) };

    [Test]
    public void Decode_ReportsGeometryThatMatchesTheCameraTransform() => AssertGeometryMatches(g_moderateView);

    [Test]
    [Explicit("Known camera detector limit at steep angles; the target for the camera detector work")]
    public void Decode_SteepView_ReportsGeometryThatMatchesTheCameraTransform() => AssertGeometryMatches(g_steepView);

    private static void AssertGeometryMatches(ImagePoint[] view)
    {
      var screenToCamera = ScreenToCamera(view);
      var camera = FilmScreen(screenToCamera, new MarkerPayload(7, 70, 1));

      var result = new MarkerDecoder(tryHarder: true).Decode(camera);

      Assert.That(result.IsDecoded, Is.True);
      Assert.That(result.Geometry.HasValue, Is.True);
      Assert.That(result.Geometry!.Value.TryGetModuleToImage(MarkerRenderer.QrModuleCount, out var measured), Is.True);

      // The symbol corners and centre, expected vs measured, in camera pixels
      var expected = Homography.Multiply(screenToCamera, ModuleToScreen());
      foreach (var module in new ImagePoint[] { new(0, 0), new(41, 0), new(0, 41), new(41, 41), new(20.5, 20.5) })
      {
        var want = expected.Map(module);
        var got = measured.Map(module);
        Assert.That(ImagePoint.Distance(want, got), Is.LessThan(1.0), $"module {module}: expected {want}, measured {got}");
      }
    }

    [Test]
    public void DecodeLocked_PureFastPath_HasNoGeometry()
    {
      var image = new GrayImage(ScreenSize, ScreenSize, 96);
      MarkerRenderer.Render(image, new MarkerPayload(1, 2, 3), Origin, Origin, ModulePx);
      var markerLock = new MarkerLock(
        new PixelRect(Origin, Origin, MarkerRenderer.MarkerSizePx(ModulePx), MarkerRenderer.MarkerSizePx(ModulePx)),
        ModulePx
      );

      var result = new MarkerDecoder().DecodeLocked(image, markerLock);

      Assert.That(result.IsDecoded, Is.True);
      Assert.That(result.Geometry, Is.Null);
    }

    private static Homography ModuleToScreen()
    {
      double symbol = Origin + (MarkerRenderer.RecommendedQuietZoneModules * ModulePx);
      return new Homography(ModulePx, 0, symbol, 0, ModulePx, symbol, 0, 0);
    }

    /// <summary>The transform from the screen to where its corners appear in the camera image.</summary>
    private static Homography ScreenToCamera(ImagePoint[] camera)
    {
      var screen = new ImagePoint[] { new(0, 0), new(ScreenSize, 0), new(0, ScreenSize), new(ScreenSize, ScreenSize) };
      Assert.That(Homography.TryFromPoints(screen, camera, out var homography), Is.True);
      return homography;
    }

    private static GrayImage FilmScreen(Homography screenToCamera, MarkerPayload payload)
    {
      var screen = new GrayImage(ScreenSize, ScreenSize, 96);
      MarkerRenderer.Render(screen, payload, Origin, Origin, ModulePx);
      Assert.That(screenToCamera.TryInvert(out var cameraToScreen), Is.True);
      var camera = new GrayImage(640, 480);
      ImageWarp.Warp(screen, cameraToScreen, camera, background: 20);
      return camera;
    }
  }
}
