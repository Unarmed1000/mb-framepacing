//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Where a frame marker was found: its bounds (including the quiet zone) and module size in image pixels. Once the analyzer knows this it
//* decodes with DecodeLocked, which samples the module grid directly instead of searching for finder patterns.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
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
}
