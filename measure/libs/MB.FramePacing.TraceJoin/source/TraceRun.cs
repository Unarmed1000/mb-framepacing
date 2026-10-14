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

namespace MB.FramePacing.TraceJoin
{
  /// <summary>A run of a capture, as a join reads it.</summary>
  /// <param name="PeriodNs">The display's refresh period, in whole nanoseconds.</param>
  public sealed record TraceRun(string Name, string Key, IReadOnlyList<FrameRow> Frames, IReadOnlyList<CaptureCsvRow> Captures, long PeriodNs)
  {
    /// <summary>The run's id, as its markers carry it: what the frame record's slices in a trace are known by.</summary>
    public uint RunId { get; init; }

    /// <summary>
    /// The capture's period, in whole nanoseconds: the refresh period for a capture card, shorter for a camera (0: not known). A capture is
    /// a refresh only where the two are the same.
    /// </summary>
    public long CapturePeriodNs { get; init; }

    /// <summary>The |animation error| above which a frame counts as off, in whole nanoseconds, as the analysis had it.</summary>
    public long ErrorThresholdNs { get; init; }

    /// <summary>
    /// The run by its id, with the name it was given or else its sequence id as the summary writes it. <paramref name="periodNs"/> is the
    /// display's refresh period, <paramref name="capturePeriodNs"/> the capture's, and <paramref name="errorThresholdNs"/> the
    /// analysis's error threshold.
    /// </summary>
    public static TraceRun Create(
      uint runId,
      string? sequenceId,
      string? name,
      IReadOnlyList<FrameRow> frames,
      IReadOnlyList<CaptureCsvRow> captures,
      long periodNs,
      long capturePeriodNs,
      long errorThresholdNs
    )
    {
      string id = runId.ToString(CultureInfo.InvariantCulture);
      string shown =
        !string.IsNullOrEmpty(name) ? name
        : !string.IsNullOrEmpty(sequenceId) ? sequenceId
        : $"run {id}";
      return new TraceRun(shown, $"{id}/{sequenceId ?? string.Empty}", frames, captures, periodNs)
      {
        RunId = runId,
        CapturePeriodNs = capturePeriodNs,
        ErrorThresholdNs = errorThresholdNs,
      };
    }

    /// <summary>
    /// A run of an analysis output: its refresh period is the run's own where the analysis measured one, else the capture period.
    /// </summary>
    public static TraceRun Of(AnalysisSummary summary, SummaryRun run, IReadOnlyList<FrameRow> frames, IReadOnlyList<CaptureCsvRow> captures) =>
      Create(
        run.RunId,
        run.SequenceId,
        run.Name,
        frames,
        captures,
        (run.Pacing?.RefreshPeriod ?? summary.CapturePeriod).Nanoseconds,
        summary.CapturePeriod.Nanoseconds,
        summary.ErrorThreshold.Nanoseconds
      );
  }
}
