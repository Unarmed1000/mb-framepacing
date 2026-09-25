//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A marker filmed at an angle: the detector's finder and alignment points recover the module to camera transform.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class MarkerGeometryTests
  {
    private const int ScreenSize = 400;
    private const int Origin = 40;
    private const int ModulePx = 8;

    [Test]
    public void Decode_ReportsGeometryThatMatchesTheCameraTransform()
    {
      var screenToCamera = ScreenToCamera();
      var camera = FilmScreen(screenToCamera, new MarkerPayload(7, 70, 1));

      var result = new MarkerDecoder(tryHarder: true).Decode(camera);

      Assert.That(result.IsDecoded, Is.True);
      Assert.That(result.Geometry.HasValue, Is.True);
      Assert.That(result.Geometry!.Value.TryGetModuleToImage(MarkerRenderer.FrameQrModuleCount, out var measured), Is.True);

      // The symbol corners and centre, expected vs measured, in camera pixels
      var expected = Homography.Multiply(screenToCamera, ModuleToScreen());
      foreach (var module in new ImagePoint[] { new(0, 0), new(25, 0), new(0, 25), new(25, 25), new(12.5, 12.5) })
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

    /// <summary>A camera looking at the screen from the lower right: keystone, a little rotation, non-integer scale.</summary>
    private static Homography ScreenToCamera()
    {
      var screen = new ImagePoint[] { new(0, 0), new(ScreenSize, 0), new(0, ScreenSize), new(ScreenSize, ScreenSize) };
      var camera = new ImagePoint[] { new(30, 22), new(610, 64), new(52, 452), new(588, 418) };
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
