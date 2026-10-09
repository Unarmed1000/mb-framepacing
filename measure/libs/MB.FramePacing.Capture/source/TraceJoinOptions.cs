//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL (measure/doc/perfetto.md). The Perfetto trace a capture is joined with, and how: the trace's file, which of its processes
//* is the application, and how the application's clock sits on the system's. A capture keeps it in capture.json ("trace"), so a later
//* analysis joins the same trace again. MB.FramePacing.Trace does the join.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture
{
  /// <summary>Experimental. A Perfetto trace of a capture's run, and the options of the join.</summary>
  public sealed record TraceJoinOptions
  {
    /// <summary>The trace's file: a Perfetto protobuf trace (a whole-file gzip is taken too).</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// The application's process name in the trace (* and ? as in a file name). Null: the system's own account of the frames and the CPU's
    /// time are left out.
    /// </summary>
    public string? Process { get; init; }

    /// <summary>The application's layer in the frame timeline, when it has several. Null: every layer.</summary>
    public string? Layer { get; init; }

    /// <summary>The thread whose CPU time is counted. Null: the process's main thread.</summary>
    public string? Thread { get; init; }

    /// <summary>Which of the trace's vertical blank sources to use, or "none". Null: the kernel's vertical blank events, when it has them.</summary>
    public string? VBlanks { get; init; }

    /// <summary>
    /// The system clock the application's steady clock is: "monotonic", "boottime", "monotonic-raw" or "realtime". Null: monotonic.
    /// </summary>
    public string? AppClock { get; init; }

    /// <summary>What to add to a marker's time to get that clock's, for an application whose clock counts from an epoch of its own.</summary>
    public long? AppClockOffsetNs { get; init; }

    /// <summary>The capture's clock minus the application's, when it is known: nothing is worked out from the frames then.</summary>
    public long? CaptureOffsetNs { get; init; }
  }
}
