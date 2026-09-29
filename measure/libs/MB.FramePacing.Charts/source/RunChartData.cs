//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the cards draw of a run, prepared once and kept with the run (immutable, cached per ChartRun): each kind of value as a frame sequence
//* with its wavelet matrix, the flags as rank bits, and the late share's rolling window over the whole run. A section is a range of frames
//* (found by binary search on the display times), and every pixel column's numbers come from queries on these, so a card costs per column,
//* not per frame, whatever the run's length. Everything is built on first use, once, and is safe to read from any thread.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Charts
{
  public sealed class RunChartData
  {
    private static readonly ConditionalWeakTable<ChartRun, RunChartData> g_cache = new ConditionalWeakTable<ChartRun, RunChartData>();

    private readonly Lazy<FrameSequence> m_errors;
    private readonly Lazy<FrameSequence> m_absoluteErrors;
    private readonly Lazy<FrameSequence> m_errorDisplaySteps;
    private readonly Lazy<FrameSequence> m_displaySteps;
    private readonly Lazy<FrameSequence> m_holds;
    private readonly Lazy<FrameSequence> m_lateHolds;
    private readonly Lazy<FrameSequence> m_frameTimes;
    private readonly Lazy<FrameSequence> m_cpuBusy;
    private readonly Lazy<RankBits> m_spans;
    private readonly Lazy<WaveletMatrix> m_frameTimesAndCpuBusy;
    private readonly Lazy<FrameSequence> m_drift;
    private readonly Lazy<LateShareData?> m_lateShare;
    private readonly Lazy<int[]> m_segmentEnds;

    private RunChartData(ChartRun run)
    {
      Run = run;
      var frames = run.Run.Frames;
      // Display order (a run's frames are in it); a run that is not keeps a sorted copy here
      bool sorted = true;
      for (int i = 1; i < frames.Count && sorted; ++i)
        sorted = frames[i].FirstSeenTicks >= frames[i - 1].FirstSeenTicks;
      Frames = sorted ? frames : frames.OrderBy(f => f.FirstSeenTicks).ToArray();
      OriginTicks = run.Run.Frames.Count > 0 ? run.Run.Frames[0].FirstSeenTicks : 0;
      int count = Frames.Count;

      Lazy<T> Once<T>(Func<T> create) => new Lazy<T>(create, LazyThreadSafetyMode.ExecutionAndPublication);
      bool HasNext(int i) => i + 1 < count && Frames[i + 1].Segment == Frames[i].Segment;
      m_errors = Once(() => new FrameSequence(count, i => Frames[i].AnimationErrorTicks));
      m_absoluteErrors = Once(() => new FrameSequence(count, i => Frames[i].AnimationErrorTicks is { } e ? Math.Abs(e) : null));
      m_errorDisplaySteps = Once(() => new FrameSequence(count, i => Frames[i].AnimationErrorTicks.HasValue ? Frames[i].DisplayDeltaTicks : null));
      m_displaySteps = Once(() => new FrameSequence(count, i => Frames[i].DisplayDeltaTicks));
      m_holds = Once(() => new FrameSequence(count, i => HasNext(i) ? Frames[i + 1].DisplayDeltaTicks : null));
      m_lateHolds = Once(() =>
        new FrameSequence(count, i => HasNext(i) && (Frames[i + 1].Flags & PresentedFrameFlags.Late) != 0 ? Frames[i + 1].DisplayDeltaTicks : null)
      );
      m_frameTimes = Once(() => new FrameSequence(count, i => Frames[i].FrameTimeTicks is > 0 and var t ? t : null));
      m_cpuBusy = Once(() => new FrameSequence(count, i => Frames[i].CpuBusyTicks > 0 ? Frames[i].CpuBusyTicks : null));
      m_spans = Once(() => new RankBits(count, i => Frames[i].FrameTimeTicks is > 0 || Frames[i].CpuBusyTicks > 0));
      m_frameTimesAndCpuBusy = Once(() =>
      {
        // Per frame its frametime, then its CPU busy (each when above 0): a range of frames starts at its frametimes' plus its CPU busys' start
        var values = new List<long>(FrameTimes.Count + CpuBusy.Count);
        for (int i = 0; i < count; ++i)
        {
          if (Frames[i].FrameTimeTicks is > 0 and var t)
            values.Add(t);
          if (Frames[i].CpuBusyTicks > 0)
            values.Add(Frames[i].CpuBusyTicks);
        }
        return new WaveletMatrix(values.ToArray());
      });
      m_drift = Once(() => new FrameSequence(count, i => Frames[i].DriftTicks));
      m_lateShare = Once(() => run.Run.Pacing is { } pacing ? LateShareData.Create(Frames, pacing) : null);
      m_segmentEnds = Once(() => Enumerable.Range(0, count).Where(i => !HasNext(i)).ToArray());
    }

    /// <summary>The prepared data of <paramref name="run"/>: made on first use and kept as long as the run is.</summary>
    public static RunChartData Of(ChartRun run) => g_cache.GetValue(run, r => new RunChartData(r));

    public ChartRun Run { get; }

    /// <summary>The run's frames in display order.</summary>
    public IReadOnlyList<PresentedFrame> Frames { get; }

    /// <summary>The time at 0 s: the run's first frame.</summary>
    public long OriginTicks { get; }

    public FrameSequence Errors => m_errors.Value;

    public FrameSequence AbsoluteErrors => m_absoluteErrors.Value;

    /// <summary>The display time steps of the frames with an animation error (the histogram's frames).</summary>
    public FrameSequence ErrorDisplaySteps => m_errorDisplaySteps.Value;

    public FrameSequence DisplaySteps => m_displaySteps.Value;

    /// <summary>Each frame's hold: until the next frame of its segment, at the next frame's display time step.</summary>
    public FrameSequence Holds => m_holds.Value;

    /// <summary>The holds whose next frame is late (held too long).</summary>
    public FrameSequence LateHolds => m_lateHolds.Value;

    public FrameSequence FrameTimes => m_frameTimes.Value;

    public FrameSequence CpuBusy => m_cpuBusy.Value;

    /// <summary>The frames with a frametime or a CPU busy: the frametime panel's spans.</summary>
    public RankBits Spans => m_spans.Value;

    /// <summary>
    /// Every frametime and CPU busy above 0, per frame in that order: frames <c>a</c> to <c>b</c> are positions
    /// <c>FrameTimes.Frames.Rank(a) + CpuBusy.Frames.Rank(a)</c> to the same at <c>b</c>.
    /// </summary>
    public WaveletMatrix FrameTimesAndCpuBusy => m_frameTimesAndCpuBusy.Value;

    public FrameSequence Drift => m_drift.Value;

    /// <summary>The late share over the whole run, when it has pacing information.</summary>
    public LateShareData? LateShare => m_lateShare.Value;

    /// <summary>The frames without a next frame in their segment, in order.</summary>
    public IReadOnlyList<int> SegmentEnds => m_segmentEnds.Value;

    /// <summary>Frame <paramref name="index"/>'s display time in seconds since the run's first frame.</summary>
    public double Seconds(int index) => (Frames[index].FirstSeenTicks - OriginTicks) / (double)TimeSpan.TicksPerSecond;

    /// <summary>The frames first seen from <paramref name="fromSeconds"/> to <paramref name="toSeconds"/> (both included), as a range.</summary>
    public (int Start, int End) Range(double fromSeconds, double toSeconds) =>
      (FirstWhere(0, Frames.Count, i => Seconds(i) >= fromSeconds), FirstWhere(0, Frames.Count, i => Seconds(i) > toSeconds));

    /// <summary>The first index from <paramref name="start"/> to <paramref name="end"/> where <paramref name="reached"/> holds (it holds from some index on), or end.</summary>
    public static int FirstWhere(int start, int end, Func<int, bool> reached)
    {
      while (start < end)
      {
        int middle = start + ((end - start) / 2);
        if (reached(middle))
          end = middle;
        else
          start = middle + 1;
      }
      return start;
    }
  }
}
