//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The system clock an application's steady clock is: the markers carry the CPU start time and the intended display time on it, and a trace
//* ties it to its own clock with its clock snapshots.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. The system clock the application's steady clock is.</summary>
  public enum TraceAppClock
  {
    /// <summary>CLOCK_MONOTONIC: what std::chrono::steady_clock is on Linux and Android.</summary>
    Monotonic,
    Boottime,
    MonotonicRaw,
    Realtime,
  }
}
