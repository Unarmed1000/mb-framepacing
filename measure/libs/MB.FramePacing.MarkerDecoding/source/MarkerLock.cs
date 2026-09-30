//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Where a frame marker was found: its bounds (including the quiet zone) and module size in image pixels. Once the analyzer knows this it
//* decodes with DecodeLocked, which samples the module grid directly instead of searching for finder patterns.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.MarkerDecoding
{
  /// <summary>
  /// Where a frame marker was found: its bounds (including the quiet zone) and module size in image pixels. Once the analyzer knows this it
  /// decodes with <see cref="MarkerDecoder.DecodeLocked"/>, which samples the module grid directly instead of searching for finder patterns.
  /// </summary>
  public readonly record struct MarkerLock(PixelRect Bounds, double ModuleSizePx)
  {
    /// <summary>Region that holds the marker drawn at the same origin, with a small margin.</summary>
    public PixelRect SearchRegion
    {
      get
      {
        int margin = (int)Math.Ceiling(2 * ModuleSizePx);
        return new PixelRect(Bounds.X - margin, Bounds.Y - margin, Bounds.Width + (2 * margin), Bounds.Height + (2 * margin));
      }
    }

    /// <summary>Modules per side of the locked symbol (41 for the main marker, 25 for the sync marker), from the bounds.</summary>
    public int ModuleCount => ModuleSizePx > 0 ? (int)Math.Round(Bounds.Width / ModuleSizePx) - (2 * MarkerRenderer.RecommendedQuietZoneModules) : 0;

    /// <summary>A lock on the marker of <paramref name="kind"/> at <paramref name="originX"/>, <paramref name="originY"/>.</summary>
    public static MarkerLock At(int originX, int originY, double moduleSizePx, MarkerKind kind = MarkerKind.Frame)
    {
      int size = (int)Math.Round(MarkerRenderer.MarkerSizePx(1, MarkerRenderer.RecommendedQuietZoneModules, kind) * moduleSizePx);
      return new MarkerLock(new PixelRect(originX, originY, size, size), moduleSizePx);
    }

    /// <summary>The frame marker with 1.5 modules of the quiet zone trimmed off, so the crop is white all around the symbol.</summary>
    internal PixelRect PureRegion
    {
      get
      {
        int inset = (int)Math.Round(1.5 * ModuleSizePx);
        return new PixelRect(Bounds.X + inset, Bounds.Y + inset, Bounds.Width - (2 * inset), Bounds.Height - (2 * inset));
      }
    }
  }
}
