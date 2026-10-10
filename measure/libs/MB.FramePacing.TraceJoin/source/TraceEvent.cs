//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One event of a track: a slice (from StartNs to EndNs), a moment (at StartNs) or a counter's value (at StartNs). Every time is on the
//* system clock the application's clock is (TraceAlignment), in whole nanoseconds.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. One event of a track of a capture's measurements.</summary>
  /// <param name="EndNs">A slice's end; a moment's and a value's own time.</param>
  /// <param name="Flow">Joins a slice to the slice of another track that has the same number (0: none).</param>
  /// <param name="Value">A counter's value.</param>
  public sealed record TraceEvent(
    TraceTrack Track,
    TraceEventKind Kind,
    long StartNs,
    long EndNs,
    string Name,
    IReadOnlyList<TraceArgument> Arguments,
    ulong Flow = 0,
    double Value = 0
  )
  {
    public static TraceEvent Slice(TraceTrack track, long startNs, long endNs, string name, IReadOnlyList<TraceArgument> arguments, ulong flow = 0) =>
      new TraceEvent(track, TraceEventKind.Slice, startNs, endNs, name, arguments, flow);

    public static TraceEvent Instant(TraceTrack track, long timeNs, string name, IReadOnlyList<TraceArgument> arguments) =>
      new TraceEvent(track, TraceEventKind.Instant, timeNs, timeNs, name, arguments);

    public static TraceEvent Counter(TraceTrack track, long timeNs, double value) =>
      new TraceEvent(track, TraceEventKind.Counter, timeNs, timeNs, string.Empty, Array.Empty<TraceArgument>(), 0, value);
  }
}
