//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The pacer's planning: the first frame, an hour on the grid without drift, the inferred and reported display, present latency, vsync and
//* predicted times, late starts, a missing EndFrame, the rule's change in the schedule, Reset, pauses and refresh period changes. The same cases
//* as the C++ tests.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using NUnit.Framework;
using static MB.FramePacing.Pacer.UnitTest.TestPacing;

namespace MB.FramePacing.Pacer.UnitTest
{
  [TestFixture]
  public class FramePacerTests
  {
    [Test]
    public void TheFirstFrame_AimsOneIntervalAfterTheRefreshItStartsIn()
    {
      var pacer = new FramePacer(Settings());
      FrameSchedule schedule = pacer.BeginFrame(new FrameInput(10 * Second));
      Assert.That(schedule.FrameIndex, Is.Zero);
      Assert.That(schedule.IntendedDisplayTicks, Is.EqualTo((10 * Second) + 166_667));
      Assert.That(schedule.EarliestPresentTicks, Is.EqualTo(10 * Second));
      Assert.That(schedule.SwapInterval, Is.EqualTo(1u));
      Assert.That(schedule.TargetFrameTicks, Is.EqualTo(166_667u));
      Assert.That(schedule.PreferredFrameTicks, Is.EqualTo(166_667u));
      Assert.That(schedule.CpuStartTicks, Is.EqualTo(10 * Second));
      Assert.That(schedule.Change, Is.EqualTo(SwapIntervalChange.None));
      Assert.That(pacer.EndFrame(new FrameEnd((10 * Second) + 90_000)), Is.EqualTo(90_000u));
    }

    [Test]
    public void FramesOnTime_FollowTheGridForAnHourWithoutDrift()
    {
      var pacer = new FramePacer(Settings());
      long start = Second;
      long now = start;
      FrameSchedule schedule = default;
      for (long frame = 0; frame < 216_000; ++frame)
      {
        schedule = pacer.BeginFrame(new FrameInput(now));
        pacer.EndFrame(new FrameEnd(now + (8 * Ms)));
        now = schedule.IntendedDisplayTicks;
      }
      Assert.That(schedule.IntendedDisplayTicks, Is.EqualTo(start + Hz60.TicksFor(216_000)));
      Assert.That(schedule.FrameIndex, Is.EqualTo(215_999UL));
    }

    [Test]
    public void ALateFrame_IsInferredFromItsPresent_AndTheNextFrameCatchesUp()
    {
      var pacer = new FramePacer(Settings());
      long start = Second;
      FrameSchedule first = pacer.BeginFrame(new FrameInput(start));
      // Presented after its refresh: shown one refresh later
      pacer.EndFrame(new FrameEnd(first.IntendedDisplayTicks + Ms));
      FrameSchedule second = pacer.BeginFrame(new FrameInput(first.IntendedDisplayTicks + Ms));
      Assert.That(second.IntendedDisplayTicks, Is.EqualTo(start + Hz60.TicksFor(3)));
      Assert.That(pacer.Window.LateFrames, Is.EqualTo(1u));
    }

    [Test]
    public void PresentLatency_MakesAPresentJustBeforeTheRefreshLate()
    {
      PacerSettings settings = Settings();
      settings.PresentLatencyTicks = 2 * Ms;
      var pacer = new FramePacer(settings);
      long start = Second;
      FrameSchedule first = pacer.BeginFrame(new FrameInput(start));
      pacer.EndFrame(new FrameEnd(first.IntendedDisplayTicks - Ms));
      FrameSchedule second = pacer.BeginFrame(new FrameInput(first.IntendedDisplayTicks));
      Assert.That(pacer.Window.LateFrames, Is.EqualTo(1u));
      Assert.That(second.IntendedDisplayTicks, Is.EqualTo(start + Hz60.TicksFor(3)));
    }

