//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads a marker whose module grid is known (a lock) by sampling every module centre directly and thresholding against the finder patterns'
//* own black and white levels. Unlike ZXing's pure barcode mode it does not look for the outermost black pixels, so soft edges, low contrast
//* single modules (camera footage after rectification, MJPEG) and noise in the quiet zone do not throw it off.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using ZXing;
using ZXing.Common;
using ZXing.QrCode.Internal;

namespace MB.FramePacing.Marker
{
  /// <summary>
  /// Reads a marker whose module grid is known (a lock) by sampling every module centre and thresholding against the finder patterns' own black
  /// and white levels. Not thread safe: one instance per <see cref="MarkerDecoder"/>.
  /// </summary>
  internal sealed class ModuleGridSampler
  {
    private readonly Decoder m_decoder = new Decoder();
    private readonly IDictionary<DecodeHintType, object> m_hints;
    private readonly Dictionary<int, BitMatrix> m_matrices = new Dictionary<int, BitMatrix>();

    public ModuleGridSampler(IDictionary<DecodeHintType, object> hints)
    {
      m_hints = hints;
    }

    /// <summary>
    /// Sample the grid of a <paramref name="moduleCount"/> module symbol whose top-left corner is at (<paramref name="symbolX"/>,
    /// <paramref name="symbolY"/>) with <paramref name="moduleSizePx"/> pixel modules, and decode it. Returns the raw bytes, or null.
    /// </summary>
    public byte[]? TryRead(GrayImage image, double symbolX, double symbolY, double moduleSizePx, int moduleCount)
    {
      double extent = moduleCount * moduleSizePx;
      if (symbolX < 0 || symbolY < 0 || symbolX + extent > image.Width || symbolY + extent > image.Height || moduleSizePx < 1)
        return null;

      // Black and white levels of each finder pattern: its 3x3 centre is dark, the ring around it (one module in) light
      var topLeft = FinderLevels(image, symbolX, symbolY, moduleSizePx, 0, 0);
      var topRight = FinderLevels(image, symbolX, symbolY, moduleSizePx, moduleCount - 7, 0);
      var bottomLeft = FinderLevels(image, symbolX, symbolY, moduleSizePx, 0, moduleCount - 7);
      if (topLeft.Contrast < 16 || topRight.Contrast < 16 || bottomLeft.Contrast < 16)
        return null;

      if (!m_matrices.TryGetValue(moduleCount, out var bits))
      {
        bits = new BitMatrix(moduleCount);
        m_matrices.Add(moduleCount, bits);
      }
      bits.clear();
      double span = moduleCount - 7;
      for (int my = 0; my < moduleCount; ++my)
      {
        for (int mx = 0; mx < moduleCount; ++mx)
        {
          // A threshold plane through the three finders follows lighting gradients across the marker
          double fx = mx / span;
          double fy = my / span;
          double threshold = topLeft.Threshold + ((topRight.Threshold - topLeft.Threshold) * fx) + ((bottomLeft.Threshold - topLeft.Threshold) * fy);
          if (SampleModule(image, symbolX, symbolY, moduleSizePx, mx, my) < threshold)
            bits[mx, my] = true;
        }
      }

      try
      {
        var result = m_decoder.decode(bits, m_hints);
        return result != null ? Join(result.ByteSegments) ?? (result.Text != null ? System.Text.Encoding.Latin1.GetBytes(result.Text) : null) : null;
      }
      catch (ReaderException)
      {
        return null;
      }
    }

    /// <summary>Average of the central half of a module (a 2x2 set of bilinear samples), away from the blurred edges.</summary>
    private static double SampleModule(GrayImage image, double symbolX, double symbolY, double moduleSizePx, int mx, int my)
    {
      double cx = symbolX + ((mx + 0.5) * moduleSizePx);
      double cy = symbolY + ((my + 0.5) * moduleSizePx);
      double d = 0.2 * moduleSizePx;
      return (
          ImageWarp.SampleBilinear(image, cx - d, cy - d)
          + ImageWarp.SampleBilinear(image, cx + d, cy - d)
          + ImageWarp.SampleBilinear(image, cx - d, cy + d)
          + ImageWarp.SampleBilinear(image, cx + d, cy + d)
        ) / 4;
    }

    private static (double Threshold, double Contrast) FinderLevels(
      GrayImage image,
      double symbolX,
      double symbolY,
      double moduleSizePx,
      int left,
      int top
    )
    {
      double dark = 0;
      for (int y = 2; y <= 4; ++y)
      {
        for (int x = 2; x <= 4; ++x)
          dark += SampleModule(image, symbolX, symbolY, moduleSizePx, left + x, top + y);
      }
      dark /= 9;
      double light = 0;
      for (int i = 1; i <= 5; ++i)
      {
        light += SampleModule(image, symbolX, symbolY, moduleSizePx, left + i, top + 1);
        light += SampleModule(image, symbolX, symbolY, moduleSizePx, left + i, top + 5);
        light += SampleModule(image, symbolX, symbolY, moduleSizePx, left + 1, top + i);
        light += SampleModule(image, symbolX, symbolY, moduleSizePx, left + 5, top + i);
      }
      light /= 20;
      return ((dark + light) / 2, light - dark);
    }

    private static byte[]? Join(IList<byte[]>? segments)
    {
      if (segments == null || segments.Count == 0)
        return null;
      if (segments.Count == 1)
        return segments[0];
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
  }
}
