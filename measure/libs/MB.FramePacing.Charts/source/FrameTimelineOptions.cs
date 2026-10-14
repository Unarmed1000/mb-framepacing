//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How a frame timeline card (FrameTimelineCard) is laid out for a document, as mb-framepacing-explained's timing diagrams are: its own title
//* (the section's frames and seconds are still added, as on the report card), its own description lines in place of the card's own, and a
//* label per frame of the section in place of its frame index. The defaults draw the standard card.
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

    /// <summary>The card's title instead of 'Run N  name'; the section's frames and seconds are added to it.</summary>
    public string? Title { get; init; }

    /// <summary>The lines under the title instead of the card's own (the display's refresh, how the CPU times are placed); null: those.</summary>
    public IReadOnlyList<string>? Description { get; init; }

    /// <summary>A label per presented frame of the section, in order, instead of '#' and the last digits of its index; null: those.</summary>
    public IReadOnlyList<string>? FrameLabels { get; init; }
  }
}
