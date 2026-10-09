//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a trace has to go by, for picking the options a join takes: its clocks, its vertical blank sources, its processes, their layers in
//* the frame timeline and their threads.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. What a trace has to go by.</summary>
  /// <param name="FirstNs">The first time of the trace, on its own clock.</param>
  /// <param name="Errors">What the trace processor counted as errors while it read the trace, by name.</param>
  public sealed record TraceInfo(
    long FirstNs,
    long LastNs,
    IReadOnlyList<TraceClockInfo> Clocks,
    IReadOnlyList<VBlankSource> VBlankSources,
    IReadOnlyList<TraceProcessInfo> Processes,
    IReadOnlyList<TraceLayerInfo> Layers,
    IReadOnlyList<TraceThreadInfo> Threads,
    IReadOnlyDictionary<string, long> Errors
  );
}
