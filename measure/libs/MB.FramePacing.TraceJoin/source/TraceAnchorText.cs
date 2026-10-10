//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The words for what placed a capture's clock, as they are written into a trace (the same as tools/trace_alignment.py's).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. The words for a <see cref="TraceAnchor"/>.</summary>
  public static class TraceAnchorText
  {
    public static string Of(TraceAnchor anchor) =>
      anchor switch
      {
        TraceAnchor.Schedule => "the pacer's intended display times (the analysis's alignment)",
        TraceAnchor.CpuWork => "the CPU start and CPU busy times (no frame is on screen before its CPU work ended)",
        TraceAnchor.Given => "the offset that was given",
        TraceAnchor.Nothing => "nothing: the markers carry no times, so the capture's own clock is used as it is",
        _ => throw new ArgumentOutOfRangeException(nameof(anchor)),
      };
  }
}
