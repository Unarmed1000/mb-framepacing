//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Late frames, the late share and the pacing verdict on hand-built rows of a capture card at the display's refresh rate (one capture is one
//* refresh).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MB.FramePacing.MarkerDecoding;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  public class PacingTests
  {
    private const long Ms = NanosecondTimeSpan.NanosecondsPerMillisecond;
    private const long Refresh = 16 * Ms; // 62.5 Hz display, captured at 62.5 fps

    /// <summary>A run of frames: each shown for its number of refreshes, with its animation time step (ms) from the previous frame.</summary>
    private static List<CaptureRow> Rows(IEnumerable<(int Refreshes, long StepMs)> frames, long refresh = Refresh, long preferredNanoseconds = 0) =>
      PacedRows(frames.Select(f => (f.Refreshes, f.StepMs, 0L, 0L)), refresh, preferredNanoseconds);

    /// <summary>
    /// A run of frames with pacing information: each shown for its number of refreshes, with its animation time step (ms), the pacer's
    /// intended display time (ms on its own clock, 0 = none) and target frame time (ns, 0 = none); every frame's preferred frame time
    /// is <paramref name="preferredNanoseconds"/> (0 = none).
    /// </summary>
    private static List<CaptureRow> PacedRows(
      IEnumerable<(int Refreshes, long StepMs, long IntendedMs, long TargetNanoseconds)> frames,
      long refresh = Refresh,
      long preferredNanoseconds = 0
    )
    {
      var rows = new List<CaptureRow>();
      void Add(MarkerPayload payload, StartMetadata? start = null) =>
        rows.Add(new CaptureRow(rows.Count, new NanosecondTickCount(rows.Count * refresh), CaptureStatus.Decoded, payload, start));

      for (int i = 0; i < 3; ++i)
        Add(
          new MarkerPayload(MarkerKind.SequenceStart, 1, 0, MB.FramePacing.Marker.MarkerFlags.NoFlags, new NanosecondTimeSpan(0)),
          StartMetadata.FromTag(0, "pacing")
        );
      ulong index = 100;
      long animationMs = 0;
      foreach (var (refreshes, step, intendedMs, target) in frames)
      {
        animationMs += step;
        for (int c = 0; c < refreshes; ++c)
          Add(
            new MarkerPayload(
              MarkerKind.Frame,
              1,
              index,
              MB.FramePacing.Marker.MarkerFlags.NoFlags,
              new NanosecondTimeSpan(animationMs * Ms),
              PreferredFrameTime: NanosecondTimeDuration.FromNanoseconds(preferredNanoseconds),
              TargetFrameTime: NanosecondTimeDuration.FromNanoseconds(target),
              IntendedDisplayTime: new NanosecondTickCount(intendedMs * Ms)
            )
          );
        ++index;
      }
      for (int i = 0; i < 3; ++i)
        Add(new MarkerPayload(MarkerKind.SequenceEnd, 1, 999_999, MB.FramePacing.Marker.MarkerFlags.NoFlags, new NanosecondTimeSpan(0)));
      return rows;
    }

    private static IEnumerable<(int, long)> Steady(int count, int refreshes = 1) => Enumerable.Repeat((refreshes, refreshes * 16L), count);

    private static RunAnalysis Analyze(List<CaptureRow> rows, double? targetFps = null) =>
      TimelineAnalyzer.Analyze(rows, new TimelineOptions { TargetFps = targetFps }).Runs.Single();

    private static bool IsLate(PresentedFrame frame) => frame.Flags.HasFlag(PresentedFrameFlags.Late);

    private static bool IsDeltaTimeJitter(PresentedFrame frame) => frame.Flags.HasFlag(PresentedFrameFlags.DeltaTimeJitter);

    [Test]
    public void SteadyFullRate_HasNoLateFrames()
    {
      var run = Analyze(Rows(Steady(60)));

      var pacing = run.Pacing!;
      Assert.That(pacing.RefreshPeriod, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(16 * Ms)));
      Assert.That(pacing.RefreshCalculated, Is.False, "a capture card's refresh is its capture period");
      Assert.That(pacing.TargetFrameTime, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(16 * Ms)));
      Assert.That(pacing.Source, Is.EqualTo(PacingSource.NativeRefresh));
      Assert.That(pacing.LateFrames, Is.Zero);
      Assert.That(pacing.Verdict, Is.EqualTo(PacingVerdict.None));
    }

    [Test]
    public void OneMissedRefresh_IsLate_AndCountsAsAnimationError()
    {
      // Frame 11 misses one refresh: it appears 32 ms after frame 10 but its animation advanced only 16 ms
      var frames = Steady(31).ToList();
      frames[9] = (2, 16);
      var run = Analyze(Rows(frames));

      Assert.That(run.Frames[10].DisplayDelta?.Nanoseconds, Is.EqualTo(32 * Ms));
      Assert.That(IsLate(run.Frames[10]));
      Assert.That(run.Frames.Count(IsLate), Is.EqualTo(1));
      Assert.That(run.Statistics.FramesWithAnimationError, Is.EqualTo(1), "an error of one refresh is real at the display's refresh rate");
      Assert.That(run.Pacing!.LateFrames, Is.EqualTo(1));
      Assert.That(run.Pacing.Verdict, Is.EqualTo(PacingVerdict.BadPacing));
    }

    [Test]
    public void CatchUpAfterAHitch_CountsAsBadPacing()
    {
      // A naive delta time: the frame after the late one catches up (32 ms step, shown after one refresh)
      var frames = Steady(11).ToList();
      frames[9] = (2, 16);
      frames.Add((1, 32));
      frames.AddRange(Steady(10));
      var run = Analyze(Rows(frames));

      Assert.That(run.Frames[10].AnimationError?.Nanoseconds, Is.EqualTo(-16 * Ms));
      Assert.That(run.Frames[11].AnimationError?.Nanoseconds, Is.EqualTo(16 * Ms));
      Assert.That(IsLate(run.Frames[11]), Is.False);
      Assert.That(run.Pacing!.ErrorFramesWithUnevenDisplay, Is.EqualTo(2));
      Assert.That(run.Pacing.ErrorFramesWithEvenDisplay, Is.Zero);
    }

    [Test]
    public void UnevenStepsOnAnEvenDisplay_AreDeltaTimeJitter()
    {
      var frames = new List<(int, long)>();
      for (int i = 0; i < 10; ++i)
        frames.AddRange(new[] { (1, 16L), (1, 16L), (1, 32L), (1, 0L) });
      var run = Analyze(Rows(frames));

      // Every frame is on screen for its intended refresh: none is late, however its animation step was off. The timer is off by a whole
      // intended period, which is more than jitter
      Assert.That(run.Pacing!.LateFrames, Is.Zero);
      Assert.That(run.Frames.Count(IsDeltaTimeJitter), Is.Zero, "off by a whole intended period");
      Assert.That(run.Pacing.ErrorFramesWithEvenDisplay, Is.EqualTo(20), "every 32 and 0 ms step");
      Assert.That(run.Pacing.ErrorFramesWithUnevenDisplay, Is.Zero);
      Assert.That(run.Pacing.Verdict, Is.EqualTo(PacingVerdict.DeltaTimeJitter));
    }

    [Test]
    public void HalfRate_WithoutPacingInformation_IsNotLate()
    {
      // Nothing says the game aims for half rate, so the display's refresh rate is the target. Every frame is on screen as long as it was
      // animated for: none is late, whatever the target is guessed to be
      var run = Analyze(Rows(Steady(20, refreshes: 2)));

      Assert.That(run.Pacing!.Source, Is.EqualTo(PacingSource.NativeRefresh));
      Assert.That(run.Pacing.TargetFrameTime, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(16 * Ms)));
      Assert.That(run.Pacing.LateFrames, Is.Zero);
      Assert.That(run.Frames.Skip(1).All(f => f.DisplayDelta > f.PreferredFrameTime), "longer than preferred: the late share's amber");
    }

    [Test]
    public void HalfRate_ThatTheGamePrefers_IsNotLate()
    {
      // The markers say only that the game wants 31.25 fps on this 62.5 Hz display: two refreshes per frame are its target, three are late
      var frames = Steady(20, refreshes: 2).ToList();
      frames[9] = (3, 32);
      var run = Analyze(Rows(frames, preferredNanoseconds: 32 * Ms));

      Assert.That(run.Pacing!.Source, Is.EqualTo(PacingSource.PreferredFrameTime));
      Assert.That(run.Pacing.TargetFrameTime, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(32 * Ms)));
      Assert.That(run.Frames.Count(IsLate), Is.EqualTo(1));
      Assert.That(IsLate(run.Frames[10]));
      Assert.That(run.Frames.Skip(1).All(f => f.PreferredFrameTime?.Nanoseconds == 32 * Ms));
    }

    [Test]
    public void HalfRate_WithAGivenTarget_OnlyTheSlowFrameIsLate()
    {
      // 31.25 fps on 62.5 Hz: two refreshes per frame are the target, three are late
      var frames = Steady(20, refreshes: 2).ToList();
      frames[9] = (3, 32);
      var run = Analyze(Rows(frames), targetFps: 31.25);

      Assert.That(run.Pacing!.TargetFrameTime, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(32 * Ms)));
      Assert.That(run.Frames.Count(IsLate), Is.EqualTo(1));
      Assert.That(IsLate(run.Frames[10]));
    }

    [Test]
    public void MarkerTargetFrameTime_FollowsARateSwitch()
    {
      // An adaptive pacer: 60 fps, then a stretch at 30 fps it chose itself, then 60 again. A frame's target frame time is the interval
      // before it, so the 30 fps targets start one frame after the first frame shown for two refreshes
      const long Full = 16 * Ms;
      const long Half = 32 * Ms;
      var frames = new List<(int, long, long, long)>();
      for (int i = 0; i < 30; ++i)
      {
        bool halfRate = i >= 10 && i < 20;
        bool halfTarget = i >= 11 && i <= 20;
        frames.Add((halfRate ? 2 : 1, halfTarget ? 32L : 16L, 0L, halfTarget ? Half : Full));
      }
      frames[25] = (2, 16L, 0L, Full); // one real miss at 60 fps
      var run = Analyze(PacedRows(frames));

      Assert.That(run.Pacing!.Source, Is.EqualTo(PacingSource.TargetFrameTime));
      Assert.That(run.Frames.Count(IsLate), Is.EqualTo(1), "the chosen 30 fps stretch is not late, the miss is");
      Assert.That(IsLate(run.Frames[26]));
      Assert.That(run.Frames[15].TargetFrameTime?.Nanoseconds, Is.EqualTo(32 * Ms));
    }

    [Test]
    public void Schedule_AFrameThatStaysBehindAfterAHitchIsNotLate()
    {
      // A full frame queue: after frame 10 misses a refresh, every later frame is shown a refresh after its intended time, with even steps.
      // Frame 10 is late; the frames after it stay behind the plan with a step that is right, which their lateness says and no flag
      var frames = new List<(int, long, long, long)>();
      long intended = 1000;
      for (int i = 0; i < 30; ++i, intended += 16)
        frames.Add((i == 9 ? 2 : 1, 16L, intended, 16 * Ms));
      var run = Analyze(PacedRows(frames));

      Assert.That(run.Pacing!.Source, Is.EqualTo(PacingSource.Schedule));
      Assert.That(IsLate(run.Frames[10]));
      Assert.That(run.Frames.Count(IsLate), Is.EqualTo(1), "only the frame whose step was long");
      Assert.That(run.Frames[20].Lateness?.Nanoseconds, Is.EqualTo(16 * Ms), "a refresh behind its intended time");
      Assert.That(run.Frames[20].DisplayDelta?.Nanoseconds, Is.EqualTo(16 * Ms), "the steps are even; only the schedule shows the delay");
      Assert.That(run.Frames[20].AnimationError?.Nanoseconds, Is.Zero);
    }

    [Test]
    public void Schedule_SplitsTheAnimationErrorIntoPacingAndPrediction()
    {
      // The pacer shows every frame on time, but the game animates with a naive delta time: all of the error is prediction error, and no
      // frame is late (each is on screen for its intended period)
      var frames = new List<(int, long, long, long)>();
      long intended = 1000;
      for (int i = 0; i < 20; ++i, intended += 16)
        frames.Add(
          (
            1,
            i % 4 == 2 ? 32L
            : i % 4 == 3 ? 0L
            : 16L,
            intended,
            16 * Ms
          )
        );
      var run = Analyze(PacedRows(frames));

      var errors = run.Frames.Where(f => f.AnimationError?.Nanoseconds is { } e && e != 0).ToList();
      Assert.That(errors, Is.Not.Empty);
      foreach (var frame in errors)
      {
        Assert.That(frame.PacingError?.Nanoseconds, Is.Zero);
        Assert.That(frame.AnimationError?.Nanoseconds, Is.EqualTo(frame.PredictionError?.Nanoseconds - frame.PacingError?.Nanoseconds));
      }
      Assert.That(run.Pacing!.LateFrames, Is.Zero);
      Assert.That(run.Pacing.PredictionErrorMs!.Max, Is.EqualTo(16));
      Assert.That(run.Pacing.Verdict, Is.EqualTo(PacingVerdict.DeltaTimeJitter));
    }

    [Test]
    public void GivenTargetFps_IsTheTarget()
    {
      // A game meant to run at 62.5 fps that only manages half, and animates for the time each frame is shown: slower than its target (every
      // display time step is off it) and not late
      var run = Analyze(Rows(Steady(20, refreshes: 2)), targetFps: 62.5);

      Assert.That(run.Pacing!.Source, Is.EqualTo(PacingSource.GivenTarget));
      Assert.That(run.Pacing.TargetFrameTime, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(16 * Ms)));
      Assert.That(run.Pacing.LateFrames, Is.Zero);
      Assert.That(
        run.Frames.Skip(1).All(f => f.DisplayDelta?.Nanoseconds == 32 * Ms && f.TargetFrameTime?.Nanoseconds == 16 * Ms),
        "all but the first frame, which has no display time step"
      );
    }

    [Test]
    public void TargetBetweenRefreshes_RoundsUp()
    {
      // 60 fps on a 7 ms (about 143 Hz) display alternates 2 and 3 refreshes, each frame animated for the time the frame before it was meant
      // to stay: 3 is the target. Frame 11 stays 4, so frame 12 is shown a refresh later than it was animated for
      var frames = new List<(int, long)>();
      for (int i = 0; i < 20; ++i)
        frames.Add((i % 2 == 0 ? 2 : 3, i == 0 || i % 2 != 0 ? 14L : 21L));
      frames[11] = (4, 14);
      var run = Analyze(Rows(frames, refresh: 7 * Ms), targetFps: 60);

      Assert.That(run.Pacing!.TargetFrameTime, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(21 * Ms)));
      Assert.That(run.Frames.Count(IsLate), Is.EqualTo(1));
      Assert.That(IsLate(run.Frames[12]));
    }

    [Test]
    public void AStepLengthenedWithTheDisplay_IsNotLate()
    {
      // A pacer that knows a frame will stay four refreshes animates the next one for that: on screen long, and as animated for
      var frames = Steady(30).ToList();
      frames[9] = (4, 16);
      frames[10] = (1, 64);
      var run = Analyze(Rows(frames));

      Assert.That(run.Frames[10].DisplayDelta?.Nanoseconds, Is.EqualTo(64 * Ms));
      Assert.That(run.Frames[10].AnimationError?.Nanoseconds, Is.Zero);
      Assert.That(run.Pacing!.LateFrames, Is.Zero);
    }

    [Test]
    public void TwoLostRefreshesInARow_AreTwoLateFrames()
    {
      // Frames 10 and 11 each arrive a refresh after they were animated for, and the animation does not catch up
      var frames = Steady(30).ToList();
      frames[9] = (2, 16);
      frames[10] = (2, 16);
      var run = Analyze(Rows(frames));

      Assert.That(run.Frames.Select((f, i) => (f, i)).Where(x => IsLate(x.f)).Select(x => x.i), Is.EqualTo(new[] { 10, 11 }));
      Assert.That(run.Frames[12].AnimationError?.Nanoseconds, Is.Zero, "the frames after them are behind the clock and not late");
    }

    [Test]
    public void AShortAnimationStepOnAnEvenDisplay_IsDeltaTimeJitter_NotLate()
    {
      // 7 or 8 ms more on screen than animated for at a 16 ms refresh, but on screen for the intended refresh: not late. The timer is off
      // by less than the intended period on an even display: jitter
      foreach (long animation in new[] { 9L, 8L })
      {
        var frames = Steady(20).ToList();
        frames[10] = (1, animation);
        var run = Analyze(Rows(frames));

        Assert.That(run.Pacing!.LateFrames, Is.Zero, $"{animation} ms");
        Assert.That(
          run.Frames.Select((f, i) => (f, i)).Where(x => IsDeltaTimeJitter(x.f)).Select(x => x.i),
          Is.EqualTo(new[] { 10 }),
          $"{animation} ms"
        );
      }
    }

    [Test]
    public void ALateFrame_HasNoDeltaTimeJitter()
    {
      // Frame 10 stays a refresh longer: frame 11 is on screen half a refresh or more later than animated for and than intended. Late, and
      // not jitter
      var frames = Steady(20).ToList();
      frames[10] = (2, 16);
      var run = Analyze(Rows(frames));

      Assert.That(run.Frames.Select((f, i) => (f, i)).Where(x => IsLate(x.f)).Select(x => x.i), Is.EqualTo(new[] { 11 }));
      Assert.That(run.Frames.Count(IsDeltaTimeJitter), Is.Zero);
    }

    [Test]
    public void CaptureCard_ExpectedDisplayRate_IsComparedWithTheCaptureRate()
    {
      var rows = Rows(Steady(20));
      RunAnalysis Expecting(double hz) => TimelineAnalyzer.Analyze(rows, new TimelineOptions { ExpectedRefreshHz = hz }).Runs.Single();

      var matching = Expecting(62.5);
      Assert.That(matching.Pacing!.MatchesExpectedRefresh, Is.True);
      Assert.That(matching.Warnings, Has.None.Contains("was expected"));

      var wrong = Expecting(120);
      Assert.That(wrong.Pacing!.MatchesExpectedRefresh, Is.False);
      Assert.That(
        wrong.Pacing.RefreshPeriod,
        Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(16 * Ms)),
        "a capture card's refresh stays the capture period"
      );
      Assert.That(wrong.Warnings, Has.Some.Contains("capture runs at 62.5 fps, but a 120 Hz display was expected"));
    }

    /// <summary>The rows with the frame markers' refresh period: <paramref name="periodOf"/> of the frame's index from the run's first.</summary>
    private static List<CaptureRow> WithRefreshPeriods(List<CaptureRow> rows, Func<ulong, long> periodOf) =>
      rows.Select(row =>
          row.Payload.Kind == MarkerKind.Frame
            ? row with
            {
              Payload = row.Payload with { RefreshPeriod = NanosecondTimeDuration.FromNanoseconds(periodOf(row.Payload.FrameIndex - 100)) },
            }
            : row
        )
        .ToList();

    [Test]
    public void CaptureCard_TheMarkersRefreshPeriod_IsComparedWithTheCaptureRate()
    {
      var rows = Rows(Steady(20));

      // Within 1 %: 62.5 Hz, and 62 Hz (0.8 % off); nothing the markers do not say (0)
      foreach (long period in new[] { 16 * Ms, 16_129_032, 0 })
      {
        var matching = TimelineAnalyzer.Analyze(WithRefreshPeriods(rows, _ => period), new TimelineOptions()).Runs.Single();
        Assert.That(matching.Warnings, Has.None.Contains("The markers say"), period.ToString());
        Assert.That(matching.Frames.Select(f => f.MarkerRefreshPeriod.Nanoseconds), Is.All.EqualTo(period));
      }

      // A 144 Hz display: the capture's rate stays the refresh, and the warning gives both
      var wrong = TimelineAnalyzer.Analyze(WithRefreshPeriods(rows, _ => 6_944_444), new TimelineOptions()).Runs.Single();
      Assert.That(
        wrong.Pacing!.RefreshPeriod,
        Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(16 * Ms)),
        "a capture card's refresh stays the capture period"
      );
      Assert.That(
        wrong.Warnings,
        Has.Some.Contains("The markers say the application works with a 144 Hz display (20 frames), but the capture runs at 62.5 fps.")
      );

      // A window that moved to another display: each rate that is not the capture's, the most frames first
      var moved = TimelineAnalyzer
        .Analyze(
          WithRefreshPeriods(
            rows,
            index =>
              index < 5 ? 16 * Ms
              : index < 8 ? 8_333_333
              : 6_944_444
          ),
          new TimelineOptions()
        )
        .Runs.Single();
      Assert.That(
        moved.Warnings,
        Has.Some.Contains("works with a 144 Hz display (12 frames), a 120 Hz display (3 frames), but the capture runs at 62.5 fps")
      );

      // The user's expected rate is its own warning
      var both = TimelineAnalyzer.Analyze(WithRefreshPeriods(rows, _ => 6_944_444), new TimelineOptions { ExpectedRefreshHz = 144 }).Runs.Single();
      Assert.That(both.Warnings, Has.Some.Contains("but a 144 Hz display was expected"));
      Assert.That(both.Warnings, Has.Some.Contains("The markers say the application works with a 144 Hz display"));
    }

    [Test]
    public void LateShare_TellsABusyStretchFromRareSpikes()
    {
      // 10 s at 62.5 Hz; the first second has every other frame late, the rest is clean
      var frames = new List<(int, long)>();
      for (int i = 0; i < 40; ++i)
        frames.Add(i % 2 == 0 ? (1, 16L) : (2, 32L));
      frames.AddRange(Steady(560));
      var run = Analyze(Rows(frames));

      Assert.That(run.Pacing!.LateFrames, Is.EqualTo(20));
      Assert.That(run.Pacing.LateShare, Is.LessThan(0.05));
      Assert.That(run.Pacing.WorstLateShare, Is.GreaterThan(0.1), "the busy stretch stands out");
      var rolling = LateShare.Rolling(run.Frames, LateShare.Window);
      Assert.That(rolling[^1], Is.Zero, "the last 2 s are clean");
    }

    [Test]
    public void LateShare_BeforeTheFirstWholeWindow_IsAShareOfAWholeWindow()
    {
      // Two late frames among the first four, then 5 s of steady frames at 62.5 Hz
      var frames = new List<(int, long)> { (2, 16L), (1, 16L), (2, 16L), (1, 16L) };
      frames.AddRange(Steady(300));
      var run = Analyze(Rows(frames));
      Assert.That(run.Pacing!.LateFrames, Is.EqualTo(2));

      var rolling = LateShare.Rolling(run.Frames, LateShare.Window);
      int firstWindow = LateShare.FirstWindowCount(run.Frames, LateShare.Window);
      Assert.That(firstWindow, Is.GreaterThan(100), "about 2 s of frames");
      int secondLate = run.Frames.ToList().FindLastIndex(IsLate);
      // The time before the run holds no late frame: two late frames among a window of frames, not two among the four seen so far
      Assert.That(rolling[secondLate], Is.EqualTo(2.0 / firstWindow));
      Assert.That(rolling.Max(), Is.EqualTo(2.0 / firstWindow));
      Assert.That(run.Pacing.WorstLateShare, Is.LessThanOrEqualTo(2.0 / firstWindow), "only whole windows count, as before");
    }

    [Test]
    public void LateShare_OfARunShorterThanItsWindow_EndsAtTheWholeRunsShare()
    {
      var frames = new List<(int, long)> { (2, 16L), (1, 16L) };
      frames.AddRange(Steady(40));
      var run = Analyze(Rows(frames));
      var rolling = LateShare.Rolling(run.Frames, LateShare.Window);
      Assert.That(rolling[^1], Is.EqualTo(run.Pacing!.LateShare));
      Assert.That(run.Pacing.WorstLateShare, Is.EqualTo(run.Pacing.LateShare));
    }

    /// <summary>The frames of <paramref name="rows"/> with frame indices skipped before them: frame n has index 100 + Σ steps.</summary>
    private static List<CaptureRow> WithIndexSteps(List<CaptureRow> rows, Func<ulong, ulong> stepBefore)
    {
      var indices = new Dictionary<ulong, ulong>();
      ulong index = 100;
      foreach (ulong frame in rows.Where(r => r.Payload.Kind == MarkerKind.Frame).Select(r => r.Payload.FrameIndex).Distinct())
      {
        if (frame > 100)
          index += stepBefore(frame - 100);
        indices[frame] = index;
      }
      return rows.Select(row =>
          row.Payload.Kind == MarkerKind.Frame ? row with { Payload = row.Payload with { FrameIndex = indices[row.Payload.FrameIndex] } } : row
        )
        .ToList();
    }

    [Test]
    public void RunTarget_IsWhatTheFramesAimedAt_NotTheirDueTimeAfterDroppedFrames()
    {
      // An application that renders three frames per refresh and shows the newest: two indices skipped before every frame, no marker target
      var run = Analyze(WithIndexSteps(Rows(Steady(60)), _ => 3));
      var pacing = run.Pacing!;

      Assert.That(run.Frames.Skip(1).All(f => f.SkippedBefore == 2));
      Assert.That(run.Frames[1].TargetFrameTime!.Value.Nanoseconds, Is.EqualTo(3 * Refresh), "each frame is due its dropped frames' times later");
      Assert.That(pacing.TargetFrameTime.Nanoseconds, Is.EqualTo(Refresh), "the run aimed at one refresh");
      Assert.That(pacing.LateFrames, Is.Zero);
    }

    [TestCase(1, true)]
    [TestCase(20, true)]
    [TestCase(8, false)]
    public void VsyncOff_IsWarnedAbout_WhenNearlyEveryFrameHadFramesDroppedBeforeIt(int consecutiveEvery, bool warned)
    {
      // Two indices skipped before every frame but every n-th (n = 1: before every frame)
      var run = Analyze(WithIndexSteps(Rows(Steady(200)), frame => consecutiveEvery > 1 && frame % (ulong)consecutiveEvery == 0 ? 1UL : 3UL));
      var (share, judged) = VsyncOffCheck.Share(run.Frames);

      Assert.That(judged, Is.EqualTo(199));
      Assert.That(share >= VsyncOffCheck.Threshold, Is.EqualTo(warned), share.ToString(CultureInfo.InvariantCulture));
      Assert.That(run.Warnings.Any(w => w.Contains("Vsync is probably off")), Is.EqualTo(warned));
      if (warned)
        Assert.That(
          VsyncOffCheck.Lines(run.Frames)[0],
          Does.StartWith($"{share * 100:0} % of the frames had frames before them that were never shown")
        );
    }

    [Test]
    public void VsyncOff_SaysNothing_OfAShortRunOrAFullRateOne()
    {
      Assert.That(VsyncOffCheck.Warning(Analyze(WithIndexSteps(Rows(Steady(20)), _ => 3)).Frames), Is.Null, "too few frames to say anything");
      Assert.That(VsyncOffCheck.Warning(Analyze(Rows(Steady(200))).Frames), Is.Null, "every frame shown");
    }
  }
}
