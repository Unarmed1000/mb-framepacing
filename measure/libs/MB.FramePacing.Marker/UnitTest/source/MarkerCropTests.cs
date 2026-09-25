//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Fast capture crop: the downscale factor, alignment and clamping, and that every marker drawn at the origin still decodes after the crop
//* and the downscale.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class MarkerCropTests
  {
    private static MarkerLock LockAt(int x, int y, int modulePx)
    {
      int size = MarkerRenderer.MarkerSizePx(modulePx);
      return new MarkerLock(new PixelRect(x, y, size, size), modulePx);
    }

    [TestCase(2, false, 1)]
    [TestCase(3, false, 1)]
    [TestCase(4, false, 1)]
    [TestCase(6, false, 2)]
    [TestCase(8, false, 2)]
    [TestCase(12, false, 4)]
    [TestCase(6, true, 1)]
    [TestCase(8, true, 2)]
    [TestCase(12, true, 3)]
    public void Factor_KeepsTheRecommendedStoredModuleSize(int modulePx, bool mjpeg, int expectedFactor)
    {
      var crop = MarkerCrop.For(LockAt(32, 32, modulePx), 3840, 2160, mjpeg);
      Assert.That(crop.Factor, Is.EqualTo(expectedFactor));
      Assert.That(crop.StoredModulePx, Is.GreaterThanOrEqualTo(mjpeg ? 4 : Math.Min(3, modulePx)));
    }

    [Test]
    public void Crop_IsAlignedToTheMarker_AndHoldsTheLargestStartMarker()
    {
      var markerLock = LockAt(33, 35, 6);
      var crop = MarkerCrop.For(markerLock, 1920, 1080);

      Assert.That(crop.Factor, Is.EqualTo(2));
      // Module edges stay on stored pixel edges: the marker origin is a whole number of factors into the crop
      foreach (int value in new[] { markerLock.Bounds.X - crop.Roi.X, markerLock.Bounds.Y - crop.Roi.Y, crop.Roi.Width, crop.Roi.Height })
        Assert.That(value % crop.Factor, Is.Zero);
      Assert.That(crop.Roi.Intersect(markerLock.SearchRegion), Is.EqualTo(markerLock.SearchRegion));
      Assert.That(crop.StoredWidth, Is.EqualTo(crop.Roi.Width / 2));
      // 1080p at 6 px per module: about 160x160 stored instead of 1920x1080
      Assert.That(crop.StoredWidth * crop.StoredHeight, Is.LessThan(1920 * 1080 / 50));
    }

    [Test]
    public void Crop_IsClampedToTheFrame()
    {
      var markerLock = LockAt(0, 0, 3);
      var crop = MarkerCrop.For(markerLock, 160, 150);
      Assert.That(crop.Roi.X, Is.Zero);
      Assert.That(crop.Roi.Y, Is.Zero);
      Assert.That(crop.Roi.Right, Is.LessThanOrEqualTo(160));
      Assert.That(crop.Roi.Bottom, Is.LessThanOrEqualTo(150));
      Assert.That(crop.Roi.Intersect(markerLock.Bounds), Is.EqualTo(markerLock.Bounds));
    }

    [Test]
    public void MarkerOutsideTheFrame_Throws()
    {
      Assert.Throws<InvalidOperationException>(() => MarkerCrop.For(LockAt(100, 100, 6), 200, 200));
      Assert.Throws<ArgumentOutOfRangeException>(() => MarkerCrop.For(new MarkerLock(new PixelRect(0, 0, 99, 99), 0), 200, 200));
    }

    [TestCase(3)]
    [TestCase(6)]
    [TestCase(8)]
    [TestCase(12)]
    public void FrameAndStartMarkers_DecodeAfterCropAndDownscale(int modulePx)
    {
      const int Width = 1280;
      const int Height = 720;
      int originX = 34;
      int originY = 30;
      var sourceLock = LockAt(originX, originY, modulePx);
      var crop = MarkerCrop.For(sourceLock, Width, Height);
      var decoder = new MarkerDecoder();

      var frame = new MarkerPayload(1234, 5_678_000, 7, MarkerKind.Frame);
      var start = new MarkerPayload(1, 0, 7, MarkerKind.SequenceStart);
      var metadata = new StartMetadata(638_000_000_000_000_000, new string('n', 64));
      foreach (var (payload, startMetadata) in new[] { (frame, (StartMetadata?)null), (start, metadata) })
      {
        var source = new GrayImage(Width, Height, 96);
        MarkerRenderer.Render(source, payload, originX, originY, modulePx, MarkerRenderer.RecommendedQuietZoneModules, startMetadata);
        var stored = Crop(source, crop.Roi).DownscaleBox(crop.Factor);
        Assert.That(stored.Width, Is.EqualTo(crop.StoredWidth));

        // The lock the analyzer finds in the stored image: the source lock moved into the crop and scaled
        float module = sourceLock.ModuleSizePx / crop.Factor;
        int size = (int)Math.Round(MarkerRenderer.MarkerSizePx(1) * module);
        var storedLock = new MarkerLock(
          new PixelRect((originX - crop.Roi.X) / crop.Factor, (originY - crop.Roi.Y) / crop.Factor, size, size),
          module
        );
        var decoded = decoder.DecodeLocked(stored, storedLock);
        Assert.That(decoded.IsDecoded, Is.True, $"{payload.Kind} at {modulePx} px per module");
        Assert.That(decoded.Payload, Is.EqualTo(payload));
      }
    }

    private static GrayImage Crop(GrayImage image, PixelRect rect)
    {
      var result = new GrayImage(rect.Width, rect.Height);
      for (int y = 0; y < rect.Height; ++y)
        image.Pixels.AsSpan(((rect.Y + y) * image.Stride) + rect.X, rect.Width).CopyTo(result.Row(y));
      return result;
    }
  }
}
