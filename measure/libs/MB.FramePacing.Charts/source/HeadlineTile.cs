//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One headline number of a run: the GUI shows it as a tile on the Analysis page, the report draws it above the Timeline.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  /// <param name="Id">The tile's report item id (<see cref="ReportItem"/>), which switches it on or off in a report.</param>
  /// <param name="Caption">What the number is ("Late frames").</param>
  /// <param name="Value">The number, formatted ("11").</param>
  /// <param name="Detail">A smaller second number or word next to it ("2.6 %"), or empty.</param>
  /// <param name="Warning">The value is a problem (frames off, late frames): shown in the warning colour.</param>
  /// <param name="Explanation">What it means, for a tooltip.</param>
  /// <param name="HasValue">False when the tile has no number to show ("-": too few frames, no pacing); the report can leave it out.</param>
  public sealed record HeadlineTile(string Id, string Caption, string Value, string Detail, bool Warning, string Explanation, bool HasValue = true);
}
