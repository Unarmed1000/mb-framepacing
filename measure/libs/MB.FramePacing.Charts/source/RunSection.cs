//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A time range of a run to draw: the range of its frames in the run's prepared data (RunChartData, found by binary search), and, only when
//* something asks for them, the headline numbers of just those frames (statistics, late frames, the worst 2 s), computed by the analysis's
//* own functions. Times are seconds since the run's first frame, as on the Timeline. Immutable.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Charts
{
  public sealed class RunSection
  {
    private readonly Lazy<ChartRun> m_section;

    private RunSection(ChartRun run, double fromSeconds, double toSeconds, int start, int end, bool wholeRun)
    {
      Run = run;
      FromSeconds = fromSeconds;
      ToSeconds = toSeconds;
      Start = start;
      End = end;
      IsWholeRun = wholeRun;
      m_section = wholeRun ? new Lazy<ChartRun>(run) : new Lazy<ChartRun>(CreateSection, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The whole run.</summary>
    public ChartRun Run { get; }

    /// <summary>Where the section starts, in seconds since the run's first frame.</summary>
    public double FromSeconds { get; }

    /// <summary>Where it ends.</summary>
    public double ToSeconds { get; }

    /// <summary>The section's first frame in the run's prepared data (<see cref="Data"/>).</summary>
    public int Start { get; }

    /// <summary>The frame after its last.</summary>
    public int End { get; }

    /// <summary>How many frames the section has.</summary>
    public int FrameCount => End - Start;

    /// <summary>The section covers the whole run.</summary>
    public bool IsWholeRun { get; }

    /// <summary>The run's prepared data.</summary>
    public RunChartData Data => RunChartData.Of(Run);

    /// <summary>The section as a run of its own: its frames and their numbers (the whole run when the section is all of it), made when first asked for.</summary>
    public ChartRun Section => m_section.Value;

    /// <summary>The time at 0 s: the run's first frame.</summary>
    public long OriginTicks => Run.Run.Frames.Count > 0 ? Run.Run.Frames[0].FirstSeenTicks : 0;

    /// <summary>The whole run: from its first frame to one capture period after its last.</summary>
    public static RunSection Whole(ChartRun run)
    {
      var frames = run.Run.Frames;
      double end = frames.Count > 0 ? Seconds(frames[^1].LastSeenTicks - frames[0].FirstSeenTicks + run.CapturePeriodTicks) : 1;
      return new RunSection(run, 0, Math.Max(end, 0.001), 0, frames.Count, wholeRun: true);
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
      var (start, end) = RunChartData.Of(run).Range(from, to);
      return new RunSection(run, from, to, start, end, wholeRun: false);
    }

    private ChartRun CreateSection()
    {
      var all = Data.Frames;
      var frames = new List<PresentedFrame>(FrameCount);
      for (int i = Start; i < End; ++i)
        frames.Add(all[i]);
      var analysis = Run.Run with
      {
        Frames = frames,
        Statistics = RunStatistics.From(frames, Run.ErrorThresholdTicks, Run.CapturePeriodTicks),
        Counts = Run.Run.Counts with { PresentedFrames = frames.Count },
        Pacing = Run.Run.Pacing is { } pacing ? SectionPacing(pacing, frames) : null,
      };
      return Run with { Run = analysis };
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

    private static double Seconds(long ticks) => ticks / (double)TimeSpan.TicksPerSecond;
  }
}
