// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer module: the refresh period's exact arithmetic, the settings and the target frame rate, the swap interval rule's decisions at
// their edges, the animation clock (measuring frame starts, catching up, pauses, no drift, a display off its nominal rate) and the pacer
// (planning, late frames, pauses, a change of refresh period).
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/RefreshTime.hpp>
#include <mb/framepacing/pacer/animation/AnimationTime.hpp>
#include <mb/framepacing/pacer/animation/FrameMeasurement.hpp>
#include <mb/framepacing/pacer/animation/PacerAnimationClock.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/rule/SlowDownRule.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalRule.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <limits>
#include <utility>
#include <vector>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  constexpr int64_t Ms = FP::TimeSpan::TicksPerMillisecond;
  constexpr int64_t Second = FP::TimeSpan::TicksPerSecond;
  const PC::RefreshPeriod g_hz60 = PC::RefreshPeriod::FromRate(60);
  const PC::RefreshPeriod g_hz120 = PC::RefreshPeriod::FromRate(120);
  //! 59.94 Hz as DXGI states it
  const PC::RefreshPeriod g_ntsc = PC::RefreshPeriod::FromRate(60'000, 1'001);
  //! The shortest period: 10 kHz, 1000 ticks
  const PC::RefreshPeriod g_khz10 = PC::RefreshPeriod::FromTimeSpan(PC::RefreshPeriod::MinPeriod);

  constexpr FP::TimeSpan Span(const int64_t ticks) noexcept
  {
    return FP::TimeSpan(ticks);
  }

  constexpr FP::TickCount64 At(const int64_t ticks) noexcept
  {
    return FP::TickCount64(ticks);
  }

  PC::PacerSettings Settings(const PC::RefreshPeriod period = g_hz60, const PC::SlowDownRule rule = PC::SlowDownRule::LateCount)
  {
    PC::PacerSettings settings(period);
    settings.SetSlowDown(rule);
    return settings;
  }

  //! Feeds the rule frames shown gapRefreshes apart (60 Hz), each late or not, with workTicks of work.
  PC::SwapIntervalChange Feed(PC::SwapIntervalRule& rule, int64_t& displayRefresh, const int64_t frames, const int64_t workTicks, const bool late,
                              const int64_t gapRefreshes = 1)
  {
    PC::SwapIntervalChange last = PC::SwapIntervalChange::None;
    for (int64_t frame = 0; frame < frames; ++frame)
    {
      displayRefresh += gapRefreshes;
      const PC::SwapIntervalChange change = rule.AddFrame(rule.Refresh().TimeFor(displayRefresh), Span(workTicks), late);
      if (change != PC::SwapIntervalChange::None)
      {
        last = change;
      }
    }
    return last;
  }

  //! The simulation's frame model, at 60 Hz from refresh 0 at start: every frame takes workTicks, starts when the previous one is shown, and
  //! is shown at its target or at the first refresh after it is done. Returns the frames' schedules; rLastStart is when the last one started.
  std::vector<PC::FrameSchedule> RunFrames(PC::FramePacer& pacer, const int64_t start, const int64_t frames, const int64_t workTicks,
                                           int64_t& rLastStart)
  {
    std::vector<PC::FrameSchedule> schedules;
    int64_t now = start;
    for (int64_t frame = 0; frame < frames; ++frame)
    {
      const PC::FrameSchedule schedule = pacer.BeginFrame(At(now));
      schedules.push_back(schedule);
      rLastStart = now;
      const int64_t done = now + workTicks;
      static_cast<void>(pacer.EndFrame(At(done)));
      const int64_t target = g_hz60.NearestRefreshes(schedule.IntendedDisplayTime - At(start));
      const int64_t doneRefresh = g_hz60.FloorRefreshes(Span(done - start - 1)) + 1;
      now = start + g_hz60.TimeFor(std::max(target, doneRefresh)).Ticks();
    }
    return schedules;
  }

  std::vector<PC::FrameSchedule> RunFrames(PC::FramePacer& pacer, const int64_t start, const int64_t frames, const int64_t workTicks)
  {
    int64_t lastStart = 0;
    return RunFrames(pacer, start, frames, workTicks, lastStart);
  }

  //! A small generator for jitter (SplitMix64): the same numbers on every platform
  class Random
  {
    uint64_t m_state;

  public:
    explicit Random(const uint64_t seed) noexcept
      : m_state(seed)
    {
    }

    uint64_t Next() noexcept
    {
      m_state += 0x9E3779B97F4A7C15u;
      uint64_t value = m_state;
      value = (value ^ (value >> 30u)) * 0xBF58476D1CE4E5B9u;
      value = (value ^ (value >> 27u)) * 0x94D049BB133111EBu;
      return value ^ (value >> 31u);
    }

    //! A value from -range to range
    int64_t Within(const int64_t range) noexcept
    {
      return static_cast<int64_t>(Next() % static_cast<uint64_t>((2 * range) + 1)) - range;
    }
  };
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// RefreshPeriod
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(RefreshPeriod, ARationalRateIsExactOverAnHour)
{
  EXPECT_EQ(g_hz60.ToTimeSpan(), Span(166'667));
  // 60 Hz for an hour is 216 000 refreshes of exactly 1/60 s: no drift, where whole ticks would be 72 ms off
  EXPECT_EQ(g_hz60.TimeFor(216'000), Span(3'600 * Second));
  EXPECT_EQ(g_hz60.TimeFor(3), Span(500'000));
  EXPECT_EQ(g_hz60.TimeFor(1), Span(166'667));
  EXPECT_EQ(g_hz60.TimeFor(2), Span(333'333));
  EXPECT_EQ(g_hz60.TimeFor(0), Span(0));
  // 59.94 Hz as DXGI states it: 60000 refreshes take 1001 s
  EXPECT_EQ(g_ntsc.TimeFor(60'000), Span(1'001 * Second));
  EXPECT_EQ(g_ntsc.ToTimeSpan(), Span(166'833));
  // wl_output's mHz
  EXPECT_EQ(PC::RefreshPeriod::FromRate(59'940, 1'000).TimeFor(59'940), Span(1'000 * Second));
}

TEST(RefreshPeriod, OtherUnits)
{
  EXPECT_EQ(PC::RefreshPeriod::FromTimeSpan(Span(166'667)).ToTimeSpan(), Span(166'667));
  EXPECT_EQ(PC::RefreshPeriod::FromTimeSpan(Span(166'667)).TimeFor(216'000), Span(int64_t{166'667} * 216'000));
  // Nanoseconds are finer than a tick, and the fraction is kept: 16 666 650 ns is 166 666.5 ticks
  EXPECT_EQ(PC::RefreshPeriod::FromNanoseconds(16'683'350).TimeFor(2), Span(333'667));
  EXPECT_EQ(PC::RefreshPeriod::FromNanoseconds(8'333'333).ToTimeSpan(), Span(83'333));
  EXPECT_EQ(PC::RefreshPeriod::FromNanoseconds(16'666'650).ToTimeSpan(), Span(166'667));
  EXPECT_EQ(PC::RefreshPeriod::FromNanoseconds(16'666'650).TimeFor(2), Span(333'333));
  EXPECT_EQ(g_hz60, PC::RefreshPeriod::FromRate(120, 2));
  EXPECT_NE(g_hz60, g_hz120);
}

TEST(RefreshPeriod, ItIsAlwaysValid)
{
  const PC::RefreshPeriod shortest = PC::RefreshPeriod::FromTimeSpan(PC::RefreshPeriod::MinPeriod);
  const PC::RefreshPeriod longest = PC::RefreshPeriod::FromTimeSpan(PC::RefreshPeriod::MaxPeriod);
  // The range's ends: 100 µs (10 kHz) and 1 s (1 Hz)
  static_assert(PC::RefreshPeriod::MinPeriod == Span(1'000) && PC::RefreshPeriod::MaxPeriod == Span(Second));
  EXPECT_EQ(shortest.ToTimeSpan(), Span(1'000));
  EXPECT_EQ(longest.ToTimeSpan(), Span(Second));
  EXPECT_EQ(PC::RefreshPeriod::FromRate(10'000), shortest);
  EXPECT_EQ(PC::RefreshPeriod::FromNanoseconds(100'000), shortest);
  EXPECT_EQ(PC::RefreshPeriod::FromRate(1), longest);
  EXPECT_EQ(PC::RefreshPeriod::FromNanoseconds(1'000'000'000), longest);
#ifdef NDEBUG
  // Without asserts a period outside the range is clamped into it
  EXPECT_EQ(PC::RefreshPeriod::FromRate(0), longest);
  EXPECT_EQ(PC::RefreshPeriod::FromRate(60, 0), shortest);
  EXPECT_EQ(PC::RefreshPeriod::FromRate(1, 2), longest);
  EXPECT_EQ(PC::RefreshPeriod::FromRate(20'000), shortest);
  EXPECT_EQ(PC::RefreshPeriod::FromTimeSpan(Span(0)), shortest);
  EXPECT_EQ(PC::RefreshPeriod::FromTimeSpan(FP::TimeSpan::MinValue()), shortest);
  EXPECT_EQ(PC::RefreshPeriod::FromTimeSpan(FP::TimeSpan::MaxValue()), longest);
  EXPECT_EQ(PC::RefreshPeriod::FromNanoseconds(-1), shortest);
  EXPECT_EQ(PC::RefreshPeriod::FromNanoseconds(50'000), shortest);
  EXPECT_EQ(PC::RefreshPeriod::FromNanoseconds(std::numeric_limits<int64_t>::max()), longest);
  // A negative number of refreshes is none
  EXPECT_EQ(g_hz60.TimeFor(-3), Span(0));
#elif GTEST_HAS_DEATH_TEST
  EXPECT_DEATH(static_cast<void>(PC::RefreshPeriod::FromRate(0)), "");
  EXPECT_DEATH(static_cast<void>(PC::RefreshPeriod::FromRate(60, 0)), "");
  EXPECT_DEATH(static_cast<void>(PC::RefreshPeriod::FromTimeSpan(Span(0))), "");
  EXPECT_DEATH(static_cast<void>(PC::RefreshPeriod::FromNanoseconds(50'000)), "");
  EXPECT_DEATH(static_cast<void>(g_hz60.TimeFor(-3)), "");
#else
  GTEST_SKIP() << "asserts are on and death tests are not available";
#endif
}

TEST(RefreshPeriod, RefreshesInATime)
{
  EXPECT_EQ(g_hz60.FloorRefreshes(Span(166'666)), 0);
  EXPECT_EQ(g_hz60.FloorRefreshes(Span(166'667)), 1);
  EXPECT_EQ(g_hz60.FloorRefreshes(Span(0)), 0);
  EXPECT_EQ(g_hz60.FloorRefreshes(Span(-5)), 0);
  // The estimate from the rounded period is corrected both ways: 60 Hz rounds up (too few), 59.94 Hz rounds down (too many)
  EXPECT_EQ(g_hz60.FloorRefreshes(Span(3'600 * Second)), 216'000);
  EXPECT_EQ(g_ntsc.FloorRefreshes(Span(g_ntsc.TimeFor(1'000'000).Ticks() - 1)), 999'999);
  EXPECT_EQ(g_ntsc.FloorRefreshes(g_ntsc.TimeFor(1'000'000)), 1'000'000);
  EXPECT_EQ(g_hz60.NearestRefreshes(Span(83'333)), 0);
  EXPECT_EQ(g_hz60.NearestRefreshes(Span(83'334)), 1);
  EXPECT_EQ(g_hz60.NearestRefreshes(Span(250'000)), 2);    // a tie: the later refresh
  EXPECT_EQ(g_hz60.NearestRefreshes(Span(249'999)), 1);
  EXPECT_EQ(g_hz60.NearestRefreshes(Span(0)), 0);
  EXPECT_EQ(g_hz60.NearestRefreshes(Span(-5)), 0);
  // The fewest refreshes that take at least a time: 2 refreshes are 333 333 ticks
  EXPECT_EQ(g_hz60.RefreshesToFit(Span(333'333)), 2);
  EXPECT_EQ(g_hz60.RefreshesToFit(Span(333'334)), 3);
  EXPECT_EQ(g_hz60.RefreshesToFit(Span(1)), 1);
  EXPECT_EQ(g_hz60.RefreshesToFit(Span(0)), 0);
  EXPECT_EQ(g_hz60.RefreshesToFit(Span(-5)), 0);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// RefreshTime
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(RefreshTime, RefreshesAddUpExactly)
{
  PC::RefreshTime time;
  EXPECT_EQ(time.ToTimeSpan(), Span(0));
  EXPECT_EQ(time, PC::RefreshTime(Span(0)));
  // 60 Hz is 166 666.67 ticks: one refresh rounds up, two round down, three are exact
  time.Add(1, g_hz60);
  EXPECT_EQ(time.ToTimeSpan(), Span(166'667));
  time.Add(1, g_hz60);
  EXPECT_EQ(time.ToTimeSpan(), Span(333'333));
  time.Add(1, g_hz60);
  EXPECT_EQ(time.ToTimeSpan(), Span(500'000));
  EXPECT_NE(time, PC::RefreshTime(Span(500'001)));
  // After leaves the time as it is
  EXPECT_EQ(time.After(2, g_hz60).ToTimeSpan(), Span(833'333));
  EXPECT_EQ(time.After(0, g_hz60), time);
  EXPECT_EQ(time.ToTimeSpan(), Span(500'000));
  // An hour added a refresh at a time is an hour to the tick, from any start
  PC::RefreshTime hour(Span(-7 * Second));
  for (int frame = 0; frame < 216'000; ++frame)
  {
    hour.Add(1, g_hz60);
  }
  EXPECT_EQ(hour.ToTimeSpan(), Span(3'593 * Second));
  // Many refreshes at once
  EXPECT_EQ(PC::RefreshTime().After(216'000, g_hz60).ToTimeSpan(), Span(3'600 * Second));
  EXPECT_EQ(PC::RefreshTime().After(60'000, g_ntsc).ToTimeSpan(), Span(1'001 * Second));
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// PacerSettings
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(PacerSettings, TheRefreshIsRequiredAndTheRestHaveDefaults)
{
  const PC::PacerSettings defaults(g_hz60);
  EXPECT_EQ(defaults.Refresh(), g_hz60);
  EXPECT_EQ(defaults.PreferredFrameTime(), Span(0));
  EXPECT_EQ(defaults.PreferredSwapInterval(), 1u);
  EXPECT_EQ(defaults.PreferredSwapIntervalAt(g_hz60), 1u);
  EXPECT_TRUE(defaults.AutoSwapInterval());
  EXPECT_EQ(defaults.SlowDown(), PC::SlowDownRule::LateCount);
  EXPECT_EQ(defaults.FrameWindowLength(), Span(2 * Second));
  EXPECT_EQ(defaults.SlowDownLatePercent(), 10u);
  EXPECT_EQ(defaults.FrameMargin(), Span(Ms));
  EXPECT_EQ(defaults.SlowestFrameTime(), Span(50 * Ms));
  EXPECT_EQ(defaults, PC::PacerSettings(g_hz60));

  PC::PacerSettings other(g_hz60);
  other.SetRefresh(g_hz120);
  other.SetAutoSwapInterval(false);
  EXPECT_EQ(other.Refresh(), g_hz120);
  EXPECT_FALSE(other.AutoSwapInterval());
  EXPECT_NE(other, defaults);
}

TEST(PacerSettings, EveryValueIsKeptInItsRange)
{
  PC::PacerSettings settings(g_hz60);
  // The range's ends are kept
  settings.SetPreferredSwapInterval(PC::PacerSettings::MaxSwapInterval);
  settings.SetPreferredFrameTime(PC::PacerSettings::MaxPreferredFrameTime);
  settings.SetFrameWindowLength(PC::PacerSettings::MinFrameWindowLength);
  settings.SetSlowDownLatePercent(0);
  settings.SetFrameMargin(PC::PacerSettings::MaxFrameMargin);
  settings.SetSlowestFrameTime(Span(0));
  settings.SetSlowDown(PC::SlowDownRule::FullWindow);
  EXPECT_EQ(settings.PreferredSwapInterval(), PC::PacerSettings::MaxSwapInterval);
  EXPECT_EQ(settings.PreferredFrameTime(), PC::PacerSettings::MaxPreferredFrameTime);
  EXPECT_EQ(settings.FrameWindowLength(), Span(1));
  EXPECT_EQ(settings.SlowDownLatePercent(), 0u);
  EXPECT_EQ(settings.FrameMargin(), Span(Second));
  EXPECT_EQ(settings.SlowestFrameTime(), Span(0));
  EXPECT_EQ(settings.SlowDown(), PC::SlowDownRule::FullWindow);
#ifdef NDEBUG
  // Without asserts a value outside its range is clamped into it
  settings.SetPreferredSwapInterval(0);
  EXPECT_EQ(settings.PreferredSwapInterval(), 1u);
  settings.SetPreferredSwapInterval(PC::PacerSettings::MaxSwapInterval + 1);
  EXPECT_EQ(settings.PreferredSwapInterval(), PC::PacerSettings::MaxSwapInterval);
  settings.SetPreferredFrameTime(Span(-1));
  EXPECT_EQ(settings.PreferredFrameTime(), Span(0));
  settings.SetPreferredFrameTime(Span(11 * Second));
  EXPECT_EQ(settings.PreferredFrameTime(), PC::PacerSettings::MaxPreferredFrameTime);
  settings.SetSlowDown(static_cast<PC::SlowDownRule>(7));
  EXPECT_EQ(settings.SlowDown(), PC::SlowDownRule::LateCount);
  settings.SetFrameWindowLength(Span(0));
  EXPECT_EQ(settings.FrameWindowLength(), Span(1));
  settings.SetFrameWindowLength(Span(61 * Second));
  EXPECT_EQ(settings.FrameWindowLength(), PC::PacerSettings::MaxFrameWindowLength);
  settings.SetSlowDownLatePercent(101);
  EXPECT_EQ(settings.SlowDownLatePercent(), 100u);
  settings.SetFrameMargin(Span(-1));
  EXPECT_EQ(settings.FrameMargin(), Span(0));
  settings.SetFrameMargin(Span(2 * Second));
  EXPECT_EQ(settings.FrameMargin(), PC::PacerSettings::MaxFrameMargin);
  settings.SetSlowestFrameTime(Span(-1));
  EXPECT_EQ(settings.SlowestFrameTime(), Span(0));
  settings.SetSlowestFrameTime(Span(11 * Second));
  EXPECT_EQ(settings.SlowestFrameTime(), PC::PacerSettings::MaxSlowestFrameTime);
#elif GTEST_HAS_DEATH_TEST
  EXPECT_DEATH(settings.SetPreferredSwapInterval(0), "");
  EXPECT_DEATH(settings.SetPreferredFrameTime(Span(-1)), "");
  EXPECT_DEATH(settings.SetSlowDown(static_cast<PC::SlowDownRule>(7)), "");
  EXPECT_DEATH(settings.SetFrameWindowLength(Span(0)), "");
  EXPECT_DEATH(settings.SetSlowDownLatePercent(101), "");
  EXPECT_DEATH(settings.SetFrameMargin(Span(-1)), "");
  EXPECT_DEATH(settings.SetSlowestFrameTime(Span(11 * Second)), "");
#else
  GTEST_SKIP() << "asserts are on and death tests are not available";
#endif
}

TEST(PacerSettings, ATargetFrameRateIsTheSwapIntervalThatGivesItOnTheDisplay)
{
  PC::PacerSettings settings(g_hz60);
  settings.SetPreferredFrameRate(30);
  EXPECT_EQ(settings.PreferredFrameTime(), Span(333'333));
  // 30 fps: every second refresh at 60 Hz, every fourth at 120 Hz
  EXPECT_EQ(settings.PreferredSwapIntervalAt(g_hz60), 2u);
  EXPECT_EQ(settings.PreferredSwapIntervalAt(g_hz120), 4u);
  // 29.97 fps on a 59.94 Hz display, both as rationals
  settings.SetPreferredFrameRate(30'000, 1'001);
  EXPECT_EQ(settings.PreferredFrameTime(), Span(333'667));
  EXPECT_EQ(settings.PreferredSwapIntervalAt(g_ntsc), 2u);

  settings.SetPreferredFrameRate(60);
  // A twentieth of a refresh of slack: 60 fps on a 59.94 Hz display is every refresh (its frame time is 0.1 % short of one), and on a
  // 60.02 Hz one too (0.03 % over)
  EXPECT_EQ(settings.PreferredSwapIntervalAt(g_ntsc), 1u);
  EXPECT_EQ(settings.PreferredSwapIntervalAt(PC::RefreshPeriod::FromRate(60'020, 1'000)), 1u);
  // Never faster than asked: 60 fps on 144 Hz is every third refresh (48 fps), not every second (72 fps)
  EXPECT_EQ(settings.PreferredSwapIntervalAt(PC::RefreshPeriod::FromRate(144)), 3u);
  // A rate above the display's is the display's
  EXPECT_EQ(settings.PreferredSwapIntervalAt(PC::RefreshPeriod::FromRate(30)), 1u);

  // With a preferred swap interval as well, the slower of the two counts
  settings.SetPreferredSwapInterval(2);
  EXPECT_EQ(settings.PreferredSwapIntervalAt(g_hz60), 2u);
  settings.SetPreferredFrameRate(15);
  EXPECT_EQ(settings.PreferredSwapIntervalAt(g_hz60), 4u);
  // No frame time: the swap interval alone
  settings.SetPreferredFrameTime({});
  EXPECT_EQ(settings.PreferredSwapIntervalAt(g_hz60), 2u);
  // At most MaxSwapInterval refreshes: 10 s on a 144 Hz display would be 1440
  settings.SetPreferredFrameTime(PC::PacerSettings::MaxPreferredFrameTime);
  EXPECT_EQ(settings.PreferredSwapIntervalAt(PC::RefreshPeriod::FromRate(144)), PC::PacerSettings::MaxSwapInterval);
  // A rate faster than a tick a frame is a tick
  settings.SetPreferredFrameRate(4'000'000'000u);
  EXPECT_EQ(settings.PreferredFrameTime(), Span(1));
#ifdef NDEBUG
  // Without asserts a rate of nothing is no rate, and one below 0.1 fps the slowest
  settings.SetPreferredFrameRate(0);
  EXPECT_EQ(settings.PreferredFrameTime(), Span(0));
  settings.SetPreferredFrameRate(30, 0);
  EXPECT_EQ(settings.PreferredFrameTime(), Span(0));
  settings.SetPreferredFrameRate(1, 20);
  EXPECT_EQ(settings.PreferredFrameTime(), PC::PacerSettings::MaxPreferredFrameTime);
#elif GTEST_HAS_DEATH_TEST
  EXPECT_DEATH(settings.SetPreferredFrameRate(0), "");
  EXPECT_DEATH(settings.SetPreferredFrameRate(30, 0), "");
  EXPECT_DEATH(settings.SetPreferredFrameRate(1, 20), "");
#endif
}

TEST(PacerSettings, TheTargetFrameRateRoundsAsTheToolsJudgeIt)
{
  // The tools measure every frame against the target frame time in whole refreshes: rounded up, with a twentieth of a refresh of slack,
  // at least one (FrameTimeRounding in the analysis). The pacer must aim where it is judged
  const std::vector<std::pair<uint32_t, uint32_t>> displays{{50, 1},  {60'000, 1'001}, {60, 1},  {75, 1}, {90, 1},
                                                            {120, 1}, {144, 1},        {165, 1}, {240, 1}};
  for (const uint32_t fps : {24u, 25u, 30u, 40u, 48u, 50u, 60u, 72u, 90u, 120u, 144u})
  {
    PC::PacerSettings settings(g_hz60);
    settings.SetPreferredFrameRate(fps);
    for (const auto& [numerator, denominator] : displays)
    {
      const PC::RefreshPeriod display = PC::RefreshPeriod::FromRate(numerator, denominator);
      const double frameTicks = static_cast<double>(Second) / fps;
      const auto expected = static_cast<uint32_t>(std::max(1.0, std::ceil((frameTicks / static_cast<double>(display.ToTimeSpan().Ticks())) - 0.05)));
      EXPECT_EQ(settings.PreferredSwapIntervalAt(display), expected) << fps << " fps on " << numerator << "/" << denominator << " Hz";
    }
  }
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// SwapIntervalRule
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(SwapIntervalRule, TheFullWindowRuleWaitsForAFullWindow)
{
  PC::SwapIntervalRule rule(Settings(g_hz60, PC::SlowDownRule::FullWindow));
  int64_t refresh = 0;
  // Every frame late, but 2 s of frames are needed before the rule decides anything: 120 frames span 119 refreshes, not more than 2 s
  EXPECT_EQ(Feed(rule, refresh, 121, 20 * Ms, true), PC::SwapIntervalChange::None);
  EXPECT_EQ(rule.SwapInterval(), 1u);
  EXPECT_FALSE(rule.FrameWindow().Full);
  // The 122nd frame makes the window span more than 2 s: all late, so slower, to the interval the average (20 + 1 ms) needs: 2
  EXPECT_EQ(Feed(rule, refresh, 1, 20 * Ms, true), PC::SwapIntervalChange::Slower);
  EXPECT_EQ(rule.SwapInterval(), 2u);
  // Every change restarts the window
  EXPECT_EQ(rule.FrameWindow().Frames, 0u);
  EXPECT_FALSE(rule.FrameWindow().Full);
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
  // A 10 kHz display and a window of 198.5 refreshes: a full window holds exactly 200 frames
  PC::PacerSettings settings = Settings(g_khz10, PC::SlowDownRule::FullWindow);
  settings.SetFrameWindowLength(Span(198'500));
  const auto run = [&settings](const int64_t lateFrames)
  {
    PC::SwapIntervalRule rule(settings);
    PC::SwapIntervalChange change = PC::SwapIntervalChange::None;
    for (int64_t frame = 0; frame < 200; ++frame)
    {
      change = rule.AddFrame(g_khz10.TimeFor(frame), Span(5 * Ms), frame < lateFrames);
    }
    return change;
  };
  // 21 of 200 is 10.5 %: rounded half to even, 10 %, not more than 10 %
  EXPECT_EQ(run(21), PC::SwapIntervalChange::None);
  // 22 of 200 is 11 %
  EXPECT_EQ(run(22), PC::SwapIntervalChange::Slower);
  // With 11 % as the share: 22 of 200 is not more, and 23 of 200 is 11.5 %, rounded half to even, 12 %
  settings.SetSlowDownLatePercent(11);
  EXPECT_EQ(run(22), PC::SwapIntervalChange::None);
  EXPECT_EQ(run(23), PC::SwapIntervalChange::Slower);
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
  static_cast<void>(Feed(rule, refresh, 1, 14 * Ms, true, 2));
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
  // Back up it stops at what the average needs, not at the preferred interval: 20 + 1 ms needs 2, and fits 2 with 1 ms to spare
  EXPECT_EQ(Feed(rule, refresh, 42, 20 * Ms, false, 3), PC::SwapIntervalChange::Faster);
  EXPECT_EQ(rule.SwapInterval(), 2u);
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

TEST(SwapIntervalRule, ItSlowsDownNoFurtherThanTheLongestSwapInterval)
{
  // On a 10 kHz display 100 refreshes are 10 ms, well within the slowest frame time: the swap interval's own limit stops it
  PC::SwapIntervalRule rule(Settings(g_khz10));
  int64_t refresh = 0;
  rule.Reset(PC::PacerSettings::MaxSwapInterval - 1);
  // 30 ms of work needs 310 refreshes: it goes to the longest
  EXPECT_EQ(Feed(rule, refresh, 300, 30 * Ms, true, 400), PC::SwapIntervalChange::Slower);
  EXPECT_EQ(rule.SwapInterval(), PC::PacerSettings::MaxSwapInterval);
  EXPECT_EQ(Feed(rule, refresh, 300, 30 * Ms, true, 400), PC::SwapIntervalChange::None);
  EXPECT_EQ(rule.SwapInterval(), PC::PacerSettings::MaxSwapInterval);
}

TEST(SwapIntervalRule, AnyRemainderNeedsAnotherRefresh)
{
  // Two refreshes are 333 333.33 ticks; the average work plus the 1 ms margin is compared
  const auto neededAfter = [](const int64_t averageWork)
  {
    PC::SwapIntervalRule rule(Settings());
    int64_t refresh = 0;
    static_cast<void>(Feed(rule, refresh, 13, averageWork, true));
    return rule.SwapInterval();
  };
  EXPECT_EQ(neededAfter(333'333 - Ms), 2u);
  EXPECT_EQ(neededAfter(333'334 - Ms), 3u);
  // Less than a refresh still needs one: the rule goes one slower than it was
  EXPECT_EQ(neededAfter(2 * Ms), 2u);
}

TEST(SwapIntervalRule, ThePreferredIntervalIsTheFastest)
{
  PC::PacerSettings settings = Settings();
  settings.SetPreferredSwapInterval(2);
  PC::SwapIntervalRule rule(settings);
  EXPECT_EQ(rule.SwapInterval(), 2u);
  EXPECT_EQ(rule.PreferredSwapInterval(), 2u);
  int64_t refresh = 0;
  EXPECT_EQ(Feed(rule, refresh, 200, 2 * Ms, false, 2), PC::SwapIntervalChange::None);
  EXPECT_EQ(rule.SwapInterval(), 2u);
  rule.Reset(1);
  EXPECT_EQ(rule.SwapInterval(), 2u);
  rule.Reset(PC::PacerSettings::MaxSwapInterval + 5);
  EXPECT_EQ(rule.SwapInterval(), PC::PacerSettings::MaxSwapInterval);
  EXPECT_EQ(rule.Settings(), settings);
}

TEST(SwapIntervalRule, ANewRefreshPeriodStartsAgainAtTheTargetFrameRatesIntervalThere)
{
  PC::PacerSettings settings = Settings();
  settings.SetPreferredFrameRate(30);
  PC::SwapIntervalRule rule(settings);
  EXPECT_EQ(rule.Refresh(), g_hz60);
  EXPECT_EQ(rule.SwapInterval(), 2u);
  int64_t refresh = 0;
  static_cast<void>(Feed(rule, refresh, 5, 40 * Ms, true, 3));
  EXPECT_EQ(rule.FrameWindow().Frames, 5u);
  // 30 fps on 120 Hz is every fourth refresh, and the frames of the old display do not count
  rule.SetRefreshPeriod(g_hz120);
  EXPECT_EQ(rule.Refresh(), g_hz120);
  EXPECT_EQ(rule.SwapInterval(), 4u);
  EXPECT_EQ(rule.PreferredSwapInterval(), 4u);
  EXPECT_EQ(rule.FrameWindow().Frames, 0u);
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
  EXPECT_EQ(rule.FrameWindow().LateFrames, 122u);
}

TEST(SwapIntervalRule, TheWindowReportsWhatItHolds)
{
  PC::SwapIntervalRule rule(Settings(g_hz60, PC::SlowDownRule::FullWindow));
  EXPECT_EQ(rule.FrameWindow().Frames, 0u);
  int64_t refresh = 0;
  static_cast<void>(Feed(rule, refresh, 3, 10 * Ms, false));
  static_cast<void>(Feed(rule, refresh, 1, 22 * Ms, true));
  const PC::FrameWindowState window = rule.FrameWindow();
  EXPECT_EQ(window.Frames, 4u);
  EXPECT_EQ(window.LateFrames, 1u);
  EXPECT_EQ(window.AverageWork, Span(13 * Ms));
  EXPECT_EQ(window.Span, Span(g_hz60.TimeFor(4).Ticks() - g_hz60.TimeFor(1).Ticks()));
  EXPECT_FALSE(window.Full);
  // Work is counted from nothing to a window: a negative time is none, a longer one a window
  static_cast<void>(rule.AddFrame(g_hz60.TimeFor(5), Span(-7), false));
  EXPECT_EQ(rule.FrameWindow().AverageWork, Span((52 * Ms) / 5));
  static_cast<void>(rule.AddFrame(g_hz60.TimeFor(6), Span(900 * Second), false));
  EXPECT_EQ(rule.FrameWindow().AverageWork, Span(((52 * Ms) + (2 * Second)) / 6));
}

TEST(SwapIntervalRule, AWindowThatHoldsAllTheFramesItCanCountsAsFull)
{
  // Made for a 30 Hz display: room for twice the 62 frames of its window. On a 240 Hz display the window's 2 s are 480 frames, more than
  // it can hold, so its oldest frame is never 2 s old; it decides on the frames it has
  PC::SwapIntervalRule rule(Settings(PC::RefreshPeriod::FromRate(30), PC::SlowDownRule::FullWindow));
  const PC::RefreshPeriod fast = PC::RefreshPeriod::FromRate(240);
  rule.SetRefreshPeriod(fast);
  PC::SwapIntervalChange change = PC::SwapIntervalChange::None;
  int64_t frames = 0;
  while (change == PC::SwapIntervalChange::None && frames < 1'000)
  {
    ++frames;
    change = rule.AddFrame(fast.TimeFor(frames), Span(6 * Ms), true);
    if (change == PC::SwapIntervalChange::None)
    {
      EXPECT_EQ(rule.FrameWindow().Full, frames == 124) << frames;
    }
  }
  EXPECT_EQ(change, PC::SwapIntervalChange::Slower);
  EXPECT_EQ(frames, 124);
  // And once full it stays that size: the oldest frame makes room
  PC::PacerSettings fixed = Settings(PC::RefreshPeriod::FromRate(30));
  fixed.SetAutoSwapInterval(false);
  PC::SwapIntervalRule holding(fixed);
  holding.SetRefreshPeriod(fast);
  for (int64_t frame = 1; frame <= 300; ++frame)
  {
    static_cast<void>(holding.AddFrame(fast.TimeFor(frame), Span(Ms), frame % 2 == 0));
  }
  EXPECT_EQ(holding.FrameWindow().Frames, 124u);
  EXPECT_EQ(holding.FrameWindow().LateFrames, 62u);
  EXPECT_TRUE(holding.FrameWindow().Full);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// PacerAnimationClock
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(PacerAnimationClock, TheFirstFrameIsTheStartAndEveryStepIsWholeRefreshes)
{
  PC::PacerAnimationClock clock(g_hz60, Span(2 * Second), Span(1'000));
  EXPECT_EQ(clock.Refresh(), g_hz60);
  EXPECT_EQ(clock.Current().Time, Span(1'000));
  int64_t start = Second;
  const PC::AnimationTime first = clock.Advance(At(start), 1);
  EXPECT_EQ(first.Time, Span(1'000));
  EXPECT_EQ(first.Step, Span(0));
  EXPECT_EQ(first.StepRefreshes, 0u);
  // Frame starts after the vsync by anything under half a refresh still step one refresh each (the naive timer's jitter goes away)
  for (const int64_t late : {int64_t{30'000}, int64_t{0}, int64_t{75'000}, int64_t{10'000}, int64_t{60'000}, int64_t{-40'000}})
  {
    start += 166'667;
    EXPECT_EQ(clock.Advance(At(start + late), 1).StepRefreshes, 1u) << late;
  }
  EXPECT_EQ(clock.Current().Time, Span(1'000 + g_hz60.TimeFor(6).Ticks()));
  EXPECT_EQ(clock.Current().Step, Span(g_hz60.TimeFor(6).Ticks() - g_hz60.TimeFor(5).Ticks()));
}

TEST(PacerAnimationClock, ALateFrameShowsInTheNextMeasurementAndTheAnimationCatchesUp)
{
  PC::PacerAnimationClock clock(g_hz60, Span(2 * Second));
  int64_t start = Second;
  static_cast<void>(clock.Advance(At(start), 1));
  start += 166'667;
  static_cast<void>(clock.Advance(At(start), 1));
  // That frame missed its vsync: the next one starts two refreshes later, and steps two
  start += 2 * 166'667;
  const PC::FrameMeasurement late = clock.Measure(At(start));
  EXPECT_FALSE(late.Restarted);
  EXPECT_EQ(late.Refreshes, 2u);
  EXPECT_TRUE(late.Late);
  EXPECT_EQ(late.DisplayTime, g_hz60.TimeFor(3));
  EXPECT_EQ(clock.Step(1).StepRefreshes, 2u);
  // On time again
  start += 166'667;
  const PC::FrameMeasurement onTime = clock.Measure(At(start));
  EXPECT_EQ(onTime.Refreshes, 1u);
  EXPECT_FALSE(onTime.Late);
  EXPECT_EQ(clock.Step(1).StepRefreshes, 1u);
  EXPECT_EQ(clock.Current().Time, g_hz60.TimeFor(4));
}

TEST(PacerAnimationClock, AChangeOfSwapIntervalStepsByTheNewOneAtOnce)
{
  PC::PacerAnimationClock clock(g_hz60, Span(2 * Second));
  int64_t start = Second;
  static_cast<void>(clock.Advance(At(start), 1));
  // The next frame is held for two refreshes: it animates for the previous frame's display plus two
  start += 166'667;
  EXPECT_EQ(clock.Advance(At(start), 2).StepRefreshes, 2u);
  // It starts two refreshes later (on time at half rate), and the next one is back at full rate: one refresh after its display
  start += 2 * 166'667;
  const PC::FrameMeasurement measured = clock.Measure(At(start));
  EXPECT_EQ(measured.Refreshes, 2u);
  EXPECT_FALSE(measured.Late);
  EXPECT_EQ(clock.Step(1).StepRefreshes, 1u);
  // At half rate a frame never counts less than its swap interval: a start measured one refresh after is still two
  static_cast<void>(clock.Advance(At(start + 166'667), 2));
  const PC::FrameMeasurement early = clock.Measure(At(start + (2 * 166'667)));
  EXPECT_EQ(early.Refreshes, 2u);
  EXPECT_FALSE(early.Late);
  // A swap interval of nothing is one
  EXPECT_EQ(clock.Step(0).StepRefreshes, 1u);
}

TEST(PacerAnimationClock, AGapLongerThanTheLongestStartsAgainWithoutAJump)
{
  PC::PacerAnimationClock clock(g_hz60, Span(2 * Second));
  const int64_t start = Second;
  static_cast<void>(clock.Advance(At(start), 1));
  static_cast<void>(clock.Advance(At(start + 166'667), 1));
  // Exactly the longest gap is still measured: 120 refreshes
  const int64_t atTheLimit = start + 166'667 + (2 * Second);
  const PC::FrameMeasurement measured = clock.Measure(At(atTheLimit));
  EXPECT_FALSE(measured.Restarted);
  EXPECT_EQ(measured.Refreshes, 120u);
  EXPECT_EQ(clock.Step(1).StepRefreshes, 120u);
  // A tick more is a pause: nothing is measured, and the animation goes on one swap interval
  const int64_t afterAPause = atTheLimit + (2 * Second) + 1;
  const PC::FrameMeasurement paused = clock.Measure(At(afterAPause));
  EXPECT_TRUE(paused.Restarted);
  EXPECT_EQ(paused.Refreshes, 0u);
  EXPECT_FALSE(paused.Late);
  EXPECT_EQ(clock.Step(1).StepRefreshes, 1u);
  // So is a frame that starts before the previous one (a clock that went back)
  EXPECT_TRUE(clock.Measure(At(afterAPause - 5)).Restarted);
  EXPECT_EQ(clock.Step(2).StepRefreshes, 2u);
  EXPECT_EQ(clock.Current().Time, g_hz60.TimeFor(124));
}

TEST(PacerAnimationClock, TheLongestGapIsAtLeastTwoFrames)
{
  // A longest gap of a tick would make every frame a pause: two frames of the swap interval are always measured
  PC::PacerAnimationClock clock(g_hz60, Span(1));
  int64_t start = Second;
  static_cast<void>(clock.Advance(At(start), 2));
  start += g_hz60.TimeFor(4).Ticks();
  const PC::FrameMeasurement measured = clock.Measure(At(start));
  EXPECT_FALSE(measured.Restarted);
  EXPECT_EQ(measured.Refreshes, 4u);
  static_cast<void>(clock.Step(2));
  EXPECT_TRUE(clock.Measure(At(start + g_hz60.TimeFor(4).Ticks() + 1)).Restarted);
}

TEST(PacerAnimationClock, RestartAndANewRefreshPeriodMeasureNothingAcrossThem)
{
  PC::PacerAnimationClock clock(g_hz60, Span(2 * Second));
  int64_t start = Second;
  static_cast<void>(clock.Advance(At(start), 1));
  start += 166'667;
  static_cast<void>(clock.Advance(At(start), 1));
  clock.Restart();
  start += 5 * 166'667;
  EXPECT_TRUE(clock.Measure(At(start)).Restarted);
  EXPECT_EQ(clock.Step(1).StepRefreshes, 1u);
  // A step without a measurement counts as one after a restart
  EXPECT_EQ(clock.Step(3).StepRefreshes, 3u);
  // The animation time goes on at the new period: a refresh of 120 Hz
  const FP::TimeSpan before = clock.Current().Time;
  clock.SetRefreshPeriod(g_hz120);
  EXPECT_EQ(clock.Refresh(), g_hz120);
  start += 166'667;
  EXPECT_TRUE(clock.Measure(At(start)).Restarted);
  const PC::AnimationTime atTheNewPeriod = clock.Step(1);
  EXPECT_NEAR(static_cast<double>(atTheNewPeriod.Step.Ticks()), 83'333.33, 1.0);
  EXPECT_EQ(atTheNewPeriod.Time, Span(before.Ticks() + atTheNewPeriod.Step.Ticks()));
  start += 83'333;
  EXPECT_EQ(clock.Advance(At(start), 1).StepRefreshes, 1u);
}

TEST(PacerAnimationClock, TheDisplaysClockCountsRefreshesExactly)
{
  PC::PacerAnimationClock clock(g_hz60, Span(2 * Second));
  EXPECT_EQ(clock.DisplayTimeAfter(0), Span(0));
  EXPECT_EQ(clock.DisplayTimeAfter(3), Span(500'000));
  int64_t start = Second;
  static_cast<void>(clock.Advance(At(start), 1));
  for (int64_t frame = 1; frame <= 3; ++frame)
  {
    start += 166'667;
    const PC::FrameMeasurement measured = clock.Measure(At(start));
    EXPECT_EQ(measured.DisplayTime, g_hz60.TimeFor(frame));
    EXPECT_EQ(clock.DisplayTimeAfter(0), measured.DisplayTime);
    EXPECT_EQ(clock.DisplayTimeAfter(2), g_hz60.TimeFor(frame + 2));
    static_cast<void>(clock.Step(1));
  }
}

TEST(PacerAnimationClock, AnHourOfStepsDoesNotDrift)
{
  PC::PacerAnimationClock clock(g_hz60, Span(2 * Second));
  int64_t now = Second;
  // Every frame at swap interval 2, on time
  static_cast<void>(clock.Advance(At(now), 2));
  for (int64_t frame = 0; frame < 108'000; ++frame)
  {
    now += g_hz60.TimeFor(2 * (frame + 1)).Ticks() - g_hz60.TimeFor(2 * frame).Ticks();
    static_cast<void>(clock.Advance(At(now), 2));
  }
  // 216 000 refreshes: an hour exactly, the steps rounded to ticks never added up
  EXPECT_EQ(clock.Current().Time, Span(3'600 * Second));
}

TEST(PacerAnimationClock, ADisplayOffItsNominalRateNeverHitches)
{
  // The display really runs 0.1 % slower than the 60 Hz the clock was given (59.94 Hz taken for 60), and every frame start is up to
  // 2 ms early or late. A timer that kept a grid of its own would slide a whole refresh against the display every 17 s and have to jump;
  // measured frame by frame, every step is one refresh, for an hour
  PC::PacerAnimationClock clock(g_hz60, Span(2 * Second));
  Random random(20'261'002);
  static_cast<void>(clock.Advance(At(Second), 1));
  int64_t wrongSteps = 0;
  for (int64_t frame = 1; frame <= 216'000; ++frame)
  {
    const int64_t start = Second + g_ntsc.TimeFor(frame).Ticks() + random.Within(2 * Ms);
    wrongSteps += clock.Advance(At(start), 1).StepRefreshes == 1u ? 0 : 1;
  }
  EXPECT_EQ(wrongSteps, 0);
  EXPECT_EQ(clock.Current().Time, Span(3'600 * Second));
}

TEST(PacerAnimationClock, TheClocksWrapIsNotAGap)
{
  // The steady clock's ticks wrap at 2^64: frames across it are a refresh apart as anywhere
  PC::PacerAnimationClock clock(g_hz60, Span(2 * Second));
  const uint64_t first = std::numeric_limits<uint64_t>::max() - 400'000u;
  static_cast<void>(clock.Advance(FP::TickCount64::FromUnsignedTicks(first), 1));
  for (uint64_t frame = 1; frame <= 6; ++frame)
  {
    const FP::TickCount64 start =
      FP::TickCount64::FromUnsignedTicks(first + static_cast<uint64_t>(g_hz60.TimeFor(static_cast<int64_t>(frame)).Ticks()));
    EXPECT_EQ(clock.Advance(start, 1).StepRefreshes, 1u) << frame;
  }
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// FramePacer
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(FramePacer, TheFirstFrameIsAimedOneSwapIntervalAfterItStarts)
{
  PC::FramePacer pacer(Settings());
  EXPECT_EQ(pacer.Refresh(), g_hz60);
  EXPECT_EQ(pacer.SwapInterval(), 1u);
  EXPECT_EQ(pacer.Settings(), Settings());
  const PC::FrameSchedule schedule = pacer.BeginFrame(At(10 * Second));
  EXPECT_EQ(schedule.SwapInterval, 1u);
  EXPECT_EQ(schedule.AnimationTime, Span(0));
  EXPECT_EQ(schedule.AnimationStep, Span(0));
  EXPECT_EQ(schedule.IntendedDisplayTime, At((10 * Second) + 166'667));
  EXPECT_EQ(schedule.TargetFrameTime, FP::TimeSpan32(166'667u));
  EXPECT_EQ(schedule.PreferredFrameTime, FP::TimeSpan32(166'667u));
  EXPECT_EQ(schedule.Change, PC::SwapIntervalChange::None);
  EXPECT_EQ(pacer.EndFrame(At((10 * Second) + 90'000)), FP::TimeSpan32(90'000u));
}

TEST(FramePacer, FramesOnTimeFollowTheRefreshesForAnHourWithoutDrift)
{
  PC::FramePacer pacer(Settings());
  const int64_t start = Second;
  int64_t now = start;
  PC::FrameSchedule schedule;
  for (int64_t frame = 0; frame < 216'000; ++frame)
  {
    schedule = pacer.BeginFrame(At(now));
    static_cast<void>(pacer.EndFrame(At(now + (8 * Ms))));
    now = schedule.IntendedDisplayTime.Ticks();
  }
  EXPECT_EQ(schedule.IntendedDisplayTime, At(start + (3'600 * Second)));
  EXPECT_EQ(schedule.AnimationTime, g_hz60.TimeFor(215'999));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
}

TEST(FramePacer, ALateFrameIsMeasuredWhenTheNextOneStartsAndTheAnimationCatchesUp)
{
  PC::FramePacer pacer(Settings());
  const int64_t start = Second;
  const PC::FrameSchedule first = pacer.BeginFrame(At(start));
  static_cast<void>(pacer.EndFrame(At(start + (20 * Ms))));
  // Done after its refresh: shown one refresh later, where the next frame starts
  EXPECT_EQ(first.IntendedDisplayTime, At(start + 166'667));
  const int64_t shown = start + g_hz60.TimeFor(2).Ticks();
  const PC::FrameSchedule second = pacer.BeginFrame(At(shown));
  EXPECT_EQ(pacer.FrameWindow().Frames, 1u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span(20 * Ms));
  // It animates for the refresh after the one it starts at: two refreshes on from the first frame's
  EXPECT_EQ(second.AnimationStep, Span(333'333));
  EXPECT_EQ(second.IntendedDisplayTime, At(start + 500'000));
}

TEST(FramePacer, EndFrameGivesTheCpuBusyTimeAndTheWorkTheRuleCounts)
{
  PC::FramePacer pacer(Settings());
  // Before any frame there is nothing to end
  EXPECT_EQ(pacer.EndFrame(At(Second)), FP::TimeSpan32());
  const int64_t start = 3'600 * Second;
  static_cast<void>(pacer.BeginFrame(At(start)));
  // The application's own work time goes to the rule; the CPU busy time is the marker's
  EXPECT_EQ(pacer.EndFrame(At(start + (4 * Ms)), Span(12 * Ms)), FP::TimeSpan32(40'000u));
  static_cast<void>(pacer.BeginFrame(At(start + 166'667)));
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span(12 * Ms));
  // A present time before the frame's start, or further from it than the marker holds (0, another clock's time, nanoseconds), is unknown:
  // no CPU busy time, and no work counted
  EXPECT_EQ(pacer.EndFrame(At(start)), FP::TimeSpan32());
  static_cast<void>(pacer.BeginFrame(At(start + (2 * 166'667))));
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span(6 * Ms));
  EXPECT_EQ(pacer.EndFrame(At(0)), FP::TimeSpan32());
  EXPECT_EQ(pacer.EndFrame(At(start * 100)), FP::TimeSpan32());
  // The longest the marker holds is known
  EXPECT_EQ(pacer.EndFrame(At(start + (2 * 166'667) + int64_t{0xFFFF'FFFF})), FP::TimeSpan32::MaxValue());
}

TEST(FramePacer, WithoutEndFrameTheFrameCountsAsPresentedWhenTheNextOneStarts)
{
  PC::FramePacer pacer(Settings());
  const int64_t start = Second;
  static_cast<void>(pacer.BeginFrame(At(start)));
  static_cast<void>(pacer.BeginFrame(At(start + 166'667 + Ms)));
  EXPECT_EQ(pacer.FrameWindow().Frames, 1u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span(166'667 + Ms));
}

TEST(FramePacer, TheScheduleCarriesTheRulesChangeAndTheNewInterval)
{
  PC::FramePacer pacer(Settings());
  // 20 ms frames: every frame at 60 fps is late
  const std::vector<PC::FrameSchedule> schedules = RunFrames(pacer, Second, 40, 20 * Ms);
  std::vector<std::size_t> changedAt;
  for (std::size_t frame = 0; frame < schedules.size(); ++frame)
  {
    if (schedules[frame].Change != PC::SwapIntervalChange::None)
    {
      changedAt.push_back(frame);
      EXPECT_EQ(schedules[frame].Change, PC::SwapIntervalChange::Slower);
      EXPECT_EQ(schedules[frame].SwapInterval, 2u);
      EXPECT_EQ(schedules[frame].TargetFrameTime, FP::TimeSpan32(333'333u));
      EXPECT_EQ(schedules[frame].PreferredFrameTime, FP::TimeSpan32(166'667u));
    }
  }
  // 13 late frames (the 13th is measured when the 14th begins) slow it down; at 30 fps they fit
  ASSERT_EQ(changedAt.size(), 1u);
  EXPECT_EQ(changedAt[0], 13u);
  EXPECT_EQ(pacer.SwapInterval(), 2u);
}

TEST(FramePacer, ATargetFrameRateIsHeldAsAFixedSwapInterval)
{
  PC::PacerSettings settings = Settings();
  settings.SetPreferredFrameRate(30);
  settings.SetAutoSwapInterval(false);
  PC::FramePacer pacer(settings);
  // Frames that would fit a refresh are still held for two, and frames that miss do not change the rate
  const std::vector<PC::FrameSchedule> fast = RunFrames(pacer, Second, 200, 5 * Ms);
  for (const PC::FrameSchedule& schedule : fast)
  {
    EXPECT_EQ(schedule.SwapInterval, 2u);
    EXPECT_EQ(schedule.TargetFrameTime, FP::TimeSpan32(333'333u));
    EXPECT_EQ(schedule.PreferredFrameTime, FP::TimeSpan32(333'333u));
  }
  EXPECT_EQ(fast.back().AnimationTime, g_hz60.TimeFor(2 * 199));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  PC::FramePacer slow(settings);
  static_cast<void>(RunFrames(slow, Second, 200, 40 * Ms));
  EXPECT_EQ(slow.SwapInterval(), 2u);
  EXPECT_GT(slow.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(slow.FrameWindow().LateFrames, slow.FrameWindow().Frames);
}

TEST(FramePacer, APauseLongerThanTheWindowStartsAgainAndKeepsTheSwapInterval)
{
  PC::FramePacer pacer(Settings());
  // Slowed down to half rate by late frames
  int64_t lastStart = 0;
  static_cast<void>(RunFrames(pacer, Second, 40, 20 * Ms, lastStart));
  ASSERT_EQ(pacer.SwapInterval(), 2u);
  ASSERT_GT(pacer.FrameWindow().Frames, 0u);
  // A frame that starts exactly a window after the previous one is still its successor: that one was shown 120 refreshes on where 2 were
  // planned, late, and the rule slows down on it
  PC::FramePacer atTheLimit = pacer;
  const uint32_t framesBefore = pacer.FrameWindow().Frames;
  const PC::FrameSchedule measured = atTheLimit.BeginFrame(At(lastStart + (2 * Second)));
  EXPECT_EQ(measured.Change, PC::SwapIntervalChange::Slower);
  EXPECT_EQ(measured.SwapInterval, 3u);
  EXPECT_EQ(g_hz60.NearestRefreshes(measured.AnimationStep), 120 - 2 + 3);
  // A tick more is a pause: the frame before it is not counted, the window is empty, the swap interval stays, and the animation goes on
  // one swap interval
  const PC::FrameSchedule resumed = pacer.BeginFrame(At(lastStart + (2 * Second) + 1));
  EXPECT_GT(framesBefore, 0u);
  EXPECT_EQ(pacer.FrameWindow().Frames, 0u);
  EXPECT_EQ(pacer.SwapInterval(), 2u);
  EXPECT_EQ(resumed.SwapInterval, 2u);
  EXPECT_EQ(resumed.AnimationStep, Span(g_hz60.TimeFor(2).Ticks()));
  EXPECT_LE(std::abs(resumed.IntendedDisplayTime.Ticks() - (lastStart + (2 * Second) + 1 + 333'333)), 1);
  EXPECT_EQ(resumed.Change, PC::SwapIntervalChange::None);
}

TEST(FramePacer, AWindowShorterThanAFrameStillPaces)
{
  // The window's shortest is a tick; two frames are always measured, so a frame is not a pause
  PC::PacerSettings settings = Settings();
  settings.SetFrameWindowLength(PC::PacerSettings::MinFrameWindowLength);
  settings.SetAutoSwapInterval(false);
  PC::FramePacer pacer(settings);
  const std::vector<PC::FrameSchedule> schedules = RunFrames(pacer, Second, 50, 5 * Ms);
  EXPECT_EQ(schedules.back().AnimationTime, g_hz60.TimeFor(49));
  EXPECT_GT(pacer.FrameWindow().Frames, 0u);
}

TEST(FramePacer, ResetPlansTheNextFrameAsTheFirstAtThePreferredInterval)
{
  PC::FramePacer pacer(Settings());
  const std::vector<PC::FrameSchedule> before = RunFrames(pacer, Second, 40, 20 * Ms);
  ASSERT_EQ(pacer.SwapInterval(), 2u);
  pacer.Reset();
  EXPECT_EQ(pacer.SwapInterval(), 1u);
  EXPECT_EQ(pacer.FrameWindow().Frames, 0u);
  // An EndFrame for the frame from before the reset is nothing
  EXPECT_EQ(pacer.EndFrame(At(before.back().IntendedDisplayTime.Ticks())), FP::TimeSpan32());
  const int64_t later = before.back().IntendedDisplayTime.Ticks() + 123;
  const PC::FrameSchedule schedule = pacer.BeginFrame(At(later));
  EXPECT_EQ(schedule.IntendedDisplayTime, At(later + 166'667));
  EXPECT_EQ(pacer.FrameWindow().Frames, 0u);
  // The animation time goes on, one swap interval
  EXPECT_EQ(schedule.AnimationTime, Span(before.back().AnimationTime.Ticks() + schedule.AnimationStep.Ticks()));
  EXPECT_NEAR(static_cast<double>(schedule.AnimationStep.Ticks()), 166'666.67, 1.0);
}

TEST(FramePacer, ANewRefreshPeriodStartsAgainAtTheSwapIntervalTheTargetFrameRateGivesThere)
{
  PC::PacerSettings settings = Settings();
  settings.SetPreferredFrameRate(30);
  PC::FramePacer pacer(settings);
  const int64_t start = Second;
  EXPECT_EQ(pacer.BeginFrame(At(start)).SwapInterval, 2u);
  static_cast<void>(pacer.EndFrame(At(start + Ms)));
  static_cast<void>(pacer.BeginFrame(At(start + 333'333)));
  EXPECT_EQ(pacer.FrameWindow().Frames, 1u);
  // The window moved to a 120 Hz display: 30 fps is every fourth refresh there
  pacer.SetRefreshPeriod(g_hz120);
  EXPECT_EQ(pacer.Refresh(), g_hz120);
  EXPECT_EQ(pacer.SwapInterval(), 4u);
  EXPECT_EQ(pacer.FrameWindow().Frames, 0u);
  const PC::FrameSchedule schedule = pacer.BeginFrame(At(start + 333'333 + (5 * Ms)));
  EXPECT_EQ(schedule.SwapInterval, 4u);
  EXPECT_EQ(schedule.TargetFrameTime, FP::TimeSpan32(333'333u));
  EXPECT_EQ(schedule.PreferredFrameTime, FP::TimeSpan32(333'333u));
  EXPECT_LE(std::abs(schedule.IntendedDisplayTime.Ticks() - (start + 333'333 + (5 * Ms) + 333'333)), 1);
  EXPECT_EQ(pacer.FrameWindow().Frames, 0u);
}

TEST(FramePacer, ThePeriodItHasChangesNothing)
{
  // One pacer is told its own period every frame: it plans as the other one
  PC::FramePacer told(Settings());
  PC::FramePacer plain(Settings());
  int64_t now = Second;
  for (int frame = 0; frame < 20; ++frame)
  {
    told.SetRefreshPeriod(g_hz60);
    const PC::FrameSchedule schedule = told.BeginFrame(At(now));
    const PC::FrameSchedule other = plain.BeginFrame(At(now));
    EXPECT_EQ(schedule.IntendedDisplayTime, other.IntendedDisplayTime) << frame;
    EXPECT_EQ(schedule.AnimationTime, other.AnimationTime) << frame;
    static_cast<void>(told.EndFrame(At(now + (4 * Ms))));
    static_cast<void>(plain.EndFrame(At(now + (4 * Ms))));
    now = schedule.IntendedDisplayTime.Ticks();
  }
  EXPECT_EQ(told.FrameWindow().Frames, 19u);
}
