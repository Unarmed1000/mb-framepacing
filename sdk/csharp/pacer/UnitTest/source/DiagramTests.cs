//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The timing diagrams of mb-framepacing-explained (tools/timing_diagrams/generate_diagrams.py, doc/frame-pacing-strategies.md) as tests, as the
//* C++ tests have them: their frames, swap intervals and clock readings, and the display refreshes and animation times they show. A diagram's frame
//* model: 60 Hz, the first frame starts 0.2 refresh into the refresh before it is shown, every other frame when the previous one is shown; a frame
//* is shown at the first refresh after it is done, and no sooner than its swap interval after the previous one. The vsync timer animates it for
//* the previous frame's display plus its swap interval.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Linq;
using NUnit.Framework;
using static MB.FramePacing.Pacer.UnitTest.TestPacing;

namespace MB.FramePacing.Pacer.UnitTest
{
  [TestFixture]
  public class DiagramTests
  {
    // The diagrams' unit: 100 is one refresh
    private const long Period = 100;
    private const long FirstStart = 20;

    // Refresh 0 on the steady clock
    private const long Origin = 10 * Second;

    // The frame sets of generate_diagrams.py (render times in units: 75 is 12.5 ms, 125 is 20.8 ms)
    private static readonly long[] g_busy = { 75, 125, 75, 125, 75, 75, 75, 75 };
    private static readonly long[] g_hitch = { 75, 125, 75, 75, 75, 75, 75 };

    [Test]
    public void Model_MatchesTheDiagramsSlowFrames()
    {
      // slow-frames: a missed vsync still costs one late frame, and the frame after it catches up exactly
      Frame[] frames = Frames("ABCDEF", new long[] { 75, 125, 75, 75, 125, 75 }, new long[] { 1, 1, 1, 1, 1, 1 });
      Times[] times = Simulate(frames);
      Assert.That(times.Select(time => time.Shown), Is.EqualTo(new long[] { 0, 2, 3, 4, 6, 7 }));
      Assert.That(times.Select(time => time.Animation), Is.EqualTo(new long[] { 0, 1, 3, 4, 5, 7 }));
    }

    [Test]
    public void TheVsyncTimer_RemovesTheClocksJitter()
    {
      // vsync-timer: the naive clock's readings are off by 0, 0.2, 2.4, -1, 0.8, -1, 0.6 and 0.2 ms; rounded to refreshes, every step is one
      long[] errorsMicroseconds = { 0, 200, 2_400, -1_000, 800, -1_000, 600, 200 };
      Frame[] frames = Frames("ABCDEFGH", new long[] { 75, 75, 75, 75, 75, 75, 75, 75 }, new long[] { 1, 1, 1, 1, 1, 1, 1, 1 })
        .Select((frame, index) => frame with { TimerErrorTicks = errorsMicroseconds[index] * 10 })
        .ToArray();
      Times[] times = Simulate(frames);
      Assert.That(MeasuredAnimation(frames, times), Is.EqualTo(new long[] { 0, 1, 2, 3, 4, 5, 6, 7 }));
      Assert.That(times.Select(time => time.Shown), Is.EqualTo(new long[] { 0, 1, 2, 3, 4, 5, 6, 7 }));
    }

    [Test]
    public void TheVsyncTimer_CatchesUpAfterAMissedVsync()
    {
      Frame[] frames = Frames("ABCDEF", new long[] { 75, 125, 75, 75, 125, 75 }, new long[] { 1, 1, 1, 1, 1, 1 });
      Assert.That(MeasuredAnimation(frames, Simulate(frames)), Is.EqualTo(new long[] { 0, 1, 3, 4, 5, 7 }));
    }

    [Test]
    public void HalfRate_HoldsEveryFrameForTwoRefreshes()
    {
      // half-rate-even: 25 ms frames at a swap interval of 2
      Frame[] frames = Frames("ABCD", new long[] { 150, 150, 150, 150 }, new long[] { 2, 2, 2, 2 });
      Times[] times = Simulate(frames);
      Assert.That(times.Select(time => time.Shown), Is.EqualTo(new long[] { 0, 2, 4, 6 }));
      Assert.That(MeasuredAnimation(frames, times), Is.EqualTo(new long[] { 0, 2, 4, 6 }));
    }

    [Test]
    public void SwitchingWithoutHysteresis_AnimatesEveryIntervalChangeExactly()
    {
      // switching-naive: four errors of 16.7 ms, all display errors; the animation follows the swap interval at every change (C and E step three
      // refreshes after two were measured, D and F one)
      Frame[] frames = Frames("ABCDEFGH", g_busy, new long[] { 1, 1, 2, 1, 2, 1, 1, 1 });
      Times[] times = Simulate(frames);
      Assert.That(times.Select(time => time.Shown), Is.EqualTo(new long[] { 0, 2, 4, 6, 8, 9, 10, 11 }));
      Assert.That(MeasuredAnimation(frames, times), Is.EqualTo(new long[] { 0, 1, 4, 5, 8, 9, 10, 11 }));
    }

    [Test]
    public void SwitchingWithHysteresis_LeavesOnlyTheFirstSlowFrameLate()
    {
      // switching-hysteresis (back to full rate after three fast frames): only B is late
      Frame[] frames = Frames("ABCDEFGH", g_busy, new long[] { 1, 1, 2, 2, 2, 2, 2, 1 });
      Times[] times = Simulate(frames);
      Assert.That(times.Select(time => time.Shown), Is.EqualTo(new long[] { 0, 2, 4, 6, 8, 10, 12, 13 }));
      Assert.That(MeasuredAnimation(frames, times), Is.EqualTo(new long[] { 0, 1, 4, 6, 8, 10, 12, 13 }));
    }

