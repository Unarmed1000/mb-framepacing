//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The run of a capture that is joined with a trace: its frames as the analysis output has them, the capture's rows and its refresh
//* period. The name is what the run's tracks are under in a trace, the key what their ids are made from: the same as
//* tools/trace_augment.py's, so the two write the same bytes.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Globalization;
using MB.FramePacing.Data;

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. A run of a capture, as a join reads it.</summary>
  /// <param name="PeriodNs">The display's refresh period, in whole nanoseconds.</param>
  public sealed record TraceRun(string Name, string Key, IReadOnlyList<FrameRow> Frames, IReadOnlyList<CaptureCsvRow> Captures, long PeriodNs)
  {
    /// <summary>The run's id, as its markers carry it: what the application's own frame sections in a trace are known by.</summary>
    public uint RunId { get; init; }

    /// <summary>The run by its id, with the name it was given or else its sequence id as the summary writes it.</summary>
    public static TraceRun Create(
      uint runId,
      string? sequenceId,
      string? name,
      IReadOnlyList<FrameRow> frames,
      IReadOnlyList<CaptureCsvRow> captures,
      long periodNs
    )
    {
      string id = runId.ToString(CultureInfo.InvariantCulture);
      string shown =
        !string.IsNullOrEmpty(name) ? name
        : !string.IsNullOrEmpty(sequenceId) ? sequenceId
        : $"run {id}";
      return new TraceRun(shown, $"{id}/{sequenceId ?? string.Empty}", frames, captures, periodNs) { RunId = runId };
    }

    /// <summary>
    /// A run of an analysis output: its refresh period is the run's own where the analysis measured one, else the capture period.
    /// </summary>
    public static TraceRun Of(AnalysisSummary summary, SummaryRun run, IReadOnlyList<FrameRow> frames, IReadOnlyList<CaptureCsvRow> captures) =>
      Create(run.RunId, run.SequenceId, run.Name, frames, captures, (run.Pacing?.RefreshPeriod ?? summary.CapturePeriod).Nanoseconds);
  }
}
