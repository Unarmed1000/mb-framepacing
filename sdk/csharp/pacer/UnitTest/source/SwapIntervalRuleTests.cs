//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The swap interval rule's decisions at their edges: Swappy's full window, the late count fix, the share's rounding, speeding up, the slowest
//* frame time, the 500 ns remainder, the preferred interval, a fixed interval and what the window reports. The same cases as the C++ tests.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using NUnit.Framework;
using static MB.FramePacing.Pacer.UnitTest.TestPacing;

namespace MB.FramePacing.Pacer.UnitTest
{
  [TestFixture]
  public class SwapIntervalRuleTests
  {
    [Test]
    public void SwappysRule_WaitsForAFullWindow()
    {
      var rule = new SwapIntervalRule(Settings(SlowDownRule.FullWindow));
      long refresh = 0;
      // Every frame late, but 2 s of frames are needed before the rule decides anything: 120 frames span 119 refreshes, not more than 2 s
      Assert.That(Feed(rule, ref refresh, 121, 20 * Ms, true), Is.EqualTo(SwapIntervalChange.None));
      Assert.That(rule.SwapInterval, Is.EqualTo(1u));
      Assert.That(rule.Window.Full, Is.False);
      // The 122nd frame makes the window span more than 2 s: all late, so slower, to the interval the average (20 + 1 ms) needs: 2
      Assert.That(Feed(rule, ref refresh, 1, 20 * Ms, true), Is.EqualTo(SwapIntervalChange.Slower));
      Assert.That(rule.SwapInterval, Is.EqualTo(2u));
      // Every change restarts the window
      Assert.That(rule.Window.Frames, Is.Zero);
    }

    [Test]
    public void TheLateCountFix_SlowsDownAtAFullWindowsShareOfLateFrames()
    {
      var rule = new SwapIntervalRule(Settings());
      long refresh = 0;
      // A full window at 60 fps holds 120 frames; more than 10 % of that is 13 late frames, however few frames the window holds
      Assert.That(Feed(rule, ref refresh, 12, 20 * Ms, true), Is.EqualTo(SwapIntervalChange.None));
      Assert.That(Feed(rule, ref refresh, 1, 20 * Ms, true), Is.EqualTo(SwapIntervalChange.Slower));
      Assert.That(rule.SwapInterval, Is.EqualTo(2u));
      // After the change only the late frames at the new interval count: a full window at 30 fps holds 60 frames, so 7 late ones
      Assert.That(Feed(rule, ref refresh, 6, 40 * Ms, true, 3), Is.EqualTo(SwapIntervalChange.None));
      Assert.That(Feed(rule, ref refresh, 1, 40 * Ms, true, 3), Is.EqualTo(SwapIntervalChange.Slower));
      Assert.That(rule.SwapInterval, Is.EqualTo(3u));
    }

    [Test]
    public void TheShare_IsRoundedAsTheSimulationRoundsIt()
    {
      // Display times 10 ticks apart and a 1985 tick window: a full window holds exactly 200 frames (off the refresh grid, so the window's capacity
      // is given)
      PacerSettings settings = Settings(SlowDownRule.FullWindow);
      settings.WindowTicks = 1_985;
      settings.WindowCapacity = 256;
      SwapIntervalChange Run(long lateFrames)
      {
        var rule = new SwapIntervalRule(settings);
        var change = SwapIntervalChange.None;
        for (long frame = 0; frame < 200; ++frame)
          change = rule.AddFrame(frame * 10, 5 * Ms, frame < lateFrames, Hz60);
        return change;
      }
      // 21 of 200 is 10.5 %: rounded half to even, 10 %, not more than 10 %
      Assert.That(Run(21), Is.EqualTo(SwapIntervalChange.None));
      // 22 of 200 is 11 %
      Assert.That(Run(22), Is.EqualTo(SwapIntervalChange.Slower));
    }

    [Test]
    public void ItSpeedsUp_OnlyOnAFullWindowWithoutLateFramesAndRoomToSpare()
    {
      var rule = new SwapIntervalRule(Settings());
      rule.Reset(2);
      long refresh = 0;
      // 15 ms: with 1 ms margin and 1 ms to spare, 17 ms does not fit a 16.7 ms refresh
      Assert.That(Feed(rule, ref refresh, 80, 15 * Ms, false, 2), Is.EqualTo(SwapIntervalChange.None));
      Assert.That(rule.SwapInterval, Is.EqualTo(2u));
      // 14 ms: 16 ms fits; the window is full after 2 s of frames at 30 fps (62 frames)
      rule.Clear();
      Assert.That(Feed(rule, ref refresh, 61, 14 * Ms, false, 2), Is.EqualTo(SwapIntervalChange.None));
      Assert.That(Feed(rule, ref refresh, 1, 14 * Ms, false, 2), Is.EqualTo(SwapIntervalChange.Faster));
      Assert.That(rule.SwapInterval, Is.EqualTo(1u));
    }