    [Test]
    public void AReportedDisplayTime_WinsOverTheInference()
    {
      var pacer = new FramePacer(Settings());
      long start = Second;
      FrameSchedule first = pacer.BeginFrame(new FrameInput(start));
      pacer.EndFrame(new FrameEnd(start + (5 * Ms)));
      // Presented in time, but the platform says it was shown a refresh late, 30 µs after the vsync
      long shown = first.IntendedDisplayTicks + 166_667 + 300;
      FrameSchedule second = pacer.BeginFrame(new FrameInput(shown, previousDisplayTicks: shown));
      Assert.That(pacer.Window.LateFrames, Is.EqualTo(1u));
      // The grid follows the reported display: the next refresh after it
      Assert.That(second.IntendedDisplayTicks, Is.EqualTo(shown + 166_667));
    }

    [Test]
    public void AReportedVsync_PutsTheGridOnTheDisplays()
    {
      var pacer = new FramePacer(Settings());
      long vsync = (5 * Second) + 1_234;
      FrameSchedule schedule = pacer.BeginFrame(new FrameInput(vsync + (3 * Ms), vsync));
      Assert.That(schedule.IntendedDisplayTicks, Is.EqualTo(vsync + 166_667));
      Assert.That(schedule.EarliestPresentTicks, Is.EqualTo(vsync));
    }

    [Test]
    public void APredictedDisplayTime_IsTheEarliestTarget()
    {
      var pacer = new FramePacer(Settings());
      long start = Second;
      long predicted = start + Hz60.TicksFor(3);
      FrameSchedule schedule = pacer.BeginFrame(new FrameInput(start, predictedDisplayTicks: predicted));
      Assert.That(schedule.IntendedDisplayTicks, Is.EqualTo(predicted));
    }

    [Test]
    public void AFrameStartedAfterItsPlannedRefresh_AimsAtTheNextOne()
    {
      var pacer = new FramePacer(Settings());
      long start = Second;
      FrameSchedule first = pacer.BeginFrame(new FrameInput(start));
      pacer.EndFrame(new FrameEnd(start + Ms));
      // The next frame should aim one refresh after the first's display, but it starts two refreshes later
      long late = first.IntendedDisplayTicks + Hz60.TicksFor(2) + 10;
      FrameSchedule second = pacer.BeginFrame(new FrameInput(late));
      Assert.That(second.IntendedDisplayTicks, Is.EqualTo(start + Hz60.TicksFor(4)));
    }

    [Test]
    public void WithoutEndFrame_TheFrameCountsAsPresentedAtTheNextBeginFrame()
    {
      var pacer = new FramePacer(Settings());
      long start = Second;
      FrameSchedule first = pacer.BeginFrame(new FrameInput(start));
      FrameSchedule second = pacer.BeginFrame(new FrameInput(first.IntendedDisplayTicks + Ms));
      Assert.That(pacer.Window.Frames, Is.EqualTo(1u));
      Assert.That(pacer.Window.AverageWorkTicks, Is.EqualTo(Hz60.TicksFor(1) + Ms));
      Assert.That(second.IntendedDisplayTicks, Is.EqualTo(start + Hz60.TicksFor(3)));
    }

    [Test]
    public void TheSchedule_CarriesTheRulesChangeAndTheNewInterval()
    {
      // The simulation's frame model: a frame starts when the previous one is shown, and is shown at its target or the first refresh after it is
      // done
      var pacer = new FramePacer(Settings());
      long start = Second;
      long now = start;
      var changedAt = new List<long>();
      for (long frame = 0; frame < 40; ++frame)
      {
        FrameSchedule schedule = pacer.BeginFrame(new FrameInput(now));
        if (schedule.Change == SwapIntervalChange.Slower)
        {
          changedAt.Add(frame);
          Assert.That(schedule.SwapInterval, Is.EqualTo(2u));
          Assert.That(schedule.TargetFrameTicks, Is.EqualTo(333_333u));
          Assert.That(schedule.PreferredFrameTicks, Is.EqualTo(166_667u));
        }
        // 20 ms frames: every frame at 60 fps is late
        long done = now + (20 * Ms);
        pacer.EndFrame(new FrameEnd(done));
        long target = Hz60.NearestRefreshes(schedule.IntendedDisplayTicks - start);
        long doneRefresh = Hz60.FloorRefreshes(done - start - 1) + 1;
        now = start + Hz60.TicksFor(Math.Max(target, doneRefresh));
      }
      // 13 late frames (the 13th is judged when the 14th begins) slow it down; at 30 fps they fit
      Assert.That(changedAt, Is.EqualTo(new List<long> { 13 }));
    }

