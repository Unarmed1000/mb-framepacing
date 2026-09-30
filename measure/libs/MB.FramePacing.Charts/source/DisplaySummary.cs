//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The display a run was shown on, as the report's display box and the GUI's Display card show it: its refresh rate and where it comes
//* from, the time per refresh and what the frames targeted, and what the application wants when its markers say so.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  /// <param name="Rate">The refresh rate ("60 Hz"), "?? Hz" without pacing.</param>
  /// <param name="Kind">Where the rate comes from ("fixed refresh (vsync)", "calculated from the camera"), with the expected rate when it differs.</param>
  /// <param name="Mismatch">The rate differs from the display rate the user expected: shown in the warning colour.</param>
  /// <param name="Refresh">The time per refresh and what the frames targeted in whole refreshes ("16.7 ms per refresh, target 1 refresh (60 fps)").</param>
  /// <param name="Wants">What the application wants, when its markers say so ("preferred 60 fps", "on demand"), else empty.</param>
  public sealed record DisplaySummary(string Rate, string Kind, bool Mismatch, string Refresh, string Wants);
}
