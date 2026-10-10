//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A stretch of one thread's time in one scheduling state, as the trace names it ("Running", "R", "R+", "S", "D", ...). Times are whole
//* nanoseconds on the trace's clock.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. A stretch of a thread's time in one scheduling state.</summary>
  public readonly record struct ThreadState(long StartNs, long EndNs, string State);
}
