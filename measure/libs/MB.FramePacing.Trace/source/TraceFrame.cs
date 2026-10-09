//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A presented frame with what a Perfetto trace says of it: one line of run-<id>-trace.csv (measure/doc/perfetto.md "The frames CSV"). The
//* members are in the CSV's column order, and every time is whole nanoseconds on the trace's clock; null is an empty cell, where the trace
//* does not say.
//*
//* What is measured and what is not: a frame's display time is the capture's, placed on the trace's clock by the frames (TraceAlignment).
//* The differences between frames are exact; a delay the whole run shares is not in them.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. A presented frame with what a trace of the same run says of it.</summary>
  /// <param name="DisplayTraceNs">The frame's display time as measured.</param>
  /// <param name="CpuStartTraceNs">Its marker's CPU start time.</param>
  /// <param name="CpuEndTraceNs">The end of its marker's CPU busy time.</param>
  /// <param name="VBlankTraceNs">The trace's vertical blank nearest the display time, within half a refresh.</param>
  /// <param name="DisplayMinusVBlankNs">How far the display time is from that vertical blank.</param>
  /// <param name="SurfaceFrameToken">The system's frame timeline: the application's frame the end of the CPU work lies in.</param>
  /// <param name="SystemPresentTraceNs">When the system says the frame was presented.</param>
  /// <param name="DisplayMinusSystemPresentNs">The measured display time minus the system's present time.</param>
  /// <param name="CpuRunningNs">Of the CPU busy time, on one thread: running.</param>
  /// <param name="CpuRunnableNs">Of the CPU busy time: waiting for a CPU.</param>
  /// <param name="CpuSleepingNs">Of the CPU busy time: neither.</param>
  public sealed record TraceFrame(
    ulong FrameIndex,
    long DisplayTraceNs,
    long? CpuStartTraceNs = null,
    long? CpuEndTraceNs = null,
    long? VBlankTraceNs = null,
    long? DisplayMinusVBlankNs = null,
    long? SurfaceFrameToken = null,
    long? DisplayFrameToken = null,
    string? LayerName = null,
    string? PresentType = null,
    string? JankType = null,
    string? JankTag = null,
    bool? OnTimeFinish = null,
    bool? GpuComposition = null,
    long? SystemPresentTraceNs = null,
    long? DisplayMinusSystemPresentNs = null,
    long? CpuRunningNs = null,
    long? CpuRunnableNs = null,
    long? CpuSleepingNs = null
  );
}