    [Test]
    public void Reset_PlansTheNextFrameAsTheFirst()
    {
      var pacer = new FramePacer(Settings());
      long start = Second;
      pacer.BeginFrame(new FrameInput(start));
      pacer.Reset();
      long later = start + (7 * Second) + 123;
      FrameSchedule schedule = pacer.BeginFrame(new FrameInput(later));
      Assert.That(schedule.IntendedDisplayTicks, Is.EqualTo(later + 166_667));
      Assert.That(schedule.FrameIndex, Is.EqualTo(1UL));
      Assert.That(pacer.Window.Frames, Is.Zero);
    }

    [Test]
    public void ALongPause_StartsANewGrid()
    {
      var pacer = new FramePacer(Settings());
      long start = Second;
      FrameSchedule first = pacer.BeginFrame(new FrameInput(start));
      pacer.EndFrame(new FrameEnd(start + Ms));
      long resumed = first.IntendedDisplayTicks + (60 * Second) + 777;
      FrameSchedule next = pacer.BeginFrame(new FrameInput(resumed));
      Assert.That(next.IntendedDisplayTicks, Is.EqualTo(resumed + 166_667));
      Assert.That(pacer.Window.Frames, Is.Zero);
    }

    [Test]
    public void ANewRefreshPeriod_StartsAgainAtThePreferredInterval()
    {
      var pacer = new FramePacer(Settings());
      long start = Second;
      pacer.BeginFrame(new FrameInput(start));
      pacer.SetRefreshPeriod(RefreshPeriod.FromRate(120));
      FrameSchedule schedule = pacer.BeginFrame(new FrameInput(start + (5 * Ms)));
      Assert.That(schedule.IntendedDisplayTicks, Is.EqualTo(start + (5 * Ms) + 83_333));
      Assert.That(schedule.TargetFrameTicks, Is.EqualTo(83_333u));
      Assert.That(pacer.Refresh, Is.EqualTo(RefreshPeriod.FromRate(120)));
    }

    [Test]
    public void ThePeriodItHas_ChangesNothing()
    {
      // One pacer is told its own period every frame, by SetRefreshPeriod and as the platform reports it: it plans as the other one
      var told = new FramePacer(Settings());
      var plain = new FramePacer(Settings());
      long now = Second;
      for (int frame = 0; frame < 20; ++frame)
      {
        told.SetRefreshPeriod(Hz60);
        FrameSchedule schedule = told.BeginFrame(new FrameInput(now, refreshPeriodNanoseconds: Hz60.Nanoseconds));
        Assert.That(schedule.IntendedDisplayTicks, Is.EqualTo(plain.BeginFrame(new FrameInput(now)).IntendedDisplayTicks), $"frame {frame}");
        told.EndFrame(new FrameEnd(now + (4 * Ms)));
        plain.EndFrame(new FrameEnd(now + (4 * Ms)));
        now = schedule.IntendedDisplayTicks;
      }
    }

    [Test]
    public void APeriodThePlatformReports_IsADisplayModeChange()
    {
      var pacer = new FramePacer(Settings());
      long start = Second;
      pacer.BeginFrame(new FrameInput(start));
      pacer.EndFrame(new FrameEnd(start + Ms));
      var input = new FrameInput(start + (5 * Ms), refreshPeriodNanoseconds: 8_333_333);
      FrameSchedule schedule = pacer.BeginFrame(input);
      Assert.That(pacer.Refresh, Is.EqualTo(RefreshPeriod.FromNanoseconds(8_333_333)));
      // Planned as a first frame, on a new grid from now
      Assert.That(schedule.IntendedDisplayTicks, Is.EqualTo(input.NowTicks + 83_333));
      Assert.That(schedule.TargetFrameTicks, Is.EqualTo(83_333u));
    }
  }
}
