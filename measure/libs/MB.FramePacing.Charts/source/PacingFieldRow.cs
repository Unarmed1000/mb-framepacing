//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One checkbox of the report's pacer band and the GUI's Pacer line: a pacing field of the markers, whether the section's frames carry it
//* (every frame, the share that does, or none) and what it says.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  /// <param name="Mark">"✓" when every frame carries the field, the share that does when some do ("99 %"), "–" when none does.</param>
  /// <param name="Name">The field ("schedule (intended display times)").</param>
  /// <param name="Detail">What it says ("30–60 fps", "target slower in 12 %"), else empty.</param>
  public sealed record PacingFieldRow(string Mark, string Name, string Detail)
  {
    /// <summary>The row as one line: "99 % target frame time: 30–60 fps".</summary>
    public string Text => $"{Mark} {Name}" + (Detail.Length > 0 ? $": {Detail}" : string.Empty);
  }
}
