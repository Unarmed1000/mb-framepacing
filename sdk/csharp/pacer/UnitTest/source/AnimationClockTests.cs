//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The animation clock: with the pacer it steps by the intended refreshes and catches up after a late frame; measured, it rounds wake-ups to whole
//* refreshes; a pause holds it; the longest step is limited; an hour of steps does not drift. The same cases as the C++ tests.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using NUnit.Framework;
using static MB.FramePacing.Pacer.UnitTest.TestPacing;

namespace MB.FramePacing.Pacer.UnitTest
{
  [TestFixture]
  public class AnimationClockTests
  {
    [Test]
    public void WithThePacer_ItStepsByTheIntendedRefreshes_AndCatchesUpAfterALateFrame()
    {
      var pacer = new FramePacer(Settings());
      var clock = new AnimationClock(Hz60, 1_000);
      long start = Second;
      FrameSchedule first = pacer.BeginFrame(new FrameInput(start));
      Assert.That(clock.Advance(first).AnimationTicks, Is.EqualTo(1_000));
      Assert.That(clock.Current.StepRefreshes, Is.Zero);
      // The first frame is presented late: shown a refresh after its target, showing a moment already past
      pacer.EndFrame(new FrameEnd(first.IntendedDisplayTicks + Ms));
      FrameSchedule second = pacer.BeginFrame(new FrameInput(first.IntendedDisplayTicks + Hz60.TicksFor(1)));
      AnimationTime caughtUp = clock.Advance(second);
      // The second frame is aimed two refreshes after the first's target: its step catches up exactly
      Assert.That(caughtUp.StepRefreshes, Is.EqualTo(2));
      Assert.That(caughtUp.AnimationTicks, Is.EqualTo(1_000 + Hz60.TicksFor(2)));
      Assert.That(caughtUp.AnimationTicks - 1_000, Is.EqualTo(second.IntendedDisplayTicks - first.IntendedDisplayTicks));
    }

    [Test]
    public void MeasuredWakeUps_AreRoundedToWholeRefreshes()
    {
      var clock = new AnimationClock(Hz60);
      long wakeUp = Second;
      clock.AdvanceMeasured(wakeUp, 1);
      // Wake-ups after the vsync by anything under half a refresh still step one refresh each (the naive timer's jitter goes away)
      foreach (long late in new long[] { 30_000, 0, 75_000, 10_000, 60_000 })
      {
        wakeUp += 166_667;
        Assert.That(clock.AdvanceMeasured(wakeUp + late, 1).StepRefreshes, Is.EqualTo(1), $"late {late}");
      }
      wakeUp += 60_000;
      // A missed vsync shows as two refreshes, and the step catches up
      wakeUp += 2 * 166_667;
      Assert.That(clock.AdvanceMeasured(wakeUp, 1).StepRefreshes, Is.EqualTo(2));
      // At half rate a frame never steps less than the swap interval
      wakeUp += 166_667;
      Assert.That(clock.AdvanceMeasured(wakeUp, 2).StepRefreshes, Is.EqualTo(2));
    }

    [Test]
    public void APause_HoldsTheAnimation_AndResumeGoesOnWithoutAJump()
    {
      var clock = new AnimationClock(Hz60);
      long now = Second;
      clock.AdvanceMeasured(now, 1);
      now += 166_667;
      long beforePause = clock.AdvanceMeasured(now, 1).AnimationTicks;
      clock.Pause();
      Assert.That(clock.IsPaused, Is.True);
      for (int frame = 0; frame < 30; ++frame)
      {
        now += 166_667;
        Assert.That(clock.AdvanceMeasured(now, 1).AnimationTicks, Is.EqualTo(beforePause));
      }
      clock.Resume();
      now += 166_667;
      AnimationTime resumed = clock.AdvanceMeasured(now, 1);
      Assert.That(resumed.StepRefreshes, Is.EqualTo(1));
      Assert.That(resumed.AnimationTicks, Is.EqualTo(beforePause + Hz60.TicksFor(2) - Hz60.TicksFor(1)));
    }

    [Test]
    public void TheLongestStep_IsLimited()
    {
      var clock = new AnimationClock(Hz60, 0, 4);
      clock.AdvanceMeasured(Second, 1);
      // Suspended for 10 s: the animation steps 4 refreshes, not 600
      AnimationTime resumed = clock.AdvanceMeasured(11 * Second, 1);
      Assert.That(resumed.StepRefreshes, Is.EqualTo(4));
      Assert.That(resumed.AnimationTicks, Is.EqualTo(Hz60.TicksFor(4)));
      // A negative limit is no limit, as 0 is (the C++ library asserts it)
      var unlimited = new AnimationClock(Hz60, 0, -3);
      unlimited.AdvanceMeasured(Second, 1);
      Assert.That(unlimited.AdvanceMeasured(11 * Second, 1).StepRefreshes, Is.EqualTo(600));
    }

    [Test]
    public void AnHourOfSteps_DoesNotDrift()
    {
      var clock = new AnimationClock(Hz60);
      long now = Second;
      // Every frame at swap interval 2 (a first frame at 1 would make the second frame's two refreshes one late, and the clock catch it up)
      clock.AdvanceMeasured(now, 2);
      for (long frame = 0; frame < 108_000; ++frame)
      {
        now += Hz60.TicksFor(frame + 1) - Hz60.TicksFor(frame);
        now += Hz60.TicksFor(frame + 1) - Hz60.TicksFor(frame);
        clock.AdvanceMeasured(now, 2);
      }
      // 216 000 refreshes: an hour exactly, the steps rounded to ticks never added up
      Assert.That(clock.Current.AnimationTicks, Is.EqualTo(3_600 * Second));
    }
  }
}
