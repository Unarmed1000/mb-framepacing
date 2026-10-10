//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A process of a trace: what an option's pattern picks the application by.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. A process of a trace.</summary>
  /// <param name="Upid">The trace processor's own id of the process.</param>
  /// <param name="TimelineFrames">How many frames of it the system's frame timeline has.</param>
  public sealed record TraceProcessInfo(long Upid, long? Pid, string Name, long TimelineFrames = 0);
}
