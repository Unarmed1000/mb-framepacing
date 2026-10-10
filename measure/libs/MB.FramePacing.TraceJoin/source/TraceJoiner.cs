//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Joins one run of a capture with what was read of a trace: places the run on the trace's clock by the frame record the application
//* logged (sdk/doc/trace-frame-record.md), moves its display times onto the trace's vertical blanks where it has them, and works out
//* what the trace says of each frame. The steps of tools/trace_augment.py, in its order; what does not fit is refused with what to
//* do, and a trace without the run's frame record is not joined.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using MB.FramePacing.Capture;
using MB.FramePacing.Trace;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. Joins a run with a trace.</summary>
  public static class TraceJoiner
  {
    /// <summary>
    /// The join of <paramref name="run"/> with <paramref name="data"/>. A run the trace has no frame record of, one with no times in
    /// its markers, or one whose CPU start times are not inside the trace, is a <see cref="TraceException"/>.
    /// </summary>
    public static TraceRunJoin Join(TraceRun run, TraceData data, TraceJoinOptions options)
    {
      if (run.Frames.Count == 0)
        throw new TraceException("The run has no frame to place.");
      // The frame record says where the run's frames are on the trace's clock: without it nothing is joined
      var record = FrameRecordCheck.Of(run.RunId, run.Frames, data.FrameRecords);
      if (record.NamedFrames == 0)
        throw new TraceException(
          record.Slices == 0
            ? Invariant(
              $"The trace has no frame record of this run (no slice named '{FrameRecordText.NamePrefix}{run.RunId} <frame index>'): a trace is "
            ) + "merged by the slice the application logs for each frame (sdk/doc/trace-frame-record.md)."
            : "The trace's frame record of this run names none of the capture's frames that carry a CPU start time, so nothing places "
              + "the run in the trace."
        );
      var alignment = TraceAlignmentUtil.Align(run.Frames, record.ClockOffsetNs, options.CaptureOffsetNs);
      if (alignment.Anchor == TraceAnchor.Nothing)
        throw new TraceException(
          "The markers of this run carry no intended display time and no CPU busy time, so nothing ties the capture's clock to the "
            + "application's: "
            + "give the capture offset (the capture's clock minus the application's)."
        );
      TraceAlignmentUtil.CheckInsideTrace(run.Frames, alignment, data.Snapshots, data.FirstNs, data.LastNs);

      var notes = new List<string>(data.Notes)
      {
        Invariant($"{run.Frames.Count} frames, placed by {TraceAnchorText.Of(alignment.Anchor)}"),
        Invariant($"capture clock minus application clock: {alignment.CaptureMinusAppNs} ns"),
        Invariant($"frame record minus the markers' CPU start times: {record.ClockOffsetNs} ns, by {record.NamedFrames} frames"),
      };

      IReadOnlyList<long> vblanks = Array.Empty<long>();
      string vblankNote = "none used";
      VBlankFit? fit = null;
      if (data.VBlanksFrom is { } picked)
      {
        var displays = new long[run.Frames.Count];
        for (int i = 0; i < displays.Length; ++i)
          displays[i] = TraceAlignmentUtil.ToTraceNs(alignment.DisplayTime(run.Frames[i].FirstSeenNs()), data.Snapshots);
        fit = TraceAlignmentUtil.FitVBlanks(displays, data.VBlanks, run.PeriodNs);
        if (fit is { } found)
        {
          vblanks = data.VBlanks;
          alignment = alignment with { VBlankShiftNs = found.ShiftNs };
          vblankNote = Invariant($"{picked.Name}, display times moved by {-found.ShiftNs} ns");
          notes.Add(Invariant($"vertical blanks of {picked.Name}: display times moved by {-found.ShiftNs} ns onto them"));
          notes.Add(
            Invariant($"{found.UsedFrames} of {displays.Length} frames then have one within half a refresh, the furthest {found.SpreadNs} ns from it")
          );
        }
        else
        {
          notes.Add($"vertical blanks of {picked.Name}: too few of them lie in the run, none used");
        }
      }
      else
      {
        notes.Add("vertical blanks: none used");
      }

      var rows = TraceJoin.Join(
        run.Frames,
        alignment,
        data.Snapshots,
        run.PeriodNs,
        vblanks,
        data.SurfaceFrames,
        data.DisplayPresents,
        data.ThreadStates
      );
      return new TraceRunJoin(run, options, data, alignment, fit, vblankNote, rows, notes)
      {
        LineUp = TraceLineUpCheck.Check(rows, data, run.PeriodNs, record),
      };
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
  }
}
