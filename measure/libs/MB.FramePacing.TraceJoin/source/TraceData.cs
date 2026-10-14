//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Everything a join reads of one trace with one set of options: read once, then joined with each run of the capture (and kept for the
//* next analysis of the same file). Every time is whole nanoseconds on the trace's clock.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>What was read of one trace for a join.</summary>
  /// <param name="FirstNs">The first time of the trace.</param>
  /// <param name="Snapshots">The trace's snapshots of the frame record's clock (the monotonic clock), ascending by that clock.</param>
  /// <param name="Errors">What the trace processor counted as errors while it read the trace, by name.</param>
  /// <param name="VBlanksFrom">The source of vertical blanks that was picked; null when none is used.</param>
  /// <param name="VBlanks">Its vertical blanks, ascending.</param>
  /// <param name="SurfaceFrames">The application's frames in the system's frame timeline, ascending by their start.</param>
  /// <param name="DisplayPresents">For a display frame token, when the system says that frame was presented.</param>
  /// <param name="ThreadStates">The scheduling states of the application's thread, ascending.</param>
  /// <param name="Notes">What a reader of the result has to know about how the trace was read.</param>
  public sealed record TraceData(
    long FirstNs,
    long LastNs,
    IReadOnlyList<ClockSnapshot> Snapshots,
    IReadOnlyDictionary<string, long> Errors,
    VBlankSource? VBlanksFrom,
    IReadOnlyList<long> VBlanks,
    IReadOnlyList<SurfaceFrame> SurfaceFrames,
    IReadOnlyDictionary<long, long> DisplayPresents,
    IReadOnlyList<ThreadState> ThreadStates,
    IReadOnlyList<string> Notes
  )
  {
    /// <summary>
    /// The frame record (sdk/doc/trace-frame-record.md), with its times on the record's clock: the slices of the application's own
    /// trace first, where one was read, then the trace's own, each ascending by their start. Empty when there is none.
    /// </summary>
    public IReadOnlyList<FrameRecordSlice> FrameRecords { get; init; } = System.Array.Empty<FrameRecordSlice>();
  }
}
