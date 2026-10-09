//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Joins one run of a capture with what was read of a trace: places the run on the trace's clock, moves its display times onto the
//* trace's vertical blanks where it has them, and works out what the trace says of each frame. The steps of tools/trace_augment.py, in
//* its order; what does not fit is refused with what to do.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using MB.FramePacing.Capture;

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. Joins a run with a trace.</summary>
  public static class TraceJoiner
  {
    /// <summary>
    /// The join of <paramref name="run"/> with <paramref name="data"/>. A run with no times in its markers, or whose CPU start times are
    /// not inside the trace, is a <see cref="TraceException"/>.
    /// </summary>
    public static TraceRunJoin Join(TraceRun run, TraceData data, TraceJoinOptions options)
    {
      if (run.Frames.Count == 0)
        throw new TraceException("The run has no frame to place.");
      var alignment = TraceAlignmentUtil.Align(run.Frames, options.AppClockOffsetNs ?? 0, options.CaptureOffsetNs);
      if (alignment.Anchor == TraceAnchor.Nothing)
        throw new TraceException(
          "The markers of this run carry no intended display time and no CPU start time, so nothing ties the capture's clock to the trace's: "
            + "give the capture offset (the capture's clock minus the application's)."
        );
      TraceAlignmentUtil.CheckInsideTrace(run.Frames, alignment, data.Snapshots, data.FirstNs, data.LastNs);

      var notes = new List<string>(data.Notes)
      {
        Invariant($"{run.Frames.Count} frames, placed by {TraceAnchorText.Of(alignment.Anchor)}"),
        Invariant($"capture clock minus application clock: {alignment.CaptureMinusAppNs} ns"),
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
        LineUp = TraceLineUpCheck.Check(run.RunId, run.Frames, rows, data, run.PeriodNs),
      };
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
  }
}
