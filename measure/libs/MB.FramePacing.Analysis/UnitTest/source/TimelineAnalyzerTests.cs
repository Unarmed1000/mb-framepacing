//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Timeline rules on hand-built capture rows.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.MarkerDecoding;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  public class TimelineAnalyzerTests
  {
    private const long Ms = NanosecondTimeSpan.NanosecondsPerMillisecond;
    private const long Period = 4 * Ms; // 250 fps capture

    /// <summary>Builds rows at a fixed capture period. Each call appends <paramref name="captures"/> captures showing one marker.</summary>
    private sealed class RowBuilder
    {
      public readonly List<CaptureRow> Rows = new List<CaptureRow>();

      private long Next => Rows.Count;

      public RowBuilder Show(
        ulong frameIndex,
        long animationMs,
        int captures,
        uint runId = 1,
        MarkerKind kind = MarkerKind.Frame,
        StartMetadata? start = null,
        long targetFrameNanoseconds = 0,
        long preferredFrameNanoseconds = 0,
        MB.FramePacing.Marker.MarkerFlags flags = MB.FramePacing.Marker.MarkerFlags.NoFlags
      )
      {
        var payload = new MarkerPayload(
          kind,
          runId,
          frameIndex,
          flags,
          new NanosecondTimeSpan(animationMs * Ms),
          PreferredFrameTime: NanosecondTimeDuration.FromNanoseconds(preferredFrameNanoseconds),
          TargetFrameTime: NanosecondTimeDuration.FromNanoseconds(targetFrameNanoseconds)
        );
        for (int i = 0; i < captures; ++i)
          Rows.Add(new CaptureRow(Next, new NanosecondTickCount(Next * Period), CaptureStatus.Decoded, payload, start));
        return this;
      }

      public RowBuilder Start(uint runId, int captures = 3, string name = "test") =>
        Show(
          0,
          0,
          captures,
          runId,
          MarkerKind.SequenceStart,
          StartMetadata.FromTag(new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc).Ticks, name)
        );

      public RowBuilder End(uint runId, int captures = 3) => Show(999_999, 0, captures, runId, MarkerKind.SequenceEnd);

      public RowBuilder Status(CaptureStatus status, int captures = 1)
      {
        for (int i = 0; i < captures; ++i)
          Rows.Add(new CaptureRow(Next, status == CaptureStatus.NotRecorded ? default : new NanosecondTickCount(Next * Period), status, default));
        return this;
      }
    }

    [Test]
    public void PerfectPacing_HasNoAnimationError()
    {
      // 62.5 Hz game (4 captures per frame), animation advances 16 ms per frame
      var rows = new RowBuilder().Start(1);
      for (ulong f = 0; f < 20; ++f)
        rows.Show(100 + f, (long)f * 16, 4);
      rows.End(1);

      var result = TimelineAnalyzer.Analyze(rows.Rows);

      Assert.That(result.CapturePeriod.Nanoseconds, Is.EqualTo(Period));
      var run = result.Runs.Single();
      Assert.That(run.SequenceId, Is.EqualTo("test"));
      Assert.That(run.HasStartMarker && run.HasEndMarker);
      Assert.That(run.Counts.PresentedFrames, Is.EqualTo(20));
      Assert.That(run.Frames.Skip(1).All(f => f.AnimationError?.Nanoseconds == 0), Is.True);
      Assert.That(run.Statistics.DisplayDeltaMs.Mean, Is.EqualTo(16).Within(1e-9));
      Assert.That(run.Statistics.FramesWithAnimationError, Is.Zero);
      Assert.That(run.Frames[5].OnScreen.Nanoseconds, Is.EqualTo(16 * Ms));
    }

    [Test]
    public void Stall_ShowsAsNegativeErrorOnTheLateFrame()
    {
      var rows = new RowBuilder().Start(1);
      rows.Show(1, 0, 4).Show(2, 16, 4).Show(3, 32, 8).Show(4, 48, 4).Show(5, 64, 4);
      rows.End(1);

      var frames = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single().Frames;

      // Frame 3 stays on screen for 32 ms, so frame 4 appears 32 ms after frame 3 but its animation advanced only 16 ms
      Assert.That(frames[2].OnScreen.Nanoseconds, Is.EqualTo(32 * Ms));
      Assert.That(frames[3].DisplayDelta?.Nanoseconds, Is.EqualTo(32 * Ms));
      Assert.That(frames[3].AnimationError?.Nanoseconds, Is.EqualTo(-16 * Ms));
      Assert.That(frames[3].Drift.Nanoseconds, Is.EqualTo(-16 * Ms));
      Assert.That(frames[4].AnimationError?.Nanoseconds, Is.EqualTo(0));
    }

    /// <summary>
    /// A capture the recorder dropped right before a frame's first sighting: the frame may have appeared in it, so its display time is
    /// uncertain. The steps into and out of it are not judged (no animation error, no late verdict, not in the frame rates) and counted.
    /// </summary>
    [Test]
    public void CaptureGap_TheStepsAroundItAreNotJudged()
    {
      var rows = new RowBuilder().Start(1);
      rows.Show(1, 0, 4).Show(2, 16, 4).Show(3, 32, 4);
      rows.Status(CaptureStatus.NotRecorded).Show(4, 48, 3);
      rows.Show(5, 64, 4).Show(6, 80, 4).Show(7, 96, 4);
      rows.End(1);

      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();
      var frames = run.Frames;

      Assert.That(
        frames.Select(f => f.Flags.HasFlag(PresentedFrameFlags.UncertainStep)),
        Is.EqualTo(new[] { false, false, false, true, true, false, false })
      );
      Assert.That(frames[3].AnimationError?.Nanoseconds, Is.Null, "the step into the frame after the gap");
      Assert.That(frames[4].AnimationError?.Nanoseconds, Is.Null, "the step out of it");
      Assert.That(frames[3].DisplayDelta?.Nanoseconds, Is.EqualTo(20 * Ms), "what the capture saw is still stored");
      Assert.That(
        frames.Where(f => f.Flags.HasFlag(PresentedFrameFlags.UncertainStep)).Select(f => f.Flags.HasFlag(PresentedFrameFlags.Late)),
        Is.All.False
      );
      Assert.That(run.Statistics.UncertainSteps, Is.EqualTo(2));
      Assert.That(run.Statistics.DisplayDeltaMs.Count, Is.EqualTo(4), "the frame rates leave the two steps out");
      Assert.That(run.Statistics.FramesWithAnimationError, Is.Zero, "no error made up by the gap");
      Assert.That(run.Counts.NotRecorded, Is.EqualTo(1));
    }

    /// <summary>A capture source that reports dropping frames before a capture leaves the same gap as a recorder that could not record one.</summary>
    [Test]
    public void SourceDrop_IsACaptureGapToo()
    {
      var rows = new RowBuilder().Start(1);
      rows.Show(1, 0, 4).Show(2, 16, 4).Show(3, 32, 4);
      int dropped = rows.Rows.Count;
      rows.Show(4, 48, 3);
      rows.Rows[dropped] = rows.Rows[dropped] with { SourceDrops = 2 };
      rows.Show(5, 64, 4).Show(6, 80, 4);
      rows.End(1);

      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();

      Assert.That(run.Frames[3].Flags.HasFlag(PresentedFrameFlags.UncertainStart));
      Assert.That(run.Frames.Count(f => f.Flags.HasFlag(PresentedFrameFlags.UncertainStep)), Is.EqualTo(2));
      Assert.That(run.Counts.SourceDroppedFrames, Is.EqualTo(2));
    }

    /// <summary>A capture that shows an older frame again is kept with the newest frame (the one presented), at its capture's time.</summary>
    [Test]
    public void OutOfOrderCapture_IsKeptWithTheNewestFrame()
    {
      var rows = new RowBuilder().Start(1);
      rows.Show(1, 0, 4).Show(3, 32, 4);
      int older = rows.Rows.Count;
      rows.Show(2, 16, 1).Show(4, 48, 4).Show(5, 64, 4);
      rows.End(1);

      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();

      Assert.That(run.Frames.Select(f => f.FrameIndex), Is.EqualTo(new ulong[] { 1, 3, 4, 5 }));
      Assert.That(run.Frames[1].OlderFrames, Is.EqualTo(new[] { new OlderFrameCapture(new NanosecondTickCount(older * Period), 2) }));
      Assert.That(run.Frames.Where((_, i) => i != 1).Select(f => f.OlderFrames), Is.All.Null);
      Assert.That(run.Counts.OutOfOrderCaptures, Is.EqualTo(1));
    }

    /// <summary>
    /// A second of idle screen after frame 4, said by frame 4 itself (StaticAfter: no pending work after it), by frame 5 (StaticBefore: the
    /// application only knew it then) or by both: the same static step either way. The step into frame 4 is judged (it still moved to its
    /// rest), the step out of it is not, whether the animation clock paused while nothing animated (64 ms) or kept running (1064 ms), and the
    /// drift does not jump. The frame rate numbers leave out frame 4's time on screen, and count it.
    /// </summary>
    [TestCase(64L, MB.FramePacing.Marker.MarkerFlags.StaticAfter, MB.FramePacing.Marker.MarkerFlags.NoFlags)]
    [TestCase(1064L, MB.FramePacing.Marker.MarkerFlags.StaticAfter, MB.FramePacing.Marker.MarkerFlags.NoFlags)]
    [TestCase(64L, MB.FramePacing.Marker.MarkerFlags.NoFlags, MB.FramePacing.Marker.MarkerFlags.StaticBefore)]
    [TestCase(1064L, MB.FramePacing.Marker.MarkerFlags.NoFlags, MB.FramePacing.Marker.MarkerFlags.StaticBefore)]
    [TestCase(64L, MB.FramePacing.Marker.MarkerFlags.StaticAfter, MB.FramePacing.Marker.MarkerFlags.StaticBefore)]
    public void StaticStep_SaidByEitherFrame_IsNotJudged(
      long animationAfterIdleMs,
      MB.FramePacing.Marker.MarkerFlags fourth,
      MB.FramePacing.Marker.MarkerFlags fifth
    )
    {
      var rows = new RowBuilder().Start(1);
      rows.Show(1, 0, 4).Show(2, 16, 4).Show(3, 32, 4);
      rows.Show(4, 48, 250, flags: fourth);
      rows.Show(5, animationAfterIdleMs, 4, flags: fifth).Show(6, animationAfterIdleMs + 16, 4);
      rows.End(1);

      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();
      var frames = run.Frames;

      Assert.That(
        frames.Select(f => f.Flags.HasFlag(PresentedFrameFlags.StaticAfter)),
        Is.EqualTo(new[] { false, false, false, true, false, false })
      );
      Assert.That(frames[3].AnimationError?.Nanoseconds, Is.EqualTo(0), "the step to the static frame: judged");
      Assert.That(frames[4].AnimationError?.Nanoseconds, Is.Null, "the step from it");
      Assert.That(frames[4].DisplayDelta?.Nanoseconds, Is.EqualTo(1000 * Ms), "the static frame's time on screen");
      Assert.That(
        frames.Select(f => f.Flags.HasFlag(PresentedFrameFlags.StaticBefore)),
        Is.EqualTo(new[] { false, false, false, false, true, false })
      );
      Assert.That(frames[5].AnimationError?.Nanoseconds, Is.EqualTo(0));
      Assert.That(frames.Select(f => f.Drift.Nanoseconds), Is.All.EqualTo(0L), "the drift adds up only the judged steps");
      Assert.That(run.Statistics.FramesWithAnimationError, Is.Zero);
      // The frame rates: the four 16 ms steps (the last animated frame's time on screen, before the static one, included)
      Assert.That(run.Statistics.ExcludedStaticFrames, Is.EqualTo(1));
      Assert.That(run.Statistics.DisplayDeltaMs.Count, Is.EqualTo(4));
      Assert.That(run.Statistics.DisplayDeltaMs.Max, Is.EqualTo(16));
      Assert.That(run.Statistics.AverageFps, Is.EqualTo(62.5).Within(1e-9));
    }

    /// <summary>
    /// StaticBefore speaks for the frame index before it. When the target dropped that frame (frame 4, the rest's), the flag still arrived:
    /// the frame shown before it held the rest, and the analysis assumes it static (StaticAssumed). Without the guess the flag marks nothing
    /// and the rest is judged like a stall.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void StaticBefore_AfterADroppedFrame_IsAssumedStatic_UnlessTheGuessIsOff(bool assume)
    {
      var rows = new RowBuilder().Start(1);
      // Frame 3 stays 120 ms: frame 4, the rest's, is never shown; the animation clock stands still through the rest
      rows.Show(1, 0, 4).Show(2, 16, 4).Show(3, 32, 30);
      rows.Show(5, 64, 4, flags: MB.FramePacing.Marker.MarkerFlags.StaticBefore).Show(6, 80, 4);
      rows.End(1);

      var run = TimelineAnalyzer.Analyze(rows.Rows, new TimelineOptions { AssumeStatic = assume }).Runs.Single();
      var frames = run.Frames;

      var held = assume ? PresentedFrameFlags.StaticAfter | PresentedFrameFlags.StaticAssumed : PresentedFrameFlags.None;
      Assert.That(frames[2].Flags & (PresentedFrameFlags.StaticAfter | PresentedFrameFlags.StaticAssumed), Is.EqualTo(held), "frame 3 held the rest");
      Assert.That(frames[3].Flags.HasFlag(PresentedFrameFlags.StaticBefore), Is.EqualTo(assume));
      Assert.That(frames[3].AnimationError?.Nanoseconds, Is.EqualTo(assume ? null : (32 - 120) * Ms), "the rest: assumed static, or judged");
      Assert.That(run.Statistics.ExcludedStaticFrames, Is.EqualTo(assume ? 1 : 0));
      Assert.That(frames.Count(f => f.Flags.HasFlag(PresentedFrameFlags.StaticAssumed)), Is.EqualTo(assume ? 1 : 0));
    }

    /// <summary>
    /// A 62.5 fps run (16 ms target, 4 captures per frame) in which frame 6 stays on screen and the frame after it may be dropped: the frames
    /// after frame 6, and whether the analysis assumed frame 6 static. With <paramref name="staticElsewhere"/> frame 2 is a flagged rest.
    /// </summary>
    private static (PresentedFrame Held, PresentedFrame After) LostFlag(
      bool staticElsewhere = true,
      long animationStepMs = 32,
      int holdCaptures = 30,
      bool captureGap = false,
      bool dropped = true,
      bool onDemand = false,
      bool assume = true
    )
    {
      const long Target = 16 * Ms;
      var rows = new RowBuilder().Start(1);
      rows.Show(1, 0, 4, targetFrameNanoseconds: Target);
      if (staticElsewhere)
        rows.Show(2, 16, 30, targetFrameNanoseconds: Target, flags: MB.FramePacing.Marker.MarkerFlags.StaticAfter);
      else
        rows.Show(2, 16, 4, targetFrameNanoseconds: Target);
      rows.Show(3, 32, 4, targetFrameNanoseconds: Target)
        .Show(4, 48, 4, targetFrameNanoseconds: Target)
        .Show(5, 64, 4, targetFrameNanoseconds: Target);
      rows.Show(6, 80, holdCaptures, targetFrameNanoseconds: Target);
      if (captureGap)
        rows.Status(CaptureStatus.Undecodable, 2);
      ulong next = dropped ? 8u : 7u;
      long aim = onDemand ? MarkerPayload.OnDemandFrameTime.Nanoseconds : Target;
      rows.Show(next, 80 + animationStepMs, 4, targetFrameNanoseconds: aim).Show(next + 1, 96 + animationStepMs, 4, targetFrameNanoseconds: aim);
      rows.End(1);
      var frames = TimelineAnalyzer.Analyze(rows.Rows, new TimelineOptions { AssumeStatic = assume }).Runs.Single().Frames;
      return (frames[5], frames[6]);
    }

    /// <summary>
    /// The frame that woke the application was dropped, and its StaticBefore with it. In a run that uses the static flags, a hold beyond what
    /// the frames in between were due, over which the animation clock stood still, is assumed to be that rest.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void LostStaticFlag_IsAssumed_WhenTheClockStoodStillOverALongHold(bool onDemand)
    {
      var (held, after) = LostFlag(onDemand: onDemand);

      Assert.That(held.Flags.HasFlag(PresentedFrameFlags.StaticAfter | PresentedFrameFlags.StaticAssumed), "frame 6 held a rest");
      Assert.That(after.Flags.HasFlag(PresentedFrameFlags.StaticBefore));
      Assert.That(after.AnimationError, Is.Null, "the rest is not judged");
    }

    /// <summary>
    /// Nothing is assumed without all of it: static flags elsewhere in the run, a dropped frame, no capture gap, a hold beyond what was due,
    /// a clock that stood still (a stall with a running clock is a stall), and the guess switched on. The step is then judged.
    /// </summary>
    [Test]
    public void LostStaticFlag_IsNotAssumed_WithoutEveryCondition()
    {
      void Judged((PresentedFrame Held, PresentedFrame After) frames, long errorMs, string why)
      {
        Assert.That(
          frames.Held.Flags & (PresentedFrameFlags.StaticAfter | PresentedFrameFlags.StaticAssumed),
          Is.EqualTo(PresentedFrameFlags.None),
          why
        );
        Assert.That(frames.After.AnimationError?.Nanoseconds, Is.EqualTo(errorMs * Ms), why);
      }
      Judged(LostFlag(staticElsewhere: false), 32 - 120, "the run does not use the static flags");
      Judged(LostFlag(animationStepMs: 120), 0, "the animation clock kept running: a stall");
      Judged(LostFlag(holdCaptures: 8), 0, "the hold is what two frames were due");
      Judged(LostFlag(dropped: false), 32 - 120, "no frame was dropped: a long hold is a stall");
      Judged(LostFlag(assume: false), 32 - 120, "the guess is switched off");
      var gap = LostFlag(captureGap: true);
      Assert.That(gap.Held.Flags.HasFlag(PresentedFrameFlags.StaticAssumed), Is.False, "a capture gap: the frame may have been shown");
      Assert.That(gap.After.Flags.HasFlag(PresentedFrameFlags.UncertainStep), "and the step is uncertain, not static");
    }

    /// <summary>An application that presents on demand has no interval to be late against: a long wait for its next frame is not late.</summary>
    [Test]
    public void OnDemand_IsNeverLateByTheTargetRule()
    {
      long OnDemand = MarkerPayload.OnDemandFrameTime.Nanoseconds;
      var rows = new RowBuilder().Start(1);
      rows.Show(1, 0, 4, targetFrameNanoseconds: OnDemand, preferredFrameNanoseconds: OnDemand);
      rows.Show(2, 16, 500, targetFrameNanoseconds: OnDemand, preferredFrameNanoseconds: OnDemand);
      rows.Show(3, 2016, 4, targetFrameNanoseconds: OnDemand, preferredFrameNanoseconds: OnDemand);
      rows.End(1);

      var frames = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single().Frames;

      Assert.That(frames.Any(f => f.Flags.HasFlag(PresentedFrameFlags.Late)), Is.False);
      Assert.That(frames.Select(f => f.TargetFrameTime?.Nanoseconds), Is.All.Null);
      Assert.That(frames.Select(f => f.PreferredFrameTime?.Nanoseconds), Is.All.Null);
      Assert.That(frames.Select(f => f.MarkerPreferredFrameTime.Nanoseconds), Is.All.EqualTo(OnDemand));
    }

    /// <summary>
    /// The frame time the application wants comes from its marker, in whole refreshes; without it the one refresh the display shows each
    /// frame for (no target frame rate given to the tools).
    /// </summary>
    [Test]
    public void PreferredFrameTime_IsTheMarkersElseOneRefresh()
    {
      var rows = new RowBuilder().Start(1);
      rows.Show(1, 0, 8, targetFrameNanoseconds: 32 * Ms, preferredFrameNanoseconds: 16 * Ms)
        .Show(2, 32, 8, targetFrameNanoseconds: 32 * Ms, preferredFrameNanoseconds: 16 * Ms);
      rows.Show(3, 64, 4).Show(4, 80, 4);
      rows.End(1);

      var frames = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single().Frames;

      Assert.That(frames[1].PreferredFrameTime?.Nanoseconds, Is.EqualTo(16 * Ms), "the marker's: the pacer runs slower than the application wants");
      Assert.That(frames[1].TargetFrameTime?.Nanoseconds, Is.EqualTo(32 * Ms));
      Assert.That(frames[3].PreferredFrameTime?.Nanoseconds, Is.EqualTo(Period), "without one: one refresh");
    }

    [Test]
    public void SkippedFrameIndex_IsCounted_AndShowsAsPositiveError()
    {
      var rows = new RowBuilder().Start(1);
      rows.Show(1, 0, 4).Show(2, 16, 4).Show(4, 48, 4).Show(5, 64, 4);
      rows.End(1);

      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();

      Assert.That(run.Counts.SkippedFrameIndices, Is.EqualTo(1));
      Assert.That(run.Frames[2].SkippedBefore, Is.EqualTo(1UL));
      Assert.That(run.Frames[2].Flags.HasFlag(PresentedFrameFlags.SkippedBefore));
      Assert.That(run.Frames[2].AnimationError?.Nanoseconds, Is.EqualTo(16 * Ms));
    }

    [Test]
    public void CaptureSlowerThanTheDisplay_IsReported()
    {
      // Every captured frame index is two after the previous one: every second displayed frame was never captured, like a 60 fps
      // recording of a 120 Hz display
      var rows = new RowBuilder().Start(1);
      for (ulong f = 0; f < 40; ++f)
        rows.Show(100 + (2 * f), (long)f * 16, 4);
      rows.End(1);

      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();

      Assert.That(run.Counts.SkippedFrameIndices, Is.EqualTo(39));
      Assert.That(run.Warnings, Has.Some.Contains("slower than the display's refresh rate"));
      Assert.That(run.Warnings, Has.Some.Contains("advances about 125 times per second; the capture records 250 frames per second"));
    }

    [Test]
    public void OccasionalSkips_AreNotReportedAsASlowCapture()
    {
      var rows = new RowBuilder().Start(1);
      for (ulong f = 0; f < 40; ++f)
        rows.Show(f == 20 ? 1000 + f + 1 : 1000 + f + (f > 20 ? 1UL : 0UL), (long)f * 16, 4);
      rows.End(1);

      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();

      Assert.That(run.Counts.SkippedFrameIndices, Is.EqualTo(1));
      Assert.That(run.Warnings, Has.None.Contains("slower than the display's refresh rate"));
    }

    [Test]
    public void OnlyFramesBetweenStartAndEndAreMeasured()
    {
      var rows = new RowBuilder();
      rows.Show(1, 0, 4, runId: 7).Show(2, 16, 4, runId: 7); // before the run
      rows.Start(1).Show(10, 100, 4).Show(11, 116, 4).End(1);
      rows.Show(12, 132, 4); // after the run

      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();

      Assert.That(run.RunId, Is.EqualTo(1));
      Assert.That(run.Frames.Select(f => f.FrameIndex), Is.EqualTo(new ulong[] { 10, 11 }));
    }

    [Test]
    public void SeveralRuns_AreReportedSeparately_AndCanBeSelected()
    {
      var rows = new RowBuilder();
      rows.Start(1, name: "first").Show(10, 0, 4).Show(11, 16, 4).End(1);
      rows.Start(2, name: "second").Show(50, 0, 4, runId: 2).Show(51, 16, 4, runId: 2).Show(52, 32, 4, runId: 2).End(2);

      var all = TimelineAnalyzer.Analyze(rows.Rows);
      Assert.That(all.Runs.Select(r => r.SequenceId), Is.EqualTo(new[] { "first", "second" }));
      Assert.That(all.Runs[1].Counts.PresentedFrames, Is.EqualTo(3));

      var selected = TimelineAnalyzer.Analyze(rows.Rows, new TimelineOptions { RunId = 2 });
      Assert.That(selected.Runs.Single().RunId, Is.EqualTo(2));
    }

    [Test]
    public void MissingEndMarker_IsAWarning()
    {
      var rows = new RowBuilder().Start(1).Show(10, 0, 4).Show(11, 16, 4);
      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();
      Assert.That(run.HasEndMarker, Is.False);
      Assert.That(run.Warnings, Has.Some.Contains("no end marker"));
      Assert.That(run.Counts.PresentedFrames, Is.EqualTo(2));
    }

    [Test]
    public void WithoutStartMarkers_TheWholeCaptureIsOneRun()
    {
      var rows = new RowBuilder().Show(1, 0, 4, runId: 0).Show(2, 16, 4, runId: 0).Show(3, 32, 4, runId: 0);
      var result = TimelineAnalyzer.Analyze(rows.Rows);
      Assert.That(result.Warnings, Has.Some.Contains("No start markers"));
      Assert.That(result.Runs.Single().Counts.PresentedFrames, Is.EqualTo(3));
    }

    [Test]
    public void FrameIndexRestart_StartsANewSegment()
    {
      var rows = new RowBuilder().Start(1).Show(5000, 0, 4).Show(5001, 16, 4).Show(1, 0, 4).Show(2, 16, 4).End(1);
      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();

      Assert.That(run.Counts.Segments, Is.EqualTo(2));
      Assert.That(run.Frames[2].Segment, Is.EqualTo(1));
      Assert.That(run.Frames[2].AnimationError?.Nanoseconds, Is.Null, "the first frame of a segment has no predecessor");
      Assert.That(run.Frames[3].AnimationError?.Nanoseconds, Is.EqualTo(0));
      Assert.That(run.Warnings, Has.Some.Contains("jumped back"));
    }

    [Test]
    public void SmallBackwardsStep_IsOutOfOrder_NotANewFrame()
    {
      var rows = new RowBuilder().Start(1).Show(10, 0, 4).Show(11, 16, 3).Show(10, 0, 1).Show(12, 32, 4).End(1);
      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();
      Assert.That(run.Counts.OutOfOrderCaptures, Is.EqualTo(1));
      Assert.That(run.Frames.Select(f => f.FrameIndex), Is.EqualTo(new ulong[] { 10, 11, 12 }));
    }

    [Test]
    public void UndecodableAndNotRecordedCaptures_AreCounted_AndFlagTheNextFrame()
    {
      var rows = new RowBuilder().Start(1).Show(10, 0, 4).Status(CaptureStatus.Undecodable).Status(CaptureStatus.NotRecorded).Show(11, 16, 2);
      rows.Status(CaptureStatus.Torn).Show(12, 32, 4).End(1);

      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();

      Assert.That(run.Counts.Undecodable, Is.EqualTo(1));
      Assert.That(run.Counts.NotRecorded, Is.EqualTo(1));
      Assert.That(run.Counts.Torn, Is.EqualTo(1));
      Assert.That(run.Frames[1].Flags.HasFlag(PresentedFrameFlags.UncertainStart));
      Assert.That(run.Frames[2].Flags.HasFlag(PresentedFrameFlags.UncertainStart));
    }

    [Test]
    public void Statistics_Percentiles()
    {
      var stats = Statistics.From(Enumerable.Range(1, 100).Select(i => (double)i));
      Assert.That(stats.Count, Is.EqualTo(100));
      Assert.That(stats.Min, Is.EqualTo(1));
      Assert.That(stats.Max, Is.EqualTo(100));
      Assert.That(stats.Mean, Is.EqualTo(50.5));
      Assert.That(stats.P50, Is.EqualTo(50.5));
      Assert.That(stats.P95, Is.EqualTo(95.05).Within(1e-9));
      Assert.That(Statistics.From(Array.Empty<double>()), Is.EqualTo(Statistics.Empty));
    }
  }
}
