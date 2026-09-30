// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer module: the refresh period's exact arithmetic, the swap interval rule's decisions at their edges, the pacer's planning (the
// grid, the inferred and reported display, vsync and predicted times, pauses) and the animation clock (catch-up, pause, no drift).
#include <mb/framepacing/Pacer.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <cstdint>
#include <limits>
#include <vector>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  constexpr int64_t Ms = FP::TicksPerMillisecond;
  constexpr PC::RefreshPeriod Hz60 = PC::RefreshPeriod::FromRate(60);

  PC::PacerSettings Settings(const PC::RefreshPeriod period = Hz60, const PC::SlowDownRule rule = PC::SlowDownRule::LateCount)
  {
    PC::PacerSettings settings(period);
    settings.SetSlowDown(rule);
    return settings;
  }

  //! Feeds the rule one frame per refresh gap (display times gapRefreshes apart), each late or not, with workTicks of work.
  PC::SwapIntervalChange Feed(PC::SwapIntervalRule& rule, int64_t& displayRefresh, const int64_t frames, const int64_t workTicks, const bool late,
                              const int64_t gapRefreshes = 1)
  {
    PC::SwapIntervalChange last = PC::SwapIntervalChange::None;
    for (int64_t frame = 0; frame < frames; ++frame)
    {
      displayRefresh += gapRefreshes;
      const PC::SwapIntervalChange change = rule.AddFrame(Hz60.TicksFor(displayRefresh), workTicks, late, Hz60);
      if (change != PC::SwapIntervalChange::None)
      {
        last = change;
      }
    }
    return last;
  }
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// RefreshPeriod
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(RefreshPeriod, ARationalRateIsExactOverAnHour)
{
  static_assert(Hz60.Ticks() == 166'667);
  // 60 Hz for an hour is 216 000 refreshes of exactly 1/60 s: no drift, where whole ticks would be 72 ms off
  EXPECT_EQ(Hz60.TicksFor(216'000), 3'600 * FP::TicksPerSecond);
  EXPECT_EQ(Hz60.TicksFor(3), 500'000);
  EXPECT_EQ(Hz60.TicksFor(1), 166'667);
  EXPECT_EQ(Hz60.TicksFor(2), 333'333);
  // 59.94 Hz as DXGI states it: 60000 refreshes take 1001 s
  const PC::RefreshPeriod ntsc = PC::RefreshPeriod::FromRate(60'000, 1'001);
  EXPECT_EQ(ntsc.TicksFor(60'000), 1'001 * FP::TicksPerSecond);
  EXPECT_EQ(ntsc.Ticks(), 166'833);
  // wl_output's mHz
  EXPECT_EQ(PC::RefreshPeriod::FromRate(59'940, 1'000).TicksFor(59'940), 1'000 * FP::TicksPerSecond);
}

TEST(RefreshPeriod, OtherUnits)
{
  EXPECT_EQ(PC::RefreshPeriod::FromTicks(166'667).Ticks(), 166'667);
  EXPECT_EQ(PC::RefreshPeriod::FromNanoseconds(16'683'350).TicksFor(2), 333'667);
  EXPECT_EQ(PC::RefreshPeriod::FromNanoseconds(8'333'333).Ticks(), 83'333);
  // In nanoseconds, as platforms report it
  static_assert(Hz60.Nanoseconds() == 16'666'667);
  static_assert(PC::RefreshPeriod::FromNanoseconds(16'683'350).Nanoseconds() == 16'683'350);
  static_assert(PC::RefreshPeriod::FromTicksQ32(Hz60.TicksQ32()) == Hz60);
}

TEST(RefreshPeriod, ItIsAlwaysValid)
{
  constexpr int64_t Min = PC::RefreshPeriod::MinTicksQ32;
  constexpr int64_t Max = PC::RefreshPeriod::MaxTicksQ32;
  // The range's ends: 1 tick (10 MHz) and 1 s (1 Hz)
  static_assert(PC::RefreshPeriod::FromTicks(1).TicksQ32() == Min);
  static_assert(PC::RefreshPeriod::FromRate(10'000'000).TicksQ32() == Min);
  static_assert(PC::RefreshPeriod::FromNanoseconds(100).TicksQ32() == Min);
  static_assert(PC::RefreshPeriod::FromTicks(FP::TicksPerSecond).TicksQ32() == Max);
  static_assert(PC::RefreshPeriod::FromRate(1).TicksQ32() == Max);
  static_assert(PC::RefreshPeriod::FromNanoseconds(1'000'000'000).TicksQ32() == Max);
#ifdef NDEBUG
  // Without asserts a period outside the range is clamped into it
  EXPECT_EQ(PC::RefreshPeriod::FromRate(0).TicksQ32(), Max);
  EXPECT_EQ(PC::RefreshPeriod::FromRate(60, 0).TicksQ32(), Min);
  EXPECT_EQ(PC::RefreshPeriod::FromRate(1, 2).TicksQ32(), Max);
  EXPECT_EQ(PC::RefreshPeriod::FromRate(20'000'000).TicksQ32(), Min);
  EXPECT_EQ(PC::RefreshPeriod::FromTicks(0).TicksQ32(), Min);
  EXPECT_EQ(PC::RefreshPeriod::FromTicks(std::numeric_limits<int64_t>::max()).TicksQ32(), Max);
  EXPECT_EQ(PC::RefreshPeriod::FromNanoseconds(-1).TicksQ32(), Min);
  EXPECT_EQ(PC::RefreshPeriod::FromNanoseconds(50).TicksQ32(), Min);
  EXPECT_EQ(PC::RefreshPeriod::FromNanoseconds(std::numeric_limits<int64_t>::max()).TicksQ32(), Max);
  EXPECT_EQ(PC::RefreshPeriod::FromTicksQ32(0).TicksQ32(), Min);
#elif GTEST_HAS_DEATH_TEST
  EXPECT_DEATH(static_cast<void>(PC::RefreshPeriod::FromRate(0)), "");
  EXPECT_DEATH(static_cast<void>(PC::RefreshPeriod::FromRate(60, 0)), "");
  EXPECT_DEATH(static_cast<void>(PC::RefreshPeriod::FromTicks(0)), "");
  EXPECT_DEATH(static_cast<void>(PC::RefreshPeriod::FromNanoseconds(50)), "");
  EXPECT_DEATH(static_cast<void>(PC::RefreshPeriod::FromTicksQ32(Max + 1)), "");
#else
  GTEST_SKIP() << "asserts are on and death tests are not available";
#endif
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// PacerSettings
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(PacerSettings, TheRefreshIsRequiredAndTheRestAreSwappysDefaults)
{
  constexpr PC::PacerSettings Defaults(Hz60);
  static_assert(Defaults.Refresh() == Hz60);
  static_assert(Defaults.PreferredSwapInterval() == 1u && Defaults.AutoSwapInterval());
  static_assert(Defaults.SlowDown() == PC::SlowDownRule::LateCount);
  static_assert(Defaults.WindowTicks() == 2 * FP::TicksPerSecond && Defaults.SlowDownLatePercent() == 10u);
  static_assert(Defaults.FrameMarginTicks() == Ms && Defaults.SlowestFrameTicks() == 50 * Ms);
  static_assert(Defaults.PresentLatencyTicks() == 0 && Defaults.WindowCapacity() == 0u);
}

TEST(PacerSettings, EveryValueIsKeptInItsRange)
{
  PC::PacerSettings settings(Hz60);
  // The range's ends are kept
  settings.SetPreferredSwapInterval(PC::PacerSettings::MaxSwapInterval);
  settings.SetWindowTicks(1);
  settings.SetSlowDownLatePercent(0);
  settings.SetWindowCapacity(PC::PacerSettings::MinWindowCapacity);
  EXPECT_EQ(settings.PreferredSwapInterval(), PC::PacerSettings::MaxSwapInterval);
  EXPECT_EQ(settings.WindowTicks(), 1);
  EXPECT_EQ(settings.SlowDownLatePercent(), 0u);
  EXPECT_EQ(settings.WindowCapacity(), PC::PacerSettings::MinWindowCapacity);
  settings.SetWindowCapacity(0);
  EXPECT_EQ(settings.WindowCapacity(), 0u);
#ifdef NDEBUG
  // Without asserts a value outside its range is clamped into it
  settings.SetPreferredSwapInterval(0);
  EXPECT_EQ(settings.PreferredSwapInterval(), 1u);
  settings.SetPreferredSwapInterval(PC::PacerSettings::MaxSwapInterval + 1);
  EXPECT_EQ(settings.PreferredSwapInterval(), PC::PacerSettings::MaxSwapInterval);
  settings.SetSlowDown(static_cast<PC::SlowDownRule>(7));
  EXPECT_EQ(settings.SlowDown(), PC::SlowDownRule::LateCount);
  settings.SetWindowTicks(0);
  EXPECT_EQ(settings.WindowTicks(), 1);
  settings.SetWindowTicks(PC::PacerSettings::MaxWindowTicks + 1);
  EXPECT_EQ(settings.WindowTicks(), PC::PacerSettings::MaxWindowTicks);
  settings.SetSlowDownLatePercent(101);
  EXPECT_EQ(settings.SlowDownLatePercent(), 100u);
  settings.SetFrameMarginTicks(-1);
  EXPECT_EQ(settings.FrameMarginTicks(), 0);
  settings.SetSlowestFrameTicks(PC::PacerSettings::MaxSlowestFrameTicks + 1);
  EXPECT_EQ(settings.SlowestFrameTicks(), PC::PacerSettings::MaxSlowestFrameTicks);
  settings.SetPresentLatencyTicks(-5);
  EXPECT_EQ(settings.PresentLatencyTicks(), 0);
  settings.SetWindowCapacity(1);
  EXPECT_EQ(settings.WindowCapacity(), PC::PacerSettings::MinWindowCapacity);
  settings.SetWindowCapacity(PC::PacerSettings::MaxWindowCapacity + 1);
  EXPECT_EQ(settings.WindowCapacity(), PC::PacerSettings::MaxWindowCapacity);
#elif GTEST_HAS_DEATH_TEST
  EXPECT_DEATH(settings.SetPreferredSwapInterval(0), "");
  EXPECT_DEATH(settings.SetSlowDown(static_cast<PC::SlowDownRule>(7)), "");
  EXPECT_DEATH(settings.SetWindowTicks(0), "");
  EXPECT_DEATH(settings.SetSlowDownLatePercent(101), "");
  EXPECT_DEATH(settings.SetFrameMarginTicks(-1), "");
  EXPECT_DEATH(settings.SetSlowestFrameTicks(PC::PacerSettings::MaxSlowestFrameTicks + 1), "");
  EXPECT_DEATH(settings.SetPresentLatencyTicks(-5), "");
  EXPECT_DEATH(settings.SetWindowCapacity(1), "");
#else
  GTEST_SKIP() << "asserts are on and death tests are not available";
#endif
}

TEST(RefreshPeriod, RefreshesInATime)
{
  EXPECT_EQ(Hz60.FloorRefreshes(166'666), 0);
  EXPECT_EQ(Hz60.FloorRefreshes(166'667), 1);
  EXPECT_EQ(Hz60.FloorRefreshes(3'600 * FP::TicksPerSecond), 216'000);
  EXPECT_EQ(Hz60.NearestRefreshes(83'333), 0);
  EXPECT_EQ(Hz60.NearestRefreshes(83'334), 1);
  EXPECT_EQ(Hz60.NearestRefreshes(250'000), 2);    // a tie: the later refresh
  EXPECT_EQ(Hz60.NearestRefreshes(-5), 0);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// SwapIntervalRule
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(SwapIntervalRule, SwappysRuleWaitsForAFullWindow)
{
  PC::SwapIntervalRule rule(Settings(Hz60, PC::SlowDownRule::FullWindow));
  int64_t refresh = 0;
  // Every frame late, but 2 s of frames are needed before the rule decides anything: 120 frames span 119 refreshes, not more than 2 s
  EXPECT_EQ(Feed(rule, refresh, 121, 20 * Ms, true), PC::SwapIntervalChange::None);
  EXPECT_EQ(rule.SwapInterval(), 1u);
  EXPECT_TRUE(rule.Window().Full == false);
  // The 122nd frame makes the window span more than 2 s: all late, so slower, to the interval the average (20 + 1 ms) needs: 2
  EXPECT_EQ(Feed(rule, refresh, 1, 20 * Ms, true), PC::SwapIntervalChange::Slower);
  EXPECT_EQ(rule.SwapInterval(), 2u);
  // Every change restarts the window
  EXPECT_EQ(rule.Window().Frames, 0u);
}

TEST(SwapIntervalRule, TheLateCountFixSlowsDownAtAFullWindowsShareOfLateFrames)
{
  PC::SwapIntervalRule rule(Settings());
  int64_t refresh = 0;
  // A full window at 60 fps holds 120 frames; more than 10 % of that is 13 late frames, however few frames the window holds
  EXPECT_EQ(Feed(rule, refresh, 12, 20 * Ms, true), PC::SwapIntervalChange::None);
  EXPECT_EQ(Feed(rule, refresh, 1, 20 * Ms, true), PC::SwapIntervalChange::Slower);
  EXPECT_EQ(rule.SwapInterval(), 2u);
  // After the change only the late frames at the new interval count: a full window at 30 fps holds 60 frames, so 7 late ones
  EXPECT_EQ(Feed(rule, refresh, 6, 40 * Ms, true, 3), PC::SwapIntervalChange::None);
  EXPECT_EQ(Feed(rule, refresh, 1, 40 * Ms, true, 3), PC::SwapIntervalChange::Slower);
  EXPECT_EQ(rule.SwapInterval(), 3u);
}

TEST(SwapIntervalRule, TheShareIsRoundedAsTheSimulationRoundsIt)
{
  // Display times 10 ticks apart and a 1985 tick window: a full window holds exactly 200 frames (off the refresh grid, so the window's
  // capacity is given)
  PC::PacerSettings settings = Settings(Hz60, PC::SlowDownRule::FullWindow);
  settings.SetWindowTicks(1'985);
  settings.SetWindowCapacity(256);
  const auto run = [&settings](const int64_t lateFrames)
  {
    PC::SwapIntervalRule rule(settings);
    PC::SwapIntervalChange change = PC::SwapIntervalChange::None;
    for (int64_t frame = 0; frame < 200; ++frame)
    {
      change = rule.AddFrame(frame * 10, 5 * Ms, frame < lateFrames, Hz60);
    }
    return change;
  };
  // 21 of 200 is 10.5 %: rounded half to even, 10 %, not more than 10 %
  EXPECT_EQ(run(21), PC::SwapIntervalChange::None);
  // 22 of 200 is 11 %
  EXPECT_EQ(run(22), PC::SwapIntervalChange::Slower);
}

TEST(SwapIntervalRule, ItSpeedsUpOnlyOnAFullWindowWithoutLateFramesAndRoomToSpare)
{
  PC::SwapIntervalRule rule(Settings());
  rule.Reset(2);
  int64_t refresh = 0;
  // 15 ms: with 1 ms margin and 1 ms to spare, 17 ms does not fit a 16.7 ms refresh
  EXPECT_EQ(Feed(rule, refresh, 80, 15 * Ms, false, 2), PC::SwapIntervalChange::None);
  EXPECT_EQ(rule.SwapInterval(), 2u);
  // 14 ms: 16 ms fits; the window is full after 2 s of frames at 30 fps (62 frames)
  rule.Clear();
  EXPECT_EQ(Feed(rule, refresh, 61, 14 * Ms, false, 2), PC::SwapIntervalChange::None);
  EXPECT_EQ(Feed(rule, refresh, 1, 14 * Ms, false, 2), PC::SwapIntervalChange::Faster);
  EXPECT_EQ(rule.SwapInterval(), 1u);
}

TEST(SwapIntervalRule, OneLateFrameInTheWindowKeepsItSlow)
{
  PC::SwapIntervalRule rule(Settings());
  rule.Reset(2);
  int64_t refresh = 0;
  (void)Feed(rule, refresh, 1, 14 * Ms, true, 2);
  EXPECT_EQ(Feed(rule, refresh, 61, 14 * Ms, false, 2), PC::SwapIntervalChange::None);
  EXPECT_EQ(rule.SwapInterval(), 2u);
  // Once the late frame leaves the window (it keeps 2 s), it speeds up
  EXPECT_EQ(Feed(rule, refresh, 2, 14 * Ms, false, 2), PC::SwapIntervalChange::Faster);
}

TEST(SwapIntervalRule, ItJumpsToTheIntervalTheAverageNeeds)
{
  PC::SwapIntervalRule rule(Settings());
  int64_t refresh = 0;
  // 40 ms + 1 ms margin needs 3 refreshes of 16.7 ms: from 1 straight to 3
  EXPECT_EQ(Feed(rule, refresh, 13, 40 * Ms, true), PC::SwapIntervalChange::Slower);
  EXPECT_EQ(rule.SwapInterval(), 3u);
}

TEST(SwapIntervalRule, ItSlowsDownNoFurtherThanTheSlowestFrameTime)
{
  PC::SwapIntervalRule rule(Settings());
  int64_t refresh = 0;
  rule.Reset(3);
  // 50 ms (3 refreshes) is within the slowest (50 ms + 1 ms margin): it may still slow down
  EXPECT_EQ(Feed(rule, refresh, 7, 70 * Ms, true, 4), PC::SwapIntervalChange::Slower);
  EXPECT_EQ(rule.SwapInterval(), 5u);
  // 83 ms is beyond it: however late, no slower
  EXPECT_EQ(Feed(rule, refresh, 200, 120 * Ms, true, 8), PC::SwapIntervalChange::None);
  EXPECT_EQ(rule.SwapInterval(), 5u);
}

TEST(SwapIntervalRule, ARemainderWithin500NanosecondsNeedsNoMoreRefreshes)
{
  // Two refreshes are 333 333 ticks; the average work plus the 1 ms margin is compared
  const auto neededAfter = [](const int64_t averageWork)
  {
    PC::SwapIntervalRule rule(Settings());
    int64_t refresh = 0;
    (void)Feed(rule, refresh, 13, averageWork, true);
    return rule.SwapInterval();
  };
  EXPECT_EQ(neededAfter(333'333 + 5 - Ms), 2u);
  EXPECT_EQ(neededAfter(333'333 + 6 - Ms), 3u);
}

TEST(SwapIntervalRule, ThePreferredIntervalIsTheFastest)
{
  PC::PacerSettings settings = Settings();
  settings.SetPreferredSwapInterval(2);
  PC::SwapIntervalRule rule(settings);
  EXPECT_EQ(rule.SwapInterval(), 2u);
  int64_t refresh = 0;
  EXPECT_EQ(Feed(rule, refresh, 200, 2 * Ms, false, 2), PC::SwapIntervalChange::None);
  EXPECT_EQ(rule.SwapInterval(), 2u);
  rule.Reset(1);
  EXPECT_EQ(rule.SwapInterval(), 2u);
}

TEST(SwapIntervalRule, WithoutAutoSwapIntervalNothingChanges)
{
  PC::PacerSettings settings = Settings();
  settings.SetAutoSwapInterval(false);
  PC::SwapIntervalRule rule(settings);
  int64_t refresh = 0;
  EXPECT_EQ(Feed(rule, refresh, 300, 40 * Ms, true), PC::SwapIntervalChange::None);
  EXPECT_EQ(rule.SwapInterval(), 1u);
  // The window keeps 2 s of frames and the one before them: 122 at 60 fps
  EXPECT_EQ(rule.Window().LateFrames, 122u);
}

TEST(SwapIntervalRule, TheWindowReportsWhatItHolds)
{
  PC::SwapIntervalRule rule(Settings(Hz60, PC::SlowDownRule::FullWindow));
  int64_t refresh = 0;
  (void)Feed(rule, refresh, 3, 10 * Ms, false);
  (void)Feed(rule, refresh, 1, 22 * Ms, true);
  const PC::WindowState window = rule.Window();
  EXPECT_EQ(window.Frames, 4u);
  EXPECT_EQ(window.LateFrames, 1u);
  EXPECT_EQ(window.AverageWorkTicks, 13 * Ms);
  EXPECT_EQ(window.SpanTicks, Hz60.TicksFor(4) - Hz60.TicksFor(1));
  EXPECT_FALSE(window.Full);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// FramePacer
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(FramePacer, TheFirstFrameAimsOneIntervalAfterTheRefreshItStartsIn)
{
  PC::FramePacer pacer(Settings());
  const PC::FrameSchedule schedule = pacer.BeginFrame({10 * FP::TicksPerSecond});
  EXPECT_EQ(schedule.FrameIndex, 0u);
  EXPECT_EQ(schedule.IntendedDisplayTicks, (10 * FP::TicksPerSecond) + 166'667);
  EXPECT_EQ(schedule.EarliestPresentTicks, 10 * FP::TicksPerSecond);
  EXPECT_EQ(schedule.SwapInterval, 1u);
  EXPECT_EQ(schedule.TargetFrameTicks, 166'667u);
  EXPECT_EQ(schedule.PreferredFrameTicks, 166'667u);
  EXPECT_EQ(schedule.CpuStartTicks, 10 * FP::TicksPerSecond);
  EXPECT_EQ(schedule.Change, PC::SwapIntervalChange::None);
  EXPECT_EQ(pacer.EndFrame({(10 * FP::TicksPerSecond) + 90'000}), 90'000u);
}

TEST(FramePacer, FramesOnTimeFollowTheGridForAnHourWithoutDrift)
{
  PC::FramePacer pacer(Settings());
  const int64_t start = FP::TicksPerSecond;
  int64_t now = start;
  PC::FrameSchedule schedule;
  for (int64_t frame = 0; frame < 216'000; ++frame)
  {
    schedule = pacer.BeginFrame({now});
    (void)pacer.EndFrame({now + (8 * Ms)});
    now = schedule.IntendedDisplayTicks;
  }
  EXPECT_EQ(schedule.IntendedDisplayTicks, start + Hz60.TicksFor(216'000));
  EXPECT_EQ(schedule.FrameIndex, 215'999u);
}

TEST(FramePacer, ALateFrameIsInferredFromItsPresentAndTheNextFrameCatchesUp)
{
  PC::FramePacer pacer(Settings());
  const int64_t start = FP::TicksPerSecond;
  const PC::FrameSchedule first = pacer.BeginFrame({start});
  // Presented after its refresh: shown one refresh later
  (void)pacer.EndFrame({first.IntendedDisplayTicks + Ms});
  const PC::FrameSchedule second = pacer.BeginFrame({first.IntendedDisplayTicks + Ms});
  EXPECT_EQ(second.IntendedDisplayTicks, start + Hz60.TicksFor(3));
  EXPECT_EQ(pacer.Window().LateFrames, 1u);
}

TEST(FramePacer, PresentLatencyMakesAPresentJustBeforeTheRefreshLate)
{
  PC::PacerSettings settings = Settings();
  settings.SetPresentLatencyTicks(2 * Ms);
  PC::FramePacer pacer(settings);
  const int64_t start = FP::TicksPerSecond;
  const PC::FrameSchedule first = pacer.BeginFrame({start});
  (void)pacer.EndFrame({first.IntendedDisplayTicks - Ms});
  const PC::FrameSchedule second = pacer.BeginFrame({first.IntendedDisplayTicks});
  EXPECT_EQ(pacer.Window().LateFrames, 1u);
  EXPECT_EQ(second.IntendedDisplayTicks, start + Hz60.TicksFor(3));
}

TEST(FramePacer, AReportedDisplayTimeWinsOverTheInference)
{
  PC::FramePacer pacer(Settings());
  const int64_t start = FP::TicksPerSecond;
  const PC::FrameSchedule first = pacer.BeginFrame({start});
  (void)pacer.EndFrame({start + (5 * Ms)});
  // Presented in time, but the platform says it was shown a refresh late, 30 µs after the vsync
  const int64_t shown = first.IntendedDisplayTicks + 166'667 + 300;
  const PC::FrameSchedule second = pacer.BeginFrame({shown, 0, shown});
  EXPECT_EQ(pacer.Window().LateFrames, 1u);
  // The grid follows the reported display: the next refresh after it
  EXPECT_EQ(second.IntendedDisplayTicks, shown + 166'667);
}

TEST(FramePacer, AReportedVsyncPutsTheGridOnTheDisplays)
{
  PC::FramePacer pacer(Settings());
  const int64_t vsync = (5 * FP::TicksPerSecond) + 1'234;
  const PC::FrameSchedule schedule = pacer.BeginFrame({vsync + (3 * Ms), vsync});
  EXPECT_EQ(schedule.IntendedDisplayTicks, vsync + 166'667);
  EXPECT_EQ(schedule.EarliestPresentTicks, vsync);
}

TEST(FramePacer, APredictedDisplayTimeIsTheEarliestTarget)
{
  PC::FramePacer pacer(Settings());
  const int64_t start = FP::TicksPerSecond;
  const int64_t predicted = start + Hz60.TicksFor(3);
  const PC::FrameSchedule schedule = pacer.BeginFrame({start, 0, 0, predicted});
  EXPECT_EQ(schedule.IntendedDisplayTicks, predicted);
}

TEST(FramePacer, AFrameStartedAfterItsPlannedRefreshAimsAtTheNextOne)
{
  PC::FramePacer pacer(Settings());
  const int64_t start = FP::TicksPerSecond;
  const PC::FrameSchedule first = pacer.BeginFrame({start});
  (void)pacer.EndFrame({start + Ms});
  // The next frame should aim one refresh after the first's display, but it starts two refreshes later
  const int64_t late = first.IntendedDisplayTicks + Hz60.TicksFor(2) + 10;
  const PC::FrameSchedule second = pacer.BeginFrame({late});
  EXPECT_EQ(second.IntendedDisplayTicks, start + Hz60.TicksFor(4));
}

TEST(FramePacer, WithoutEndFrameTheFrameCountsAsPresentedAtTheNextBeginFrame)
{
  PC::FramePacer pacer(Settings());
  const int64_t start = FP::TicksPerSecond;
  const PC::FrameSchedule first = pacer.BeginFrame({start});
  const PC::FrameSchedule second = pacer.BeginFrame({first.IntendedDisplayTicks + Ms});
  EXPECT_EQ(pacer.Window().Frames, 1u);
  EXPECT_EQ(pacer.Window().AverageWorkTicks, Hz60.TicksFor(1) + Ms);
  EXPECT_EQ(second.IntendedDisplayTicks, start + Hz60.TicksFor(3));
}

TEST(FramePacer, TheScheduleCarriesTheRulesChangeAndTheNewInterval)
{
  // The simulation's frame model: a frame starts when the previous one is shown, and is shown at its target or the first refresh after
  // it is done
  PC::FramePacer pacer(Settings());
  const int64_t start = FP::TicksPerSecond;
  int64_t now = start;
  PC::FrameSchedule schedule;
  std::vector<int64_t> changedAt;
  for (int64_t frame = 0; frame < 40; ++frame)
  {
    schedule = pacer.BeginFrame({now});
    if (schedule.Change == PC::SwapIntervalChange::Slower)
    {
      changedAt.push_back(frame);
      EXPECT_EQ(schedule.SwapInterval, 2u);
      EXPECT_EQ(schedule.TargetFrameTicks, 333'333u);
      EXPECT_EQ(schedule.PreferredFrameTicks, 166'667u);
    }
    // 20 ms frames: every frame at 60 fps is late
    const int64_t done = now + (20 * Ms);
    (void)pacer.EndFrame({done});
    const int64_t target = Hz60.NearestRefreshes(schedule.IntendedDisplayTicks - start);
    const int64_t doneRefresh = Hz60.FloorRefreshes(done - start - 1) + 1;
    now = start + Hz60.TicksFor(std::max(target, doneRefresh));
  }
  // 13 late frames (the 13th is judged when the 14th begins) slow it down; at 30 fps they fit
  ASSERT_EQ(changedAt.size(), 1u);
  EXPECT_EQ(changedAt[0], 13);
}

TEST(FramePacer, ResetPlansTheNextFrameAsTheFirst)
{
  PC::FramePacer pacer(Settings());
  const int64_t start = FP::TicksPerSecond;
  (void)pacer.BeginFrame({start});
  pacer.Reset();
  const int64_t later = start + (7 * FP::TicksPerSecond) + 123;
  const PC::FrameSchedule schedule = pacer.BeginFrame({later});
  EXPECT_EQ(schedule.IntendedDisplayTicks, later + 166'667);
  EXPECT_EQ(schedule.FrameIndex, 1u);
  EXPECT_EQ(pacer.Window().Frames, 0u);
}

TEST(FramePacer, ALongPauseStartsANewGrid)
{
  PC::FramePacer pacer(Settings());
  const int64_t start = FP::TicksPerSecond;
  const PC::FrameSchedule first = pacer.BeginFrame({start});
  (void)pacer.EndFrame({start + Ms});
  const int64_t resumed = first.IntendedDisplayTicks + (60 * FP::TicksPerSecond) + 777;
  const PC::FrameSchedule next = pacer.BeginFrame({resumed});
  EXPECT_EQ(next.IntendedDisplayTicks, resumed + 166'667);
  EXPECT_EQ(pacer.Window().Frames, 0u);
}

TEST(FramePacer, ANewRefreshPeriodStartsAgainAtThePreferredInterval)
{
  PC::FramePacer pacer(Settings());
  const int64_t start = FP::TicksPerSecond;
  (void)pacer.BeginFrame({start});
  pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(120));
  const PC::FrameSchedule schedule = pacer.BeginFrame({start + (5 * Ms)});
  EXPECT_EQ(schedule.IntendedDisplayTicks, start + (5 * Ms) + 83'333);
  EXPECT_EQ(schedule.TargetFrameTicks, 83'333u);
  EXPECT_EQ(pacer.Refresh(), PC::RefreshPeriod::FromRate(120));
}

TEST(FramePacer, ThePeriodItHasChangesNothing)
{
  // One pacer is told its own period every frame, by SetRefreshPeriod and as the platform reports it: it plans as the other one
  PC::FramePacer told(Settings());
  PC::FramePacer plain(Settings());
  int64_t now = FP::TicksPerSecond;
  for (int frame = 0; frame < 20; ++frame)
  {
    told.SetRefreshPeriod(Hz60);
    PC::FrameInput input;
    input.NowTicks = now;
    input.RefreshPeriodNanoseconds = Hz60.Nanoseconds();
    const PC::FrameSchedule schedule = told.BeginFrame(input);
    EXPECT_EQ(schedule.IntendedDisplayTicks, plain.BeginFrame({now}).IntendedDisplayTicks) << frame;
    (void)told.EndFrame({now + (4 * Ms)});
    (void)plain.EndFrame({now + (4 * Ms)});
    now = schedule.IntendedDisplayTicks;
  }
}

TEST(FramePacer, APeriodThePlatformReportsIsADisplayModeChange)
{
  PC::FramePacer pacer(Settings());
  const int64_t start = FP::TicksPerSecond;
  (void)pacer.BeginFrame({start});
  (void)pacer.EndFrame({start + Ms});
  PC::FrameInput input;
  input.NowTicks = start + (5 * Ms);
  input.RefreshPeriodNanoseconds = 8'333'333;
  const PC::FrameSchedule schedule = pacer.BeginFrame(input);
  EXPECT_EQ(pacer.Refresh(), PC::RefreshPeriod::FromNanoseconds(8'333'333));
  // Planned as a first frame, on a new grid from now
  EXPECT_EQ(schedule.IntendedDisplayTicks, input.NowTicks + 83'333);
  EXPECT_EQ(schedule.TargetFrameTicks, 83'333u);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// AnimationClock
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(AnimationClock, WithThePacerItStepsByTheIntendedRefreshesAndCatchesUpAfterALateFrame)
{
  PC::FramePacer pacer(Settings());
  PC::AnimationClock clock(Hz60, 1'000);
  const int64_t start = FP::TicksPerSecond;
  const PC::FrameSchedule first = pacer.BeginFrame({start});
  EXPECT_EQ(clock.Advance(first).AnimationTicks, 1'000);
  EXPECT_EQ(clock.Current().StepRefreshes, 0);
  // The first frame is presented late: shown a refresh after its target, showing a moment already past
  (void)pacer.EndFrame({first.IntendedDisplayTicks + Ms});
  const PC::FrameSchedule second = pacer.BeginFrame({first.IntendedDisplayTicks + Hz60.TicksFor(1)});
  const PC::AnimationTime caughtUp = clock.Advance(second);
  // The second frame is aimed two refreshes after the first's target: its step catches up exactly
  EXPECT_EQ(caughtUp.StepRefreshes, 2);
  EXPECT_EQ(caughtUp.AnimationTicks, 1'000 + Hz60.TicksFor(2));
  EXPECT_EQ(caughtUp.AnimationTicks - 1'000, second.IntendedDisplayTicks - first.IntendedDisplayTicks);
}

TEST(AnimationClock, MeasuredWakeUpsAreRoundedToWholeRefreshes)
{
  PC::AnimationClock clock(Hz60);
  int64_t wakeUp = FP::TicksPerSecond;
  (void)clock.AdvanceMeasured(wakeUp, 1);
  // Wake-ups after the vsync by anything under half a refresh still step one refresh each (the naive timer's jitter goes away)
  const std::vector<int64_t> lateness{30'000, 0, 75'000, 10'000, 60'000};
  for (const int64_t late : lateness)
  {
    wakeUp += 166'667;
    EXPECT_EQ(clock.AdvanceMeasured(wakeUp + late, 1).StepRefreshes, 1) << late;
  }
  wakeUp += 60'000;
  // A missed vsync shows as two refreshes, and the step catches up
  wakeUp += 2 * 166'667;
  EXPECT_EQ(clock.AdvanceMeasured(wakeUp, 1).StepRefreshes, 2);
  // At half rate a frame never steps less than the swap interval
  wakeUp += 166'667;
  EXPECT_EQ(clock.AdvanceMeasured(wakeUp, 2).StepRefreshes, 2);
}

TEST(AnimationClock, APauseHoldsTheAnimationAndResumeGoesOnWithoutAJump)
{
  PC::AnimationClock clock(Hz60);
  int64_t now = FP::TicksPerSecond;
  (void)clock.AdvanceMeasured(now, 1);
  now += 166'667;
  const int64_t beforePause = clock.AdvanceMeasured(now, 1).AnimationTicks;
  clock.Pause();
  for (int frame = 0; frame < 30; ++frame)
  {
    now += 166'667;
    EXPECT_EQ(clock.AdvanceMeasured(now, 1).AnimationTicks, beforePause);
  }
  clock.Resume();
  now += 166'667;
  const PC::AnimationTime resumed = clock.AdvanceMeasured(now, 1);
  EXPECT_EQ(resumed.StepRefreshes, 1);
  EXPECT_EQ(resumed.AnimationTicks, beforePause + Hz60.TicksFor(2) - Hz60.TicksFor(1));
}

TEST(AnimationClock, TheLongestStepIsLimited)
{
  PC::AnimationClock clock(Hz60, 0, 4);
  (void)clock.AdvanceMeasured(FP::TicksPerSecond, 1);
  // Suspended for 10 s: the animation steps 4 refreshes, not 600
  const PC::AnimationTime resumed = clock.AdvanceMeasured(11 * FP::TicksPerSecond, 1);
  EXPECT_EQ(resumed.StepRefreshes, 4);
  EXPECT_EQ(resumed.AnimationTicks, Hz60.TicksFor(4));
}

TEST(AnimationClock, AnHourOfStepsDoesNotDrift)
{
  PC::AnimationClock clock(Hz60);
  int64_t now = FP::TicksPerSecond;
  // Every frame at swap interval 2 (a first frame at 1 would make the second frame's two refreshes one late, and the clock catch it up)
  (void)clock.AdvanceMeasured(now, 2);
  for (int64_t frame = 0; frame < 108'000; ++frame)
  {
    now += Hz60.TicksFor(frame + 1) - Hz60.TicksFor(frame);
    now += Hz60.TicksFor(frame + 1) - Hz60.TicksFor(frame);
    (void)clock.AdvanceMeasured(now, 2);
  }
  // 216 000 refreshes: an hour exactly, the steps rounded to ticks never added up
  EXPECT_EQ(clock.Current().AnimationTicks, 3'600 * FP::TicksPerSecond);
}
