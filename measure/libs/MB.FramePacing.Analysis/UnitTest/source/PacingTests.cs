//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Late frames, the late share and the pacing verdict on hand-built rows of a capture card at the display's refresh rate (one capture is one
//* refresh).
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  public class PacingTests
  {
    private const long Ms = TimeSpan.TicksPerMillisecond;
    private const long Refresh = 16 * Ms; // 62.5 Hz display, captured at 62.5 fps

    /// <summary>A run of frames: each shown for its number of refreshes, with its animation time step (ms) from the previous frame.</summary>
    private static List<CaptureRow> Rows(IEnumerable<(int Refreshes, long StepMs)> frames, long refresh = Refresh) =>
      PacedRows(frames.Select(f => (f.Refreshes, f.StepMs, 0L, 0u)), refresh);

    /// <summary>
    /// A run of frames with pacing information: each shown for its number of refreshes, with its animation time step (ms), the pacer's
    /// intended display time (ms on its own clock, 0 = none) and target frame time (ticks, 0 = none).
    /// </summary>
    private static List<CaptureRow> PacedRows(
      IEnumerable<(int Refreshes, long StepMs, long IntendedMs, uint TargetTicks)> frames,
      long refresh = Refresh
    )
    {
      var rows = new List<CaptureRow>();
      void Add(MarkerPayload payload, StartMetadata? start = null) =>
        rows.Add(new CaptureRow(rows.Count, rows.Count * refresh, CaptureStatus.Decoded, payload, start));

      for (int i = 0; i < 3; ++i)
        Add(new MarkerPayload(0, 0, 1, MarkerKind.SequenceStart), new StartMetadata(0, "pacing"));
      ulong index = 100;
      long animationMs = 0;
      foreach (var (refreshes, step, intendedMs, target) in frames)
      {
        animationMs += step;
        for (int c = 0; c < refreshes; ++c)
          Add(new MarkerPayload(index, animationMs * Ms, 1, MarkerKind.Frame, intendedMs * Ms, target));
        ++index;
      }
      for (int i = 0; i < 3; ++i)
        Add(new MarkerPayload(999_999, 0, 1, MarkerKind.SequenceEnd));
      return rows;
    }

    private static IEnumerable<(int, long)> Steady(int count, int refreshes = 1) => Enumerable.Repeat((refreshes, refreshes * 16L), count);

    private static RunAnalysis Analyze(List<CaptureRow> rows, double? targetFps = null) =>
      TimelineAnalyzer.Analyze(rows, new TimelineOptions { TargetFps = targetFps }).Runs.Single();

    private static bool IsLate(PresentedFrame frame) => frame.Flags.HasFlag(PresentedFrameFlags.Late);

    [Test]
    public void SteadyFullRate_HasNoLateFrames()
    {
      var run = Analyze(Rows(Steady(60)));

      var pacing = run.Pacing!;
      Assert.That(pacing.RefreshPeriodMs, Is.EqualTo(16));
      Assert.That(pacing.RefreshCalculated, Is.False, "a capture card's refresh is its capture period");
      Assert.That(pacing.TargetFrameMs, Is.EqualTo(16));
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

      Assert.That(run.Frames[10].DisplayDeltaTicks, Is.EqualTo(32 * Ms));
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

      Assert.That(run.Frames[10].AnimationErrorTicks, Is.EqualTo(-16 * Ms));
      Assert.That(run.Frames[11].AnimationErrorTicks, Is.EqualTo(16 * Ms));
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

      Assert.That(run.Pacing!.LateFrames, Is.Zero);
      Assert.That(run.Pacing.ErrorFramesWithEvenDisplay, Is.EqualTo(20), "every 32 and 0 ms step");
      Assert.That(run.Pacing.ErrorFramesWithUnevenDisplay, Is.Zero);
      Assert.That(run.Pacing.Verdict, Is.EqualTo(PacingVerdict.DeltaTimeJitter));
    }

    [Test]
    public void HalfRate_WithoutPacingInformation_IsLateAgainstTheNativeRate()
    {
      // Nothing says the game aims for half rate: the display's refresh rate is the target, so every frame is a refresh late
      var run = Analyze(Rows(Steady(20, refreshes: 2)));

      Assert.That(run.Pacing!.Source, Is.EqualTo(PacingSource.NativeRefresh));
      Assert.That(run.Pacing.TargetFrameMs, Is.EqualTo(16));
      Assert.That(run.Pacing.LateFrames, Is.EqualTo(19));
    }

    [Test]
    public void HalfRate_WithAGivenTarget_OnlyTheSlowFrameIsLate()
    {
      // 31.25 fps on 62.5 Hz: two refreshes per frame are the target, three are late
      var frames = Steady(20, refreshes: 2).ToList();
      frames[9] = (3, 32);
      var run = Analyze(Rows(frames), targetFps: 31.25);

      Assert.That(run.Pacing!.TargetFrameMs, Is.EqualTo(32));
      Assert.That(run.Frames.Count(IsLate), Is.EqualTo(1));
      Assert.That(IsLate(run.Frames[10]));
    }

    [Test]
    public void MarkerTargetFrameTime_FollowsARateSwitch()
    {
      // A Swappy-like pacer: 60 fps, then a stretch at 30 fps it chose itself, then 60 again. A frame's target frame time is the interval
      // before it, so the 30 fps targets start one frame after the first frame shown for two refreshes
      const uint Full = 160_000;
      const uint Half = 320_000;
      var frames = new List<(int, long, long, uint)>();
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
      Assert.That(run.Frames[15].TargetTicks, Is.EqualTo(32 * Ms));
    }

    [Test]
    public void Schedule_FindsFramesThatStayLateAfterAHitch()
    {
      // A full frame queue: after frame 10 misses a refresh, every later frame is shown a refresh after its intended time, with even steps
      var frames = new List<(int, long, long, uint)>();
      long intended = 1000;
      for (int i = 0; i < 30; ++i, intended += 16)
        frames.Add((i == 9 ? 2 : 1, 16L, intended, 160_000u));
      var run = Analyze(PacedRows(frames));

      Assert.That(run.Pacing!.Source, Is.EqualTo(PacingSource.Schedule));
      Assert.That(run.Frames.Take(10).Count(IsLate), Is.Zero);
      Assert.That(run.Frames.Skip(10).All(IsLate), Is.True, "every frame after the hitch stays a refresh late");
      Assert.That(run.Frames[20].LatenessTicks, Is.EqualTo(16 * Ms));
      Assert.That(run.Frames[20].DisplayDeltaTicks, Is.EqualTo(16 * Ms), "the steps look even; only the schedule shows the delay");
    }

    [Test]
    public void Schedule_SplitsTheAnimationErrorIntoPacingAndPrediction()
    {
      // The pacer shows every frame on time, but the game animates with a naive delta time: all of the error is prediction error
      var frames = new List<(int, long, long, uint)>();
      long intended = 1000;
      for (int i = 0; i < 20; ++i, intended += 16)
        frames.Add(
          (
            1,
            i % 4 == 2 ? 32L
            : i % 4 == 3 ? 0L
            : 16L,
            intended,
            160_000u
          )
        );
      var run = Analyze(PacedRows(frames));

      var errors = run.Frames.Where(f => f.AnimationErrorTicks is { } e && e != 0).ToList();
      Assert.That(errors, Is.Not.Empty);
      foreach (var frame in errors)
      {
        Assert.That(frame.PacingErrorTicks, Is.Zero);
        Assert.That(frame.AnimationErrorTicks, Is.EqualTo(frame.PredictionErrorTicks - frame.PacingErrorTicks));
      }
      Assert.That(run.Pacing!.LateFrames, Is.Zero);
      Assert.That(run.Pacing.PredictionErrorMs!.Max, Is.EqualTo(16));
      Assert.That(run.Pacing.Verdict, Is.EqualTo(PacingVerdict.DeltaTimeJitter));
    }

    [Test]
    public void GivenTargetFps_IsTheTarget()
    {
      // A game meant to run at 62.5 fps that only manages half: every frame is late
      var run = Analyze(Rows(Steady(20, refreshes: 2)), targetFps: 62.5);

      Assert.That(run.Pacing!.Source, Is.EqualTo(PacingSource.GivenTarget));
      Assert.That(run.Pacing.TargetFrameMs, Is.EqualTo(16));
      Assert.That(run.Pacing.LateFrames, Is.EqualTo(19), "all but the first frame, which has no display time");
    }

    [Test]
    public void TargetBetweenRefreshes_RoundsUp()
    {
      // 60 fps on a 7 ms (about 143 Hz) display alternates 2 and 3 refreshes: 3 is the target, 4 is late
      var frames = new List<(int, long)>();
      for (int i = 0; i < 10; ++i)
        frames.AddRange(new[] { (2, 14L), (3, 21L) });
      frames[11] = (4, 21);
      var run = Analyze(Rows(frames, refresh: 7 * Ms), targetFps: 60);

      Assert.That(run.Pacing!.TargetFrameMs, Is.EqualTo(21));
      Assert.That(run.Frames.Count(IsLate), Is.EqualTo(1));
      Assert.That(IsLate(run.Frames[12]));
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
      Assert.That(wrong.Pacing.RefreshPeriodMs, Is.EqualTo(16), "a capture card's refresh stays the capture period");
      Assert.That(wrong.Warnings, Has.Some.Contains("capture runs at 62.5 fps, but a 120 Hz display was expected"));
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
      var rolling = LateShare.Rolling(run.Frames, LateShare.WindowTicks);
      Assert.That(rolling[^1], Is.Zero, "the last 2 s are clean");
    }
  }
}
