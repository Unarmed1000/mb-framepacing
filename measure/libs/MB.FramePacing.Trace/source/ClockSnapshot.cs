//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One moment on two clocks: the trace's own time, and what a system clock read then. Whole nanoseconds, as the trace has them.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. One moment on the trace's clock and on a system clock, in whole nanoseconds.</summary>
  public readonly record struct ClockSnapshot(long TraceNs, long ClockNs);
}
