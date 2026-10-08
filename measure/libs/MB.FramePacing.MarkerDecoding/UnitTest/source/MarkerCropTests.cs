//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Fast capture crop: the downscale factor, alignment and clamping, and that every marker drawn at the origin still decodes after the crop
//* and the downscale.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Linq;
using NUnit.Framework;

namespace MB.FramePacing.MarkerDecoding.UnitTest
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
      Assert.Throws<MarkerRegionException>(() => MarkerCrop.For(LockAt(100, 100, 6), 200, 200));
      Assert.Throws<MarkerRegionException>(() => MarkerCrop.For(LockAt(32, 32, 3), SyncLockAt(32, 150, 3), 320, 200), "the sync marker");
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

      var frame = new MarkerPayload(MarkerKind.Frame, 7, 1234, MB.FramePacing.Marker.MarkerFlags.NoFlags, new NanosecondTimeSpan(567_800_000));
      var start = new MarkerPayload(MarkerKind.SequenceStart, 7, 1, MB.FramePacing.Marker.MarkerFlags.NoFlags, new NanosecondTimeSpan(0));
      var metadata = new StartMetadata(638_000_000_000_000_000, new MB.FramePacing.Marker.SequenceId(0xFEDC_BA98_7654_3210, 0x0123_4567_89AB_CDEF));
      foreach (var (payload, startMetadata) in new[] { (frame, (StartMetadata?)null), (start, metadata) })
      {
        var source = new GrayImage(Width, Height, 96);
        MarkerRenderer.Render(source, payload, originX, originY, modulePx, MarkerRenderer.RecommendedQuietZoneModules, startMetadata);
        var stored = Crop(source, crop.Roi).DownscaleBox(crop.Factor);
        Assert.That(stored.Width, Is.EqualTo(crop.StoredWidth));

        // The lock the analyzer finds in the stored image: the source lock moved into the crop and scaled
        double module = sourceLock.ModuleSizePx / crop.Factor;
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

    [TestCase(3)]
    [TestCase(6)]
    [TestCase(8)]
    [TestCase(12)]
    public void BothMarkers_GetACropEach_AndDecodeFromTheStackedFrame(int modulePx)
    {
      // A 2160p output: room between the two markers at every module size (12 px modules on 1080p would overlap)
      const int Width = 3840;
      const int Height = 2160;
      var mainLock = LockAt(32, 32, modulePx);
      int syncSize = MarkerRenderer.MarkerSizePx(modulePx, MarkerRenderer.RecommendedQuietZoneModules, MarkerKind.Sync);
      var syncLock = SyncLockAt(32, Height - 32 - syncSize, modulePx);

      var crop = MarkerCrop.For(mainLock, syncLock, Width, Height);

      Assert.That(crop.HasSyncRoi, Is.True);
      Assert.That(crop.Roi, Is.EqualTo(MarkerCrop.For(mainLock, Width, Height).Roi), "the main marker's crop is what it is alone");
      // Each crop on its own marker's grid, each a whole number of stored pixels
      foreach (
        int value in new[]
        {
          syncLock.Bounds.X - crop.SyncRoi.X,
          syncLock.Bounds.Y - crop.SyncRoi.Y,
          crop.SyncRoi.Width,
          crop.SyncRoi.Height,
          crop.Roi.Width,
          crop.Roi.Height,
        }
      )
        Assert.That(value % crop.Factor, Is.Zero);
      Assert.That(crop.SyncRoi.Intersect(syncLock.SearchRegion), Is.EqualTo(syncLock.SearchRegion));
      Assert.That(crop.Roi.Intersect(crop.SyncRoi).IsEmpty, Is.True);
      Assert.That(
        (crop.StoredWidth, crop.StoredHeight),
        Is.EqualTo((crop.Roi.Width / crop.Factor, (crop.Roi.Height + crop.SyncRoi.Height) / crop.Factor)),
        "the sync marker's crop is the narrower one"
      );

      // The stored frame as ffmpeg makes it: each crop downscaled, the narrower one padded with white, one below the other
      var main = new MarkerPayload(MarkerKind.Frame, 7, 1234, MB.FramePacing.Marker.MarkerFlags.NoFlags, new NanosecondTimeSpan(567_800_000));
      var sync = new MarkerPayload(MarkerKind.Sync, 7, 1234, MB.FramePacing.Marker.MarkerFlags.NoFlags, new NanosecondTimeSpan(0));
      var source = new GrayImage(Width, Height, 96);
      MarkerRenderer.Render(source, main, mainLock.Bounds.X, mainLock.Bounds.Y, modulePx);
      MarkerRenderer.Render(source, sync, syncLock.Bounds.X, syncLock.Bounds.Y, modulePx);
      var top = Crop(source, crop.Roi).DownscaleBox(crop.Factor);
      var bottom = Crop(source, crop.SyncRoi).DownscaleBox(crop.Factor);
      var stored = new GrayImage(crop.StoredWidth, crop.StoredHeight, 255);
      for (int y = 0; y < top.Height; ++y)
        top.Row(y).CopyTo(stored.Row(y));
      for (int y = 0; y < bottom.Height; ++y)
        bottom.Row(y).CopyTo(stored.Row(top.Height + y));

      var found = new MarkerDecoder(tryHarder: true).DecodeAll(stored).Where(r => r.IsDecoded).Select(r => r.Payload).ToList();
      Assert.That(found, Is.EquivalentTo(new[] { main, sync }), $"{modulePx} px per module");
    }

    [Test]
    public void MarkersWhoseRegionsOverlap_GetOneCropThatHoldsBoth()
    {
      // A frame so small that the sync marker is right below the main marker
      var mainLock = LockAt(16, 16, 3);
      var syncLock = SyncLockAt(16, 16 + mainLock.Bounds.Height, 3);
      var crop = MarkerCrop.For(mainLock, syncLock, 320, 300);

      Assert.That(crop.HasSyncRoi, Is.False);
      Assert.That(crop.Roi.Intersect(mainLock.Bounds), Is.EqualTo(mainLock.Bounds));
      Assert.That(crop.Roi.Intersect(syncLock.Bounds), Is.EqualTo(syncLock.Bounds));
      Assert.That((crop.StoredWidth, crop.StoredHeight), Is.EqualTo((crop.Roi.Width / crop.Factor, crop.Roi.Height / crop.Factor)));
    }

    private static MarkerLock SyncLockAt(int x, int y, int modulePx) => MarkerLock.At(x, y, modulePx, MarkerKind.Sync);

    private static GrayImage Crop(GrayImage image, PixelRect rect)
    {
      var result = new GrayImage(rect.Width, rect.Height);
      for (int y = 0; y < rect.Height; ++y)
        image.Pixels.AsSpan(((rect.Y + y) * image.Stride) + rect.X, rect.Width).CopyTo(result.Row(y));
      return result;
    }
  }
}
