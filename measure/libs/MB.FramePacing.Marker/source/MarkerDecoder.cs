//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Finds and decodes frame markers in 8 bit grayscale frames using ZXing. Not thread safe: use one instance per thread.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using ZXing;
using ZXing.Common;
using ZXing.Multi.QrCode;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace MB.FramePacing.Marker
{
  public sealed class MarkerDecoder
  {
    private readonly QRCodeReader m_reader = new QRCodeReader();
    private readonly QRCodeMultiReader m_multiReader = new QRCodeMultiReader();
    private readonly Dictionary<DecodeHintType, object> m_hints;
    private readonly Dictionary<DecodeHintType, object> m_pureHints;
    private readonly ModuleGridSampler m_grid;
    private readonly bool m_sampleModuleGrid;

    /// <param name="tryHarder">Spend more time looking for the marker. Useful for the one-off search, not needed for a locked region.</param>
    /// <param name="sampleModuleGrid">
    /// EXPERIMENTAL camera captures: <see cref="DecodeLocked"/> first samples every module centre (<see cref="DecodeGrid"/>), which copes with
    /// the soft edges of rectified camera footage. Capture card captures keep the pixel exact pure barcode path.
    /// </param>
    public MarkerDecoder(bool tryHarder = false, bool sampleModuleGrid = false)
    {
      m_sampleModuleGrid = sampleModuleGrid;
      m_hints = new Dictionary<DecodeHintType, object>
      {
        [DecodeHintType.POSSIBLE_FORMATS] = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
        [DecodeHintType.CHARACTER_SET] = "ISO-8859-1",
      };
      if (tryHarder)
        m_hints[DecodeHintType.TRY_HARDER] = true;
      m_pureHints = new Dictionary<DecodeHintType, object>(m_hints) { [DecodeHintType.PURE_BARCODE] = true };
      m_grid = new ModuleGridSampler(m_hints);
    }

    /// <summary>
    /// Decode the marker at a known location. The frame marker is sampled directly (no finder pattern search, which is both faster and immune
    /// to the rare module patterns that confuse the detector); if that fails the detector searches the region a start marker may cover. With
    /// sampleModuleGrid (camera captures) every module centre is sampled first.
    /// </summary>
    public MarkerDecodeResult DecodeLocked(GrayImage image, MarkerLock markerLock)
    {
      // Camera captures: only the grid read, which rejects a marker the scanout has only partly replaced; ZXing's paths would decode it early
      if (m_sampleModuleGrid)
        return DecodeGrid(image, markerLock);

      var pure = markerLock.PureRegion.Intersect(image.Bounds);
      if (!pure.IsEmpty && pure == markerLock.PureRegion)
      {
        try
        {
          var result = m_reader.decode(CreateBitmap(image, pure), m_pureHints);
          if (result != null)
          {
            var decoded = ToDecodeResult(result, pure);
            if (decoded.IsDecoded)
              return decoded with { Bounds = markerLock.Bounds, ModuleSizePx = markerLock.ModuleSizePx };
          }
        }
        catch (ReaderException) { }
      }
      return Decode(image, markerLock.SearchRegion);
    }

    /// <summary>
    /// Modules of a camera decode that may differ from the re-rendered symbol (sampling noise). More means the scanout had only partly replaced
    /// the marker: error correction still decodes it, but its first-seen time would be early by up to the scanout time across the marker.
    /// </summary>
    public const double MaxModuleMismatchFraction = 0.02;

    /// <summary>
    /// Sample the module grid of the lock at every module centre (every marker kind has the same version and size). Only a marker whose every
    /// module matches its payload counts (up to <see cref="MaxModuleMismatchFraction"/>), so a partly replaced marker is not decoded early.
    /// </summary>
    public MarkerDecodeResult DecodeGrid(GrayImage image, MarkerLock markerLock)
    {
      if (markerLock.ModuleSizePx < 1.5)
        return MarkerDecodeResult.NotFound;
      double symbolX = markerLock.Bounds.X + (MarkerRenderer.RecommendedQuietZoneModules * markerLock.ModuleSizePx);
      double symbolY = markerLock.Bounds.Y + (MarkerRenderer.RecommendedQuietZoneModules * markerLock.ModuleSizePx);
      var bytes = m_grid.TryRead(image, symbolX, symbolY, markerLock.ModuleSizePx, markerLock.ModuleCount);
      if (bytes == null || !MarkerPayload.TryDecode(bytes, out var payload, out var start))
        return MarkerDecodeResult.NotFound;
      var expected = MarkerRenderer.GenerateModules(payload, start);
      if (m_grid.CountMismatches(expected) > MaxModuleMismatchFraction * expected.Size * expected.Size)
        return MarkerDecodeResult.NotFound;
      return new MarkerDecodeResult(MarkerDecodeStatus.Decoded, payload, start, markerLock.Bounds, markerLock.ModuleSizePx, Bytes: bytes);
    }

    /// <summary>Decode a single marker, optionally restricted to a region of the image.</summary>
    public MarkerDecodeResult Decode(GrayImage image, PixelRect? region = null)
    {
      var area = ClipRegion(image, region);
      if (area.IsEmpty)
        return MarkerDecodeResult.NotFound;

      Result? result;
      try
      {
        result = m_reader.decode(CreateBitmap(image, area), m_hints);
      }
      catch (ReaderException)
      {
        result = null;
      }
      return result == null ? MarkerDecodeResult.NotFound : ToDecodeResult(result, area);
    }

    /// <summary>
    /// Decode the main marker (frame, start or end) of an image: when the search finds the sync marker first, every marker is searched for the
    /// main one.
    /// </summary>
    public MarkerDecodeResult DecodeMain(GrayImage image, PixelRect? region = null)
    {
      var result = Decode(image, region);
      if (!result.IsDecoded || result.Payload.Kind != MarkerKind.Sync)
        return result;
      foreach (var other in DecodeAll(image, region))
      {
        if (other.IsDecoded && other.Payload.Kind != MarkerKind.Sync)
          return other;
      }
      return MarkerDecodeResult.NotFound;
    }

    /// <summary>Find and decode every marker in the image (the main and the sync marker), sorted top to bottom.</summary>
    public List<MarkerDecodeResult> DecodeAll(GrayImage image, PixelRect? region = null)
    {
      var list = new List<MarkerDecodeResult>();
      var area = ClipRegion(image, region);
      if (area.IsEmpty)
        return list;

      Result[]? results;
      try
      {
        results = m_multiReader.decodeMultiple(CreateBitmap(image, area), m_hints);
      }
      catch (ReaderException)
      {
        results = null;
      }
      if (results != null)
      {
        foreach (var result in results)
          list.Add(ToDecodeResult(result, area));
      }
      list.Sort((lhs, rhs) => lhs.Bounds.Y != rhs.Bounds.Y ? lhs.Bounds.Y.CompareTo(rhs.Bounds.Y) : lhs.Bounds.X.CompareTo(rhs.Bounds.X));
      return list;
    }

    /// <summary>
    /// Find up to <paramref name="maxCount"/> markers one at a time: decode, cover the marker on a copy of the image, repeat. When a search
    /// finds nothing it is repeated on overlapping halves (two levels deep), since finder patterns of several markers can confuse one search.
    /// Slower than <see cref="DecodeAll"/> but it works on camera footage, where ZXing's multi-marker finder rejects markers seen at an angle.
    /// Only decoded markers are returned, sorted top to bottom.
    /// </summary>
    public List<MarkerDecodeResult> DecodeEach(GrayImage image, int maxCount, PixelRect? region = null)
    {
      var list = new List<MarkerDecodeResult>();
      var area = ClipRegion(image, region);
      if (area.IsEmpty)
        return list;

      GrayImage? work = null;
      var pending = new Queue<(PixelRect Area, int Depth)>();
      pending.Enqueue((area, 0));
      while (list.Count < maxCount && pending.Count > 0)
      {
        var (current, depth) = pending.Dequeue();
        var result = Decode(work ?? image, current);
        if (result.IsDecoded && !result.Bounds.IsEmpty)
        {
          list.Add(result);
          if (work == null)
          {
            work = new GrayImage(image.Width, image.Height);
            for (int y = 0; y < image.Height; ++y)
              image.Row(y).CopyTo(work.Row(y));
          }
          // Grey out the whole marker (quiet zone included) so the finder patterns are gone but no new edges look like one
          work.FillRect(result.Bounds, 128);
          pending.Enqueue((current, depth));
          continue;
        }
        if (depth < 2)
        {
          foreach (var half in OverlappingHalves(current))
            pending.Enqueue((half, depth + 1));
        }
      }
      list.Sort((lhs, rhs) => lhs.Bounds.Y != rhs.Bounds.Y ? lhs.Bounds.Y.CompareTo(rhs.Bounds.Y) : lhs.Bounds.X.CompareTo(rhs.Bounds.X));
      return list;
    }

    /// <summary>Two halves along the longer side, each two thirds long, so a marker cut by one lies whole in the other.</summary>
    private static PixelRect[] OverlappingHalves(PixelRect area)
    {
      if (area.Height >= area.Width)
      {
        int part = (2 * area.Height) / 3;
        return new[] { area with { Height = part }, area with { Y = area.Bottom - part, Height = part } };
      }
      int width = (2 * area.Width) / 3;
      return new[] { area with { Width = width }, area with { X = area.Right - width, Width = width } };
    }

    private static PixelRect ClipRegion(GrayImage image, PixelRect? region) => region.HasValue ? region.Value.Intersect(image.Bounds) : image.Bounds;

    private static BinaryBitmap CreateBitmap(GrayImage image, PixelRect area)
    {
      // PlanarYUVLuminanceSource reads the Y plane in place: dataWidth doubles as the stride, left/top/width/height select the region.
      var source = new PlanarYUVLuminanceSource(image.Pixels, image.Stride, image.Height, area.X, area.Y, area.Width, area.Height, false);
      return new BinaryBitmap(new HybridBinarizer(source));
    }

    private static MarkerDecodeResult ToDecodeResult(Result result, PixelRect area)
    {
      var (bounds, moduleSize) = EstimateBounds(result.ResultPoints, area);
      var geometry = ToGeometry(result.ResultPoints, area);
      var bytes = ExtractBytes(result);
      if (bytes == null || !MarkerPayload.TryDecode(bytes, out var payload, out var start))
        return new MarkerDecodeResult(MarkerDecodeStatus.InvalidPayload, default, null, bounds, moduleSize, geometry, bytes);
      return new MarkerDecodeResult(MarkerDecodeStatus.Decoded, payload, start, bounds, moduleSize, geometry, bytes);
    }

    /// <summary>ZXing reports [bottom-left, top-left, top-right, alignment]; the alignment pattern is missing when the detector did not find it.</summary>
    private static MarkerGeometry? ToGeometry(ResultPoint[]? points, PixelRect area)
    {
      if (points == null || points.Length < 4 || points[3] == null)
        return null;
      ImagePoint At(int index) => new ImagePoint(points[index].X + area.X, points[index].Y + area.Y);
      return new MarkerGeometry(At(1), At(2), At(0), At(3));
    }

    private static byte[]? ExtractBytes(Result result)
    {
      if (
        result.ResultMetadata != null
        && result.ResultMetadata.TryGetValue(ResultMetadataType.BYTE_SEGMENTS, out var value)
        && value is IList<byte[]> segments
      )
      {
        int length = 0;
        foreach (var segment in segments)
          length += segment.Length;
        var bytes = new byte[length];
        int offset = 0;
        foreach (var segment in segments)
        {
          segment.CopyTo(bytes, offset);
          offset += segment.Length;
        }
        return bytes;
      }
      // Fallback: the text was decoded as ISO-8859-1, which maps chars 1:1 back to bytes
      return result.Text != null ? System.Text.Encoding.Latin1.GetBytes(result.Text) : null;
    }

    /// <summary>
    /// ZXing reports the finder pattern centres as [bottom-left, top-left, top-right, (alignment)]. Each finder carries an estimated module size;
    /// the finder centres sit 3.5 modules inside the symbol corners, so the marker bounds (including the quiet zone) follow from the centres.
    /// </summary>
    private static (PixelRect Bounds, double ModuleSize) EstimateBounds(ResultPoint[]? points, PixelRect area)
    {
      if (points == null || points.Length < 3)
        return (area, 0);

      var bottomLeft = points[0];
      var topLeft = points[1];
      var topRight = points[2];
      double moduleSize = EstimateModuleSize(bottomLeft, topLeft, topRight);
      if (moduleSize <= 0)
        return (area, 0);

      double minX = Math.Min(Math.Min(topLeft.X, topRight.X), bottomLeft.X);
      double minY = Math.Min(Math.Min(topLeft.Y, topRight.Y), bottomLeft.Y);
      double maxX = Math.Max(Math.Max(topLeft.X, topRight.X), bottomLeft.X);
      double maxY = Math.Max(Math.Max(topLeft.Y, topRight.Y), bottomLeft.Y);
      double margin = (3.5 + MarkerRenderer.RecommendedQuietZoneModules) * moduleSize;

      int left = (int)Math.Floor(minX - margin) + area.X;
      int top = (int)Math.Floor(minY - margin) + area.Y;
      int right = (int)Math.Ceiling(maxX + margin) + area.X;
      int bottom = (int)Math.Ceiling(maxY + margin) + area.Y;
      return (new PixelRect(left, top, right - left, bottom - top), moduleSize);
    }

    private static double EstimateModuleSize(ResultPoint bottomLeft, ResultPoint topLeft, ResultPoint topRight)
    {
      // First estimate from the finder patterns themselves, then refine using the finder distance which must be (moduleCount - 7) modules
      double estimate = 0;
      int count = 0;
      foreach (var point in new[] { bottomLeft, topLeft, topRight })
      {
        if (point is FinderPattern finder && finder.EstimatedModuleSize > 0)
        {
          estimate += finder.EstimatedModuleSize;
          ++count;
        }
      }
      double distance = ((double)ResultPoint.distance(topLeft, topRight) + ResultPoint.distance(topLeft, bottomLeft)) * 0.5;
      if (count == 0)
        return distance / (MarkerRenderer.QrModuleCount - 7);
      estimate /= count;

      // QR symbol sizes are 17 + 4 * version
      int version = Math.Clamp((int)Math.Round(((distance / estimate) + 7 - 17) / 4.0), 1, 40);
      return distance / ((17 + (4 * version)) - 7);
    }
  }
}
