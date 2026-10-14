//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A frame record as it was read from a trace (sdk/doc/trace-frame-record.md): the slice an application logs for each frame, named
//* "mbfp frame <run id> <frame index>" with the ids of the frame's marker, from the marker's CPU start time for its CPU busy time, with
//* the marker's other times as annotations. It is what a trace and a capture share: a frame of the trace is known by its marker's frame
//* index, the frames are placed by it, and what the tools decode from a marker is held against what the application logged.
//*
//* The SDK's trace module defines the record and its strings (MB.FramePacing.Trace); this is the reader's side of it. The times here are
//* on the record's clock (the system's CLOCK_MONOTONIC), in whole nanoseconds: a reader takes them back from the trace's own clock with
//* the trace's snapshots.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using MB.FramePacing.Trace;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>A frame record read from a trace, with its marker's run id and frame index.</summary>
  /// <param name="StartNs">The slice's start: the marker's CPU start time, on the record's clock.</param>
  /// <param name="EndNs">Its end: the start plus the marker's CPU busy time.</param>
  public readonly record struct FrameRecordSlice(uint RunId, ulong FrameIndex, long StartNs, long EndNs)
  {
    /// <summary>The annotations a slice may have, in the order of the marker's fields: the SDK's names.</summary>
    public static IReadOnlyList<string> AnnotationNames { get; } =
      new[] { FrameRecordText.AnimationNs, FrameRecordText.MarkerPreferredNs, FrameRecordText.MarkerTargetNs, FrameRecordText.IntendedDisplayNs };

    public long? AnimationNs { get; init; }

    public long? MarkerPreferredNs { get; init; }

    public long? MarkerTargetNs { get; init; }

    public long? IntendedDisplayNs { get; init; }

    /// <summary>The process the slice's track belongs to, by the name the trace has for it; null when the track is no process's.</summary>
    public string? Process { get; init; }
  }
}
