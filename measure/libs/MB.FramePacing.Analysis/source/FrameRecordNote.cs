//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What an analysis says of a run whose start marker carries the frame record flag (MarkerFlags.FrameRecordLogged): the
//* application says it logged the run's frames to a trace of its own. A note, so that whoever reads the analysis knows a trace exists; it
//* is the application's word and nothing is judged by it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  public static class FrameRecordNote
  {
    /// <summary>The run's start marker says a trace exists, and a trace was joined with the run.</summary>
    public const string Joined = "The start marker says the application logged this run's frames to a trace (the frame record).";

    /// <summary>The run's start marker says a trace exists, and none was joined with the run.</summary>
    public const string Available =
      "The start marker says the application logged this run's frames to a trace (the frame record): a trace of the run can be merged "
      + "with this capture (measure/doc/perfetto.md).";

    /// <summary>A trace was given and not joined with the run, although its start marker says the application logged a frame record.</summary>
    public const string NotJoined = "The run's start marker says the application logged a frame record: is this the trace of this run?";

    /// <summary>
    /// The note for <paramref name="run"/>, or null when its start marker does not carry the flag. <paramref name="joined"/>: a trace
    /// was joined with the run.
    /// </summary>
    public static string? Of(RunAnalysis run, bool joined) =>
      !run.FrameRecordLogged ? null
      : joined ? Joined
      : Available;
  }
}
