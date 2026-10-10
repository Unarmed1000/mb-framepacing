//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL (measure/doc/perfetto.md). The Perfetto trace a capture is joined with, and how: the trace's file, and what of it to
//* read. A capture keeps it in capture.json ("trace"), so a later analysis joins the same trace again. MB.FramePacing.TraceJoin does
//* the join, by the frame record the application logged into the trace (sdk/doc/trace-frame-record.md).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture
{
  /// <summary>Experimental. A Perfetto trace of a capture's run, and the options of the join.</summary>
  public sealed record TraceJoinOptions
  {
    /// <summary>
    /// The trace's file: a Perfetto protobuf trace recorded on the device (a whole-file gzip is taken too). Empty: no trace is joined,
    /// and only the application's own trace (<see cref="AppTracePath"/>) is read.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// A Perfetto trace the application wrote itself, when its frame record (a slice per frame named
    /// "mbfp frame &lt;run id&gt; &lt;frame index&gt;") is not in <see cref="Path"/>'s trace: the join is placed by it, and what the
    /// tools decoded from the markers is held against it. Null: none; the record is then looked for in <see cref="Path"/>'s trace.
    /// </summary>
    public string? AppTracePath { get; init; }

    /// <summary>
    /// The application's process name in the trace (* and ? as in a file name). Null: the process the frame record's track belongs
    /// to; when that is no process's, the system's own account of the frames and the CPU's time are left out.
    /// </summary>
    public string? Process { get; init; }

    /// <summary>The application's layer in the frame timeline, when it has several. Null: every layer.</summary>
    public string? Layer { get; init; }

    /// <summary>The thread whose CPU time is counted. Null: the process's main thread.</summary>
    public string? Thread { get; init; }

    /// <summary>Which of the trace's vertical blank sources to use, or "none". Null: the kernel's vertical blank events, when it has them.</summary>
    public string? VBlanks { get; init; }

    /// <summary>The capture's clock minus the application's, when it is known: nothing is worked out from the frames then.</summary>
    public long? CaptureOffsetNs { get; init; }
  }
}
