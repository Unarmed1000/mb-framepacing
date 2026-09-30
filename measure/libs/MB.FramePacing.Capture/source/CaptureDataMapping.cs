//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Between the capture data library's header (MB.FramePacing.Data, the file format) and the tools' types: the captured frames' header and
//* the marker locks.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Data;
using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Capture
{
  public static class CaptureDataMapping
  {
    /// <summary>The captures.mbcd header of frames with <paramref name="frames"/>'s header and markers at <paramref name="locks"/>.</summary>
    public static CaptureDataHeader ToDataHeader(this CaptureFileHeader frames, IReadOnlyList<MarkerLock> locks, bool framesStored, bool camera) =>
      new CaptureDataHeader(
        frames.Width,
        frames.Height,
        frames.NominalFrameRate.Numerator,
        frames.NominalFrameRate.Denominator,
        frames.SourceWidth,
        frames.SourceHeight,
        new Rectangle(frames.Roi.X, frames.Roi.Y, frames.Roi.Width, frames.Roi.Height),
        locks.ToLocations(),
        framesStored,
        camera
      );

    /// <summary>The header of the frames the capture data was read from.</summary>
    public static CaptureFileHeader ToFileHeader(this CaptureDataHeader header) =>
      new CaptureFileHeader(
        header.Width,
        header.Height,
        new FrameRate(header.FrameRateNumerator, header.FrameRateDenominator),
        header.SourceWidth,
        header.SourceHeight,
        new PixelRect(header.Region.X, header.Region.Y, header.Region.Width, header.Region.Height)
      );

    /// <summary>Where the markers were, as the decoder's locks.</summary>
    public static IReadOnlyList<MarkerLock> ToLocks(this CaptureDataHeader header) =>
      header.Markers.Select(m => new MarkerLock(new PixelRect(m.Bounds.X, m.Bounds.Y, m.Bounds.Width, m.Bounds.Height), m.ModuleSizePx)).ToList();

    public static IReadOnlyList<MarkerLocation> ToLocations(this IReadOnlyList<MarkerLock> locks) =>
      locks.Select(l => new MarkerLocation(new Rectangle(l.Bounds.X, l.Bounds.Y, l.Bounds.Width, l.Bounds.Height), l.ModuleSizePx)).ToList();
  }
}
