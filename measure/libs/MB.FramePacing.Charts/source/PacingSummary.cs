//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a section's markers say about pacing, as the report's pacer band and the GUI's Pacer line show it: whether a pacer or a frame limiter
//* held the frames, as the paced flag says, a checkbox per pacing field, and what the analysis measured the frames against (RunHeadline.Pacing).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Charts
{
  /// <param name="Answer">Whether a pacer or a frame limiter held the frames, as the paced flag says.</param>
  /// <param name="Value">The answer in a few words ("yes", "in 50 % of the frames", "no").</param>
  /// <param name="Basis">Where it comes from ("the markers say so", "no frame carries the paced flag").</param>
  /// <param name="Rows">A checkbox per pacing field, in the marker's order.</param>
  /// <param name="MeasuredAgainst">What the analysis measured the frames against ("the pacer's schedule in the markers"), empty without pacing.</param>
  public sealed record PacingSummary(PacedAnswer Answer, string Value, string Basis, IReadOnlyList<PacingFieldRow> Rows, string MeasuredAgainst)
  {
    /// <summary>The answer as one line: "pacer or limiter: yes (the markers say so)".</summary>
    public string Line => $"pacer or limiter: {Value} ({Basis})";
  }
}
