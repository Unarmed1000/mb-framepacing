//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One run of a capture joined with a trace: where its frames are on the trace's clock, and what the trace says of each of them.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using MB.FramePacing.Capture;

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. A run joined with a trace.</summary>
  /// <param name="Alignment">Where the run's times are on the application's system clock, the vertical blanks' shift included.</param>
  /// <param name="Fit">How the display times sit on the trace's vertical blanks; null when none were used.</param>
  /// <param name="VBlankNote">What was done with vertical blanks, in words: for the written trace's description.</param>
  /// <param name="Rows">A row per frame of the run, in display order.</param>
  /// <param name="Notes">What was found on the way, a line each.</param>
  public sealed record TraceRunJoin(
    TraceRun Run,
    TraceJoinOptions Options,
    TraceData Data,
    TraceAlignment Alignment,
    VBlankFit? Fit,
    string VBlankNote,
    IReadOnlyList<TraceFrame> Rows,
    IReadOnlyList<string> Notes
  );
}
