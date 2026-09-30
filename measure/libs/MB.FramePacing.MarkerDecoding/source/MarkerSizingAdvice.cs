//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How an application should draw the marker for one capture setup (see MarkerSizing.Advise and sdk/doc/marker-format.md "Sizing").
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.MarkerDecoding
{
  public sealed record MarkerSizingAdvice
  {
    /// <summary>The application's output resolution (source pixels).</summary>
    public required int SourceWidth { get; init; }

    public required int SourceHeight { get; init; }

    /// <summary>Height of the frames the capture tool stores (the capture mode, or --scale).</summary>
    public required int StoredHeight { get; init; }

    /// <summary>The capture card delivers MJPEG (4 instead of 3 stored pixels per module).</summary>
    public required bool Mjpeg { get; init; }

    /// <summary>The module size to draw with, in source pixels.</summary>
    public required int RecommendedModulePx { get; init; }

    /// <summary>The smallest module size that still decodes (2 stored pixels per module).</summary>
    public required int MinimumModulePx { get; init; }

    /// <summary>Stored pixels per module at the recommended size.</summary>
    public required double StoredPxPerModule { get; init; }

    /// <summary>Main marker size (frame, start and end; square, source pixels, including the quiet zone).</summary>
    public required int MarkerPx { get; init; }

    /// <summary>Sync marker size (tearing check, camera capture).</summary>
    public required int SyncMarkerPx { get; init; }

    /// <summary>Recommended top-left origin of the sync marker (bottom-left of the frame).</summary>
    public required int SyncOriginX { get; init; }

    public required int SyncOriginY { get; init; }

    /// <summary>The integer downscale ratio the origin is aligned to (1 when the ratio is not an integer).</summary>
    public required int AlignPx { get; init; }

    /// <summary>Recommended top-left origin of the primary marker.</summary>
    public required int OriginX { get; init; }

    public required int OriginY { get; init; }

    /// <summary>The source height is not an integer multiple of the stored height (works, but module edges blur).</summary>
    public bool NonIntegerRatio => AlignPx == 1 && SourceHeight != StoredHeight;
  }
}
