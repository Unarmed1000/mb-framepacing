//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Finds and decodes frame markers in 8 bit grayscale frames using ZXing. Not thread safe: use one instance per thread.
//*
//* (c) 2026 Mana Battery
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
  public enum MarkerDecodeStatus
  {
    /// <summary>A marker with a valid payload was decoded.</summary>
    Decoded,

    /// <summary>No readable QR code (missing, torn, blended between two frames or too small).</summary>
    NotFound,

    /// <summary>A QR code was read but it is not a frame marker (wrong length, magic or format version).</summary>
    InvalidPayload,
  }

  /// <summary>The outcome of decoding one marker.</summary>
  /// <param name="Start">The start metadata when the marker is a <see cref="MarkerKind.SequenceStart"/> marker, otherwise null.</param>
  /// <param name="Bounds">Marker bounds including the quiet zone, in image pixels. Only valid when a QR code was found.</param>
  /// <param name="ModuleSizePx">Measured size of one QR module in image pixels. Only valid when a QR code was found.</param>
  public readonly record struct MarkerDecodeResult(
    MarkerDecodeStatus Status,
    MarkerPayload Payload,
    StartMetadata? Start,
    PixelRect Bounds,
    float ModuleSizePx
  )
  {
    public static readonly MarkerDecodeResult NotFound = new MarkerDecodeResult(MarkerDecodeStatus.NotFound, default, null, default, 0);

    public bool IsDecoded => Status == MarkerDecodeStatus.Decoded;
  }

  /// <summary>
  /// Where a frame marker was found: its bounds (including the quiet zone) and module size in image pixels. Once the analyzer knows this it
  /// decodes with <see cref="MarkerDecoder.DecodeLocked"/>, which samples the module grid directly instead of searching for finder patterns.
  /// </summary>
  public readonly record struct MarkerLock(PixelRect Bounds, float ModuleSizePx)
  {
    /// <summary>Region that holds any marker drawn at the same origin, including the largest start marker.</summary>
    public PixelRect SearchRegion
    {
      get
      {
        int margin = (int)Math.Ceiling(2 * ModuleSizePx);
        int size = (int)Math.Ceiling(MarkerRenderer.MaxMarkerSizePx(1) * ModuleSizePx);
        return new PixelRect(Bounds.X - margin, Bounds.Y - margin, size + (2 * margin), size + (2 * margin));
      }
    }

    /// <summary>The frame marker with 1.5 modules of the quiet zone trimmed off, so the crop is white all around the symbol.</summary>
    internal PixelRect PureRegion
    {
      get
      {
        int inset = (int)Math.Round(1.5f * ModuleSizePx);
        return new PixelRect(Bounds.X + inset, Bounds.Y + inset, Bounds.Width - (2 * inset), Bounds.Height - (2 * inset));
      }
    }
  }

  public sealed class MarkerDecoder
  {
    private readonly QRCodeReader m_reader = new QRCodeReader();
    private readonly QRCodeMultiReader m_multiReader = new QRCodeMultiReader();
    private readonly Dictionary<DecodeHintType, object> m_hints;
    private readonly Dictionary<DecodeHintType, object> m_pureHints;

    /// <param name="tryHarder">Spend more time looking for the marker. Useful for the one-off search, not needed for a locked region.</param>
    public MarkerDecoder(bool tryHarder = false)
    {
      m_hints = new Dictionary<DecodeHintType, object>
      {
        [DecodeHintType.POSSIBLE_FORMATS] = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
        [DecodeHintType.CHARACTER_SET] = "ISO-8859-1",
      };
      if (tryHarder)
        m_hints[DecodeHintType.TRY_HARDER] = true;
      m_pureHints = new Dictionary<DecodeHintType, object>(m_hints) { [DecodeHintType.PURE_BARCODE] = true };
    }

    /// <summary>
    /// Decode the marker at a known location. The frame marker is sampled directly (no finder pattern search, which is both faster and immune
    /// to the rare module patterns that confuse the detector); if that fails the detector searches the region a start marker may cover.
    /// </summary>
    public MarkerDecodeResult DecodeLocked(GrayImage image, MarkerLock markerLock)
    {
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

    /// <summary>Find and decode every marker in the image (region search for tearing markers), sorted top to bottom.</summary>
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
      var bytes = ExtractBytes(result);
      if (bytes == null || !MarkerPayload.TryDecode(bytes, out var payload, out var start))
        return new MarkerDecodeResult(MarkerDecodeStatus.InvalidPayload, default, null, bounds, moduleSize);
      return new MarkerDecodeResult(MarkerDecodeStatus.Decoded, payload, start, bounds, moduleSize);
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
    private static (PixelRect Bounds, float ModuleSize) EstimateBounds(ResultPoint[]? points, PixelRect area)
    {
      if (points == null || points.Length < 3)
        return (area, 0);

      var bottomLeft = points[0];
      var topLeft = points[1];
      var topRight = points[2];
      float moduleSize = EstimateModuleSize(bottomLeft, topLeft, topRight);
      if (moduleSize <= 0)
        return (area, 0);

      float minX = Math.Min(Math.Min(topLeft.X, topRight.X), bottomLeft.X);
      float minY = Math.Min(Math.Min(topLeft.Y, topRight.Y), bottomLeft.Y);
      float maxX = Math.Max(Math.Max(topLeft.X, topRight.X), bottomLeft.X);
      float maxY = Math.Max(Math.Max(topLeft.Y, topRight.Y), bottomLeft.Y);
      float margin = (3.5f + MarkerRenderer.RecommendedQuietZoneModules) * moduleSize;

      int left = (int)Math.Floor(minX - margin) + area.X;
      int top = (int)Math.Floor(minY - margin) + area.Y;
      int right = (int)Math.Ceiling(maxX + margin) + area.X;
      int bottom = (int)Math.Ceiling(maxY + margin) + area.Y;
      return (new PixelRect(left, top, right - left, bottom - top), moduleSize);
    }

    private static float EstimateModuleSize(ResultPoint bottomLeft, ResultPoint topLeft, ResultPoint topRight)
    {
      // First estimate from the finder patterns themselves, then refine using the finder distance which must be (moduleCount - 7) modules
      float estimate = 0;
      int count = 0;
      foreach (var point in new[] { bottomLeft, topLeft, topRight })
      {
        if (point is FinderPattern finder && finder.EstimatedModuleSize > 0)
        {
          estimate += finder.EstimatedModuleSize;
          ++count;
        }
      }
      float distance = (ResultPoint.distance(topLeft, topRight) + ResultPoint.distance(topLeft, bottomLeft)) * 0.5f;
      if (count == 0)
        return distance / (MarkerRenderer.FrameQrModuleCount - 7);
      estimate /= count;

      // QR symbol sizes are 17 + 4 * version
      int version = Math.Clamp((int)Math.Round(((distance / estimate) + 7 - 17) / 4f), 1, 40);
      return distance / ((17 + (4 * version)) - 7);
    }
  }
}
