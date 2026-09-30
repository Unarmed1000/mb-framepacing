//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Pacing frames does not allocate: 10 000 frames of BeginFrame, EndFrame and both animation clocks, with a busy stretch that makes the rule slow
//* down and speed up, reported vsyncs and display times, a pause, a Reset and a refresh period change. The pacers are made (and allocate their
//* window) before counting. The C++ tests' case.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;
using static MB.FramePacing.Pacer.UnitTest.TestPacing;

namespace MB.FramePacing.Pacer.UnitTest
{
  [TestFixture]
  public class AllocationTests
  {
    [Test]
    public void PacingFrames_DoesNotAllocate()
    {
      RunFrames(new Pacers(), 100); // warm up (JIT, static tables)
      var pacers = new Pacers();
      long before = GC.GetAllocatedBytesForCurrentThread();
      long written = RunFrames(pacers, 10_000);
      long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
      Assert.That(allocated, Is.Zero);
      Assert.That(written, Is.Positive, "the calls must actually have produced output");
    }

    private static long RunFrames(Pacers pacers, int frames)
    {
      long written = 0;
      long now = Second;
      for (int frame = 0; frame < frames; ++frame)
      {
        // Calm frames, then a busy stretch (every third frame over a refresh), the rule slowing down and speeding up again
        bool busy = (frame / 600) % 2 == 1;
        long work = busy && frame % 3 == 0 ? 220_000 : 90_000;
        FrameSchedule schedule = pacers.Pacer.BeginFrame(new FrameInput(now, frame % 97 == 0 ? now : 0));
        written += pacers.Pacer.EndFrame(new FrameEnd(now + work, work));
        FrameSchedule other = pacers.FullWindowPacer.BeginFrame(new FrameInput(now, 0, frame % 5 == 0 ? schedule.EarliestPresentTicks : 0));
        written += pacers.FullWindowPacer.EndFrame(new FrameEnd(now + work));
        written += pacers.Clock.Advance(schedule).StepTicks + pacers.MeasuredClock.AdvanceMeasured(now, 1).StepTicks;
        written += pacers.Pacer.Window.Frames + (other.IntendedDisplayTicks % 7);
        if (frame % 2_500 == 1_250)
        {
          pacers.Clock.Pause();
          pacers.Pacer.Reset();
        }
        if (frame % 2_500 == 1_260)
          pacers.Clock.Resume();
        if (frame == 5_000)
          pacers.Pacer.SetRefreshPeriod(RefreshPeriod.FromRate(60_000, 1_001));
        now = Math.Max(schedule.IntendedDisplayTicks, now + work);
      }
      return written;
    }

    // The pacers and clocks, made before counting: the pacers allocate their window here, once
    private sealed class Pacers
    {
      public readonly FramePacer Pacer = new FramePacer(new PacerSettings(Hz60));
      public readonly FramePacer FullWindowPacer = new FramePacer(new PacerSettings(Hz60) { SlowDown = SlowDownRule.FullWindow });
      public readonly AnimationClock Clock = new AnimationClock(Hz60, 0, 30);
      public readonly AnimationClock MeasuredClock = new AnimationClock(Hz60);
    }
  }
}