    [Test]
    public void OneLateFrameInTheWindow_KeepsItSlow()
    {
      var rule = new SwapIntervalRule(Settings());
      rule.Reset(2);
      long refresh = 0;
      Feed(rule, ref refresh, 1, 14 * Ms, true, 2);
      Assert.That(Feed(rule, ref refresh, 61, 14 * Ms, false, 2), Is.EqualTo(SwapIntervalChange.None));
      Assert.That(rule.SwapInterval, Is.EqualTo(2u));
      // Once the late frame leaves the window (it keeps 2 s), it speeds up
      Assert.That(Feed(rule, ref refresh, 2, 14 * Ms, false, 2), Is.EqualTo(SwapIntervalChange.Faster));
    }

    [Test]
    public void ItJumps_ToTheIntervalTheAverageNeeds()
    {
      var rule = new SwapIntervalRule(Settings());
      long refresh = 0;
      // 40 ms + 1 ms margin needs 3 refreshes of 16.7 ms: from 1 straight to 3
      Assert.That(Feed(rule, ref refresh, 13, 40 * Ms, true), Is.EqualTo(SwapIntervalChange.Slower));
      Assert.That(rule.SwapInterval, Is.EqualTo(3u));
    }

    [Test]
    public void ItSlowsDown_NoFurtherThanTheSlowestFrameTime()
    {
      var rule = new SwapIntervalRule(Settings());
      long refresh = 0;
      rule.Reset(3);
      // 50 ms (3 refreshes) is within the slowest (50 ms + 1 ms margin): it may still slow down
      Assert.That(Feed(rule, ref refresh, 7, 70 * Ms, true, 4), Is.EqualTo(SwapIntervalChange.Slower));
      Assert.That(rule.SwapInterval, Is.EqualTo(5u));
      // 83 ms is beyond it: however late, no slower
      Assert.That(Feed(rule, ref refresh, 200, 120 * Ms, true, 8), Is.EqualTo(SwapIntervalChange.None));
      Assert.That(rule.SwapInterval, Is.EqualTo(5u));
    }

    [Test]
    public void ARemainderWithin500Nanoseconds_NeedsNoMoreRefreshes()
    {
      // Two refreshes are 333 333 ticks; the average work plus the 1 ms margin is compared
      static uint NeededAfter(long averageWork)
      {
        var rule = new SwapIntervalRule(Settings());
        long refresh = 0;
        Feed(rule, ref refresh, 13, averageWork, true);
        return rule.SwapInterval;
      }
      Assert.That(NeededAfter(333_333 + 5 - Ms), Is.EqualTo(2u));
      Assert.That(NeededAfter(333_333 + 6 - Ms), Is.EqualTo(3u));
    }

    [Test]
    public void ThePreferredInterval_IsTheFastest()
    {
      PacerSettings settings = Settings();
      settings.PreferredSwapInterval = 2;
      var rule = new SwapIntervalRule(settings);
      Assert.That(rule.SwapInterval, Is.EqualTo(2u));
      long refresh = 0;
      Assert.That(Feed(rule, ref refresh, 200, 2 * Ms, false, 2), Is.EqualTo(SwapIntervalChange.None));
      Assert.That(rule.SwapInterval, Is.EqualTo(2u));
      rule.Reset(1);
      Assert.That(rule.SwapInterval, Is.EqualTo(2u));
    }

    [Test]
    public void WithoutAutoSwapInterval_NothingChanges()
    {
      PacerSettings settings = Settings();
      settings.AutoSwapInterval = false;
      var rule = new SwapIntervalRule(settings);
      long refresh = 0;
      Assert.That(Feed(rule, ref refresh, 300, 40 * Ms, true), Is.EqualTo(SwapIntervalChange.None));
      Assert.That(rule.SwapInterval, Is.EqualTo(1u));
      // The window keeps 2 s of frames and the one before them: 122 at 60 fps
      Assert.That(rule.Window.LateFrames, Is.EqualTo(122u));
    }

    [Test]
    public void TheWindow_ReportsWhatItHolds()
    {
      var rule = new SwapIntervalRule(Settings(SlowDownRule.FullWindow));
      long refresh = 0;
      Feed(rule, ref refresh, 3, 10 * Ms, false);
      Feed(rule, ref refresh, 1, 22 * Ms, true);
      WindowState window = rule.Window;
      Assert.That(window.Frames, Is.EqualTo(4u));
      Assert.That(window.LateFrames, Is.EqualTo(1u));
      Assert.That(window.AverageWorkTicks, Is.EqualTo(13 * Ms));
      Assert.That(window.SpanTicks, Is.EqualTo(Hz60.TicksFor(4) - Hz60.TicksFor(1)));
      Assert.That(window.Full, Is.False);
    }

    // Feeds the rule one frame per refresh gap (display times gapRefreshes apart), each late or not, with workTicks of work
    private static SwapIntervalChange Feed(
      SwapIntervalRule rule,
      ref long displayRefresh,
      long frames,
      long workTicks,
      bool late,
      long gapRefreshes = 1
    )
    {
      var last = SwapIntervalChange.None;
      for (long frame = 0; frame < frames; ++frame)
      {
        displayRefresh += gapRefreshes;
        SwapIntervalChange change = rule.AddFrame(Hz60.TicksFor(displayRefresh), workTicks, late, Hz60);
        if (change != SwapIntervalChange.None)
          last = change;
      }
      return last;
    }
  }
}
