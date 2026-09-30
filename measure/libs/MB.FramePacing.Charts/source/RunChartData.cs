//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the cards draw of a run, prepared once and kept with the run (immutable, cached per ChartRun): each kind of value as a frame sequence
//* with its wavelet matrix, the flags as rank bits, and the late share's rolling window over the whole run. A section is a range of frames
//* (found by binary search on the display times), and every pixel column's numbers come from queries on these, so a card costs per column,
//* not per frame, whatever the run's length. Everything is built on first use, once, and is safe to read from any thread.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
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
    private readonly Lazy<FrameSequence> m_frameRateSteps;
    private readonly Lazy<FrameSequence> m_displaySteps;
    private readonly Lazy<FrameSequence> m_holds;
    private readonly Lazy<FrameSequence> m_animatingHolds;
    private readonly Lazy<(int Start, int End)[]> m_staticStretches;
    private readonly Lazy<FrameSequence> m_lateHolds;
    private readonly Lazy<HoldKind[]> m_holdKinds;
    private readonly Lazy<Dictionary<HoldKind, FrameSequence>> m_holdsByKind;
    private readonly Lazy<long[]> m_droppedBefore;
    private readonly Lazy<RankBits> m_uncertainSteps;
    private readonly Lazy<RankBits> m_staticSteps;
    private readonly Lazy<FrameSequence> m_animationHolds;
    private readonly Lazy<FrameSequence> m_frameTimes;
    private readonly Lazy<FrameSequence> m_cpuBusy;
    private readonly Lazy<FrameSequence> m_animatingFrameTimes;
    private readonly Lazy<RankBits> m_spans;
    private readonly Lazy<WaveletMatrix> m_frameTimesAndCpuBusy;
    private readonly Lazy<FrameSequence> m_drift;
    private readonly Lazy<LateShareData?> m_lateShare;
    private readonly Lazy<int[]> m_segmentEnds;
    private readonly Lazy<RunEvents> m_events;
    private readonly Lazy<ReferenceStretch[]> m_stepReferences;
    private readonly Lazy<ReferenceStretch[]> m_frameTimeReferences;
    private readonly Lazy<FrameSequence> m_animatingStepReferences;
    private readonly Lazy<FrameSequence> m_animatingFrameTimeReferences;

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
      m_frameRateSteps = Once(() =>
        new FrameSequence(count, i => RunStatistics.CountsTowardFrameRate(Frames[i]) ? Frames[i].DisplayDeltaTicks : null)
      );
      m_displaySteps = Once(() => new FrameSequence(count, i => Frames[i].DisplayDeltaTicks));
      m_holds = Once(() => new FrameSequence(count, i => HasNext(i) ? Frames[i + 1].DisplayDeltaTicks : null));
      m_animatingHolds = Once(() =>
        new FrameSequence(count, i => HasNext(i) && (Frames[i].Flags & PresentedFrameFlags.StaticAfter) == 0 ? Frames[i + 1].DisplayDeltaTicks : null)
      );
      m_staticStretches = Once(() =>
      {
        var stretches = new List<(int Start, int End)>();
        for (int i = 0; i < count; ++i)
        {
          if ((Frames[i].Flags & PresentedFrameFlags.StaticAfter) == 0)
            continue;
          int end = i + 1;
          while (end < count && (Frames[end].Flags & PresentedFrameFlags.StaticAfter) != 0 && Frames[end].Segment == Frames[i].Segment)
            ++end;
          stretches.Add((i, end));
          i = end - 1;
        }
        return stretches.ToArray();
      });
      m_lateHolds = Once(() =>
        new FrameSequence(count, i => HasNext(i) && (Frames[i + 1].Flags & PresentedFrameFlags.Late) != 0 ? Frames[i + 1].DisplayDeltaTicks : null)
      );
      m_animationHolds = Once(() => new FrameSequence(count, i => HasNext(i) ? Frames[i + 1].AnimationDeltaTicks : null));
      m_frameTimes = Once(() => new FrameSequence(count, i => Frames[i].FrameTimeTicks is > 0 and var t ? t : null));
      m_cpuBusy = Once(() => new FrameSequence(count, i => Frames[i].CpuBusyTicks > 0 ? Frames[i].CpuBusyTicks : null));
      m_spans = Once(() => new RankBits(count, i => Frames[i].FrameTimeTicks is > 0 || Frames[i].CpuBusyTicks > 0));
      m_droppedBefore = Once(() => DroppedFrames.Before(Frames));
      m_holdKinds = Once(() =>
      {
        // A hold's kind, the first that applies: not known (a capture gap made the next step uncertain), late, an older frame came back
        // while it was the newest, frames never shown before the next, as planned
        var kinds = new HoldKind[count];
        for (int i = 0; i + 1 < count; ++i)
        {
          if (!HasNext(i))
            continue;
          var next = Frames[i + 1];
          kinds[i] =
            (next.Flags & PresentedFrameFlags.UncertainStep) != 0 ? HoldKind.Unknown
            : (next.Flags & PresentedFrameFlags.Late) != 0 ? HoldKind.Late
            : Frames[i].OlderFrames is { Count: > 0 } ? HoldKind.OlderFrameBack
            : DroppedBeforeFrame[i + 1] > 0 ? HoldKind.FramesDropped
            : HoldKind.AsPlanned;
        }
        return kinds;
      });
      m_holdsByKind = Once(() =>
        Enum.GetValues<HoldKind>()
          .ToDictionary(
            kind => kind,
            kind => new FrameSequence(count, i => HasNext(i) && HoldKinds[i] == kind ? Frames[i + 1].DisplayDeltaTicks : null)
          )
      );
      m_uncertainSteps = Once(() =>
        new RankBits(
          count,
          i =>
            Frames[i].DisplayDeltaTicks.HasValue
            && (Frames[i].Flags & (PresentedFrameFlags.UncertainStep | PresentedFrameFlags.StaticBefore)) == PresentedFrameFlags.UncertainStep
        )
      );
      m_staticSteps = Once(() =>
        new RankBits(count, i => Frames[i].DisplayDeltaTicks.HasValue && (Frames[i].Flags & PresentedFrameFlags.StaticBefore) != 0)
      );
      m_animatingFrameTimes = Once(() =>
        new FrameSequence(
          count,
          i => (Frames[i].Flags & PresentedFrameFlags.StaticAfter) == 0 && Frames[i].FrameTimeTicks is > 0 and var t ? t : null
        )
      );
      m_frameTimesAndCpuBusy = Once(() =>
      {
        // Per frame its frametime (not a static frame's: an idle wait), then its CPU busy (each when above 0): a range of frames starts at its
        // animating frametimes' plus its CPU busys' start
        var values = new List<long>(AnimatingFrameTimes.Count + CpuBusy.Count);
        for (int i = 0; i < count; ++i)
        {
          if ((Frames[i].Flags & PresentedFrameFlags.StaticAfter) == 0 && Frames[i].FrameTimeTicks is > 0 and var t)
            values.Add(t);
          if (Frames[i].CpuBusyTicks > 0)
            values.Add(Frames[i].CpuBusyTicks);
        }
        return new WaveletMatrix(values.ToArray());
      });
      m_drift = Once(() => new FrameSequence(count, i => Frames[i].DriftTicks));
      m_lateShare = Once(() => run.Run.Pacing is { } pacing ? LateShareData.Create(Frames, pacing) : null);
      m_segmentEnds = Once(() => Enumerable.Range(0, count).Where(i => !HasNext(i)).ToArray());
      m_events = Once(() => RunEvents.Of(this));

      // What each hold was aimed at: the target and preferred frame time of the frame that ends it. The display time step panel draws them
      // in whole refreshes, as the analysis compares; the frametime panel as written. Without pacing (no refresh rate) there are none
      long refresh = run.Run.Pacing is { } pacing ? (long)Math.Round(pacing.RefreshPeriodMs * TimeSpan.TicksPerMillisecond) : 0;
      long? Rounded(long? ticks) => ticks is { } t ? FrameTimeRounding.WholeRefreshes(t, refresh) : null;
      ReferenceStretch[] Stretches(Func<PresentedFrame, long?> target, Func<PresentedFrame, long?> preferred)
      {
        var stretches = new List<ReferenceStretch>();
        for (int i = 0; refresh > 0 && i < count; ++i)
        {
          if (!HasNext(i))
            continue;
          var next = Frames[i + 1];
          var (t, p) = (target(next), preferred(next));
          if (t == null && p == null)
            continue;
          if (stretches.Count > 0 && stretches[^1] is var last && last.End == i && last.TargetTicks == t && last.PreferredTicks == p)
            stretches[^1] = last with { End = i + 1 };
          else
            stretches.Add(new ReferenceStretch(i, i + 1, t, p));
        }
        return stretches.ToArray();
      }
      // The scales take the lines of the holds that animate (an idle screen's aim would squash them, as its hold would)
      FrameSequence Animating(Func<PresentedFrame, long?> target, Func<PresentedFrame, long?> preferred) =>
        new FrameSequence(
          count,
          i =>
            refresh > 0 && HasNext(i) && (Frames[i].Flags & PresentedFrameFlags.StaticAfter) == 0
              ? (target(Frames[i + 1]), preferred(Frames[i + 1])) switch
              {
                (null, null) => null,
                var (t, p) => Math.Max(t ?? 0, p ?? 0),
              }
              : null
        );
      m_stepReferences = Once(() => Stretches(f => Rounded(FrameReference.Target(f)), f => Rounded(FrameReference.Preferred(f))));
      m_frameTimeReferences = Once(() => Stretches(FrameReference.Target, FrameReference.Preferred));
      m_animatingStepReferences = Once(() => Animating(f => Rounded(FrameReference.Target(f)), f => Rounded(FrameReference.Preferred(f))));
      m_animatingFrameTimeReferences = Once(() => Animating(FrameReference.Target, FrameReference.Preferred));
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

    /// <summary>The display time steps that count toward the frame rate (a static frame's time on screen does not): the histogram's.</summary>
    public FrameSequence FrameRateSteps => m_frameRateSteps.Value;

    public FrameSequence DisplaySteps => m_displaySteps.Value;

    /// <summary>Each frame's hold: until the next frame of its segment, at the next frame's display time step.</summary>
    public FrameSequence Holds => m_holds.Value;

    /// <summary>The holds of the frames that animate: a static frame's hold (an idle screen) is left out, as the display time step scale is.</summary>
    public FrameSequence AnimatingHolds => m_animatingHolds.Value;

    /// <summary>Runs of consecutive static frames of one segment, as frame ranges (start, end), in display order.</summary>
    public IReadOnlyList<(int Start, int End)> StaticStretches => m_staticStretches.Value;

    /// <summary>Each frame's hold kind (<see cref="HoldKind"/>): how the display time step panel draws it.</summary>
    public IReadOnlyList<HoldKind> HoldKinds => m_holdKinds.Value;

    /// <summary>The holds of one kind: for the columns of a zoomed out panel.</summary>
    public FrameSequence HoldsOf(HoldKind kind) => m_holdsByKind.Value[kind];

    /// <summary>
    /// Per frame, the frames the target dropped just before it: frame indices it skipped that never reached the display (not even out of
    /// order later in the segment), 0 when a capture gap came before it (they may have been shown in the refreshes the capture missed).
    /// </summary>
    public IReadOnlyList<long> DroppedBeforeFrame => m_droppedBefore.Value;

    /// <summary>The frames whose display time step a capture gap made uncertain (not judged); a static frame's is counted as static.</summary>
    public RankBits UncertainSteps => m_uncertainSteps.Value;

    /// <summary>The frames whose display time step is a static frame's time on screen (RunStatistics.ExcludedStaticFrames).</summary>
    public RankBits StaticSteps => m_staticSteps.Value;

    /// <summary>The holds whose next frame is late (held too long).</summary>
    public FrameSequence LateHolds => m_lateHolds.Value;

    /// <summary>Each frame's hold at the next frame's animation time step: the animation time step over the display time step.</summary>
    public FrameSequence AnimationHolds => m_animationHolds.Value;

    public FrameSequence FrameTimes => m_frameTimes.Value;

    public FrameSequence CpuBusy => m_cpuBusy.Value;

    /// <summary>The frames with a frametime or a CPU busy: the frametime panel's spans.</summary>
    public RankBits Spans => m_spans.Value;

    /// <summary>The frametimes of the frames that animate: a static frame's (an idle wait) is left out, as the frametime scale is.</summary>
    public FrameSequence AnimatingFrameTimes => m_animatingFrameTimes.Value;

    /// <summary>
    /// The frametime panel's scale values: every frametime of a frame that animates and every CPU busy above 0, per frame in that order: frames
    /// <c>a</c> to <c>b</c> are positions <c>AnimatingFrameTimes.Frames.Rank(a) + CpuBusy.Frames.Rank(a)</c> to the same at <c>b</c>.
    /// </summary>
    public WaveletMatrix FrameTimesAndCpuBusy => m_frameTimesAndCpuBusy.Value;

    public FrameSequence Drift => m_drift.Value;

    /// <summary>The late share over the whole run, when it has pacing information.</summary>
    public LateShareData? LateShare => m_lateShare.Value;

    /// <summary>The display time step panel's reference lines: stretches of holds with one target and preferred frame time, in whole refreshes.</summary>
    public IReadOnlyList<ReferenceStretch> StepReferences => m_stepReferences.Value;

    /// <summary>The frametime panel's reference lines: the same stretches with the frame times as written.</summary>
    public IReadOnlyList<ReferenceStretch> FrameTimeReferences => m_frameTimeReferences.Value;

    /// <summary>Per frame that animates, the higher of its hold's target and preferred frame time in whole refreshes: for the step scale.</summary>
    public FrameSequence AnimatingStepReferences => m_animatingStepReferences.Value;

    /// <summary>Per frame that animates, the higher of its frametime's target and preferred frame time as written: for the frametime scale.</summary>
    public FrameSequence AnimatingFrameTimeReferences => m_animatingFrameTimeReferences.Value;

    /// <summary>What went wrong when, in the frames and in the capture: the events lanes'.</summary>
    public RunEvents Events => m_events.Value;

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
