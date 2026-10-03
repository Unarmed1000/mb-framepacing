//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a capture stores of its source's frames (RegionRule decides which).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture.Ffmpeg
{
  public enum RegionChoice
  {
    /// <summary>The whole frame (scaled to the stored size, when one is given).</summary>
    WholeFrame,

    /// <summary>The rectangle that was given.</summary>
    Rectangle,

    /// <summary>The markers' regions, located first: asked for ('auto'), so a marker that cannot be cropped to is an error.</summary>
    Markers,

    /// <summary>The markers' regions, located first, as the default for a recording: the whole frame when they cannot be cropped to.</summary>
    MarkersIfTheyFit,
  }
}
