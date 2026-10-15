//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How a frame timeline card (FrameTimelineCard) is laid out for a document, as mb-framepacing-explained's timing diagrams are: its own title
//* (the section's frames and seconds are added, as on the report card, or left out), its own description lines in place of the card's own, a
//* label per frame of the section in place of its frame index, its own words for the CPU row, and a refresh as wide as the diagrams draw it.
//* The defaults draw the standard card.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Charts
{
  public sealed record FrameTimelineOptions
  {
    public static readonly FrameTimelineOptions Default = new FrameTimelineOptions();

    /// <summary>The card's title instead of 'Run N  name'; the section's frames and seconds are added to it unless <see cref="TitleRange"/> is false.</summary>
    public string? Title { get; init; }

    /// <summary>false: the title without the section's frames and seconds (where in a recording a diagram is says nothing to its reader).</summary>
    public bool TitleRange { get; init; } = true;

    /// <summary>The CPU row's label and the line under it instead of the card's own ("CPU", "start + CPU busy"); null: those.</summary>
    public string? CpuRowLabel { get; init; }

    /// <inheritdoc cref="CpuRowLabel"/>
    public string? CpuRowSubLabel { get; init; }

    /// <summary>false: the key's last line without the error threshold (the diagrams' line, for a card whose threshold only keeps the
    /// rounding of the times out).</summary>
    public bool ThresholdInKey { get; init; } = true;

    /// <summary>The lines under the title instead of the card's own (the display's refresh, how the CPU times are placed); null: those.</summary>
    public IReadOnlyList<string>? Description { get; init; }

    /// <summary>A label per presented frame of the section, in order, instead of '#' and the last digits of its index; null: those.</summary>
    public IReadOnlyList<string>? FrameLabels { get; init; }

    /// <summary>
    /// How wide a refresh is drawn, in pixels (<see cref="MinRefreshPixels"/> to <see cref="MaxRefreshPixels"/>; the diagrams draw 120); null:
    /// the standard card's, 64 or more.
    /// </summary>
    public double? RefreshPixels { get; init; }

    public const double MinRefreshPixels = 16;
    public const double MaxRefreshPixels = 480;
  }
}