    [Test]
    public void Recovering_AtHalfRateAndWithPerFrameTargets()
    {
      // recovery-half-rate: C, D and E held for two refreshes
      Frame[] halfRate = Frames("ABCDEFG", g_hitch, new long[] { 1, 1, 2, 2, 2, 1, 1 });
      Times[] halfRateTimes = Simulate(halfRate);
      Assert.That(halfRateTimes.Select(time => time.Shown), Is.EqualTo(new long[] { 0, 2, 4, 6, 8, 9, 10 }));
      Assert.That(MeasuredAnimation(halfRate, halfRateTimes), Is.EqualTo(new long[] { 0, 1, 4, 6, 8, 9, 10 }));
      // recovery-targeting: back at full rate a frame sooner (E steps one refresh, although two were measured)
      Frame[] targeting = Frames("ABCDEFG", g_hitch, new long[] { 1, 1, 2, 2, 1, 1, 1 });
      Times[] targetingTimes = Simulate(targeting);
      Assert.That(targetingTimes.Select(time => time.Shown), Is.EqualTo(new long[] { 0, 2, 4, 6, 7, 8, 9 }));
      Assert.That(MeasuredAnimation(targeting, targetingTimes), Is.EqualTo(new long[] { 0, 1, 4, 6, 7, 8, 9 }));
    }

    [Test]
    public void ThePacer_PlansTheDiagramsFramesAtAFixedSwapInterval()
    {
      // slow-frames and half-rate-even paced by the pacer (its interval fixed): it aims every frame where the diagram's frame is aimed, infers the
      // late frames from their Present, and the animation clock steps by its plan
      var cases = new (Frame[] Frames, long Interval)[]
      {
        (Frames("ABCDEF", new long[] { 75, 125, 75, 75, 125, 75 }, new long[] { 1, 1, 1, 1, 1, 1 }), 1),
        (Frames("ABCD", new long[] { 150, 150, 150, 150 }, new long[] { 2, 2, 2, 2 }), 2),
      };
      foreach ((Frame[] frames, long interval) in cases)
      {
        var pacer = new FramePacer(new PacerSettings(Hz60) { PreferredSwapInterval = (uint)interval, AutoSwapInterval = false });
        var clock = new AnimationClock(Hz60);
        Times[] times = Simulate(frames);
        // The first frame is told the vsync its predecessor was shown at: it starts in that refresh. The pacer's grid starts there, on a whole tick,
        // so its slots are that vsync plus whole refreshes rounded to the tick
        long firstVsync = Ticks(-interval * Period);
        for (int index = 0; index < frames.Length; ++index)
        {
          long start = Ticks(times[index].Start);
          FrameSchedule schedule = pacer.BeginFrame(new FrameInput(start, index == 0 ? firstVsync : 0));
          long work = Ticks(frames[index].Render) - Origin;
          pacer.EndFrame(new FrameEnd(start + work, work));
          AnimationTime animation = clock.Advance(schedule);
          Assert.That(
            schedule.IntendedDisplayTicks,
            Is.EqualTo(firstVsync + Hz60.TicksFor(times[index].Animation + interval)),
            $"frame {frames[index].Name}"
          );
          Assert.That(
            Hz60.NearestRefreshes(animation.AnimationTicks),
            Is.EqualTo(times[index].Animation - times[0].Animation),
            $"frame {frames[index].Name}"
          );
        }
      }
    }

    // A time in units as ticks on the steady clock
    private static long Ticks(long units)
    {
      long scaled = units * Hz60.TicksQ32 / Period;
      return Origin + ((scaled + (RefreshPeriod.OneTickQ32 / 2)) >> 32);
    }

    // a / b rounded up (b > 0)
    private static long CeilDiv(long a, long b) => a >= 0 ? (a + b - 1) / b : -((-a) / b);

    // generate_diagrams.py's simulate (not VRR, no CPU cap): the times of every frame
    private static Times[] Simulate(Frame[] frames)
    {
      var times = new Times[frames.Length];
      long previousShown = -frames[0].SwapInterval * Period;
      for (int index = 0; index < frames.Length; ++index)
      {
        Frame frame = frames[index];
        long start = previousShown + (index == 0 ? FirstStart : 0);
        long end = start + frame.Render;
        long shown = Math.Max(CeilDiv(end, Period), (previousShown / Period) + frame.SwapInterval);
        times[index] = new Times(start, shown, (previousShown / Period) + frame.SwapInterval);
        previousShown = shown * Period;
      }
      return times;
    }

    private static Frame[] Frames(string names, long[] renders, long[] intervals) =>
      names.Select((name, index) => new Frame(name, renders[index], intervals[index], 0)).ToArray();

    // The vsync timer without a pacer: every frame's measured wake-up (its start, or its clock reading) and swap interval
    private static long[] MeasuredAnimation(Frame[] frames, Times[] times)
    {
      var clock = new AnimationClock(Hz60);
      return frames
        .Select(
          (frame, index) =>
            Hz60.NearestRefreshes(clock.AdvanceMeasured(Ticks(times[index].Start) + frame.TimerErrorTicks, frame.SwapInterval).AnimationTicks)
        )
        .ToArray();
    }

    // A diagram's frame: its render time (units), its swap interval, and its clock reading's error (vsync timer diagrams, ticks)
    private sealed record Frame(char Name, long Render, long SwapInterval, long TimerErrorTicks);

    // A diagram frame's times: when it starts, when it is shown (whole refreshes), and the vsync timer's animation time (refreshes)
    private sealed record Times(long Start, long Shown, long Animation);
  }
}
