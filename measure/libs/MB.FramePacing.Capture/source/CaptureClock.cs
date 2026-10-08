//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Monotonic capture clock, shared by a source and the recorder: the time since the capture started.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Diagnostics;

namespace MB.FramePacing.Capture
{
  /// <summary>Monotonic capture clock, shared by a source and the recorder: the time since the capture started.</summary>
  public sealed class CaptureClock
  {
    private readonly long m_startTimestamp = Stopwatch.GetTimestamp();

    /// <summary>The time since the capture started: the stopwatch's count since then, as the nanosecond it is in.</summary>
    public NanosecondTickCount Now => NanosecondTickCount.FromCounter(Stopwatch.GetTimestamp() - m_startTimestamp, Stopwatch.Frequency);
  }
}
