//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A time range of a run to draw: its frames, and the headline numbers of just those frames (statistics, late frames, the worst 2 s),
//* computed by the analysis's own functions. Times are seconds since the run's first frame, as on the Timeline.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Charts
{
  /// <param name="Run">The whole run.</param>
  /// <param name="FromSeconds">Where the section starts, in seconds since the run's first frame.</param>
  /// <param name="ToSeconds">Where it ends.</param>
  /// <param name="Section">The section as a run of its own: its frames and their numbers (the whole run when the section is all of it).</param>
  public sealed record RunSection(ChartRun Run, double FromSeconds, double ToSeconds, ChartRun Section)
  {
    /// <summary>The time at 0 s: the run's first frame.</summary>
    public long OriginTicks => Run.Run.Frames.Count > 0 ? Run.Run.Frames[0].FirstSeenTicks : 0;

    /// <summary>The section covers the whole run.</summary>
    public bool IsWholeRun => ReferenceEquals(Run, Section);

    /// <summary>The whole run: from its first frame to one capture period after its last.</summary>
    public static RunSection Whole(ChartRun run)
    {
      var frames = run.Run.Frames;
      double end = frames.Count > 0 ? Seconds(frames[^1].LastSeenTicks - frames[0].FirstSeenTicks + run.CapturePeriodTicks) : 1;
      return new RunSection(run, 0, Math.Max(end, 0.001), run);
    }

    /// <summary>The frames first seen from <paramref name="fromSeconds"/> to <paramref name="toSeconds"/>, clamped to the run.</summary>
    public static RunSection Create(ChartRun run, double fromSeconds, double toSeconds)
    {
      var whole = Whole(run);
      double from = Math.Clamp(Math.Min(fromSeconds, toSeconds), 0, whole.ToSeconds);
      double to = Math.Clamp(Math.Max(fromSeconds, toSeconds), 0, whole.ToSeconds);
      if (to - from < 0.001)
        to = Math.Min(whole.ToSeconds, from + 0.001);
      if (from <= 0 && to >= whole.ToSeconds)
        return whole;

      long origin = whole.OriginTicks;
      var frames = FramesBetween(run.Run.Frames, origin, from, to);
      var analysis = run.Run with
      {
        Frames = frames,
        Statistics = RunStatistics.From(frames, run.ErrorThresholdTicks, run.CapturePeriodTicks),
        Counts = run.Run.Counts with { PresentedFrames = frames.Count },
        Pacing = run.Run.Pacing is { } pacing ? SectionPacing(pacing, frames) : null,
      };
      return new RunSection(run, from, to, run with { Run = analysis });
    }

    /// <summary>The run's pacing with the late frames, their share and the worst 2 s of the section's frames.</summary>
    private static RunPacing SectionPacing(RunPacing pacing, IReadOnlyList<PresentedFrame> frames)
    {
      int measured = frames.Count(f => f.DisplayDeltaTicks.HasValue);
      long late = frames.LongCount(f => f.DisplayDeltaTicks.HasValue && (f.Flags & PresentedFrameFlags.Late) != 0);
      return pacing with
      {
        LateFrames = frames.LongCount(f => (f.Flags & PresentedFrameFlags.Late) != 0),
        LateShare = measured > 0 ? late / (double)measured : 0,
        WorstLateShare = LateShare.Worst(frames, LateShare.WindowTicks),
      };
    }

    /// <summary>
    /// The frames first seen from <paramref name="from"/> to <paramref name="to"/> seconds after <paramref name="origin"/>: found by binary
    /// search in display order (a run's frames are), else by looking at every frame.
    /// </summary>
    private static List<PresentedFrame> FramesBetween(IReadOnlyList<PresentedFrame> all, long origin, double from, double to)
    {
      bool Before(PresentedFrame f) => Seconds(f.FirstSeenTicks - origin) < from;
      bool After(PresentedFrame f) => Seconds(f.FirstSeenTicks - origin) > to;
      for (int i = 1; i < all.Count; ++i)
      {
        if (all[i].FirstSeenTicks < all[i - 1].FirstSeenTicks)
          return all.Where(f => !Before(f) && !After(f)).ToList();
      }
      int first = LowerBound(all, f => !Before(f));
      int end = LowerBound(all, After);
      var frames = new List<PresentedFrame>(Math.Max(0, end - first));
      for (int i = first; i < end; ++i)
        frames.Add(all[i]);
      return frames;
    }

    /// <summary>The first index where <paramref name="reached"/> holds (it holds from some index on), or the count.</summary>
    private static int LowerBound(IReadOnlyList<PresentedFrame> frames, Func<PresentedFrame, bool> reached)
    {
      int low = 0;
      int high = frames.Count;
      while (low < high)
      {
        int middle = low + ((high - low) / 2);
        if (reached(frames[middle]))
          high = middle;
        else
          low = middle + 1;
      }
      return low;
    }

    private static double Seconds(long ticks) => ticks / (double)TimeSpan.TicksPerSecond;
  }
}
