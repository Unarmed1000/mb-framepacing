// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer module: the refresh period's exact arithmetic, the settings and the target frame rate, and the swap interval rule's
// decisions at their edges.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/RefreshTime.hpp>
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
  constexpr int64_t Ms = FP::NanosecondTimeSpan::NanosecondsPerMillisecond;
  constexpr int64_t Second = FP::NanosecondTimeSpan::NanosecondsPerSecond;
  const PC::RefreshPeriod g_hz60 = PC::RefreshPeriod::FromRate(60);
  const PC::RefreshPeriod g_hz120 = PC::RefreshPeriod::FromRate(120);
  //! 59.94 Hz as DXGI states it
  const PC::RefreshPeriod g_ntsc = PC::RefreshPeriod::FromRate(60'000, 1'001);
  //! The shortest period: 10 kHz, 100 µs
  const PC::RefreshPeriod g_khz10 = PC::RefreshPeriod::FromNanosecondTimeSpan(PC::RefreshPeriod::MinPeriod);

  constexpr FP::NanosecondTimeSpan Span(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTimeSpan(nanoseconds);
  }

  PC::PacerSettings Settings(const PC::RefreshPeriod period = g_hz60, const PC::SlowDownRule rule = PC::SlowDownRule::LateCount)
  {
    PC::PacerSettings settings(period);
    settings.SetSlowDown(rule);
    return settings;
  }

  //! Feeds the rule frames shown gapRefreshes apart (60 Hz), each late or not, with workNanoseconds of work.
  PC::SwapIntervalChange Feed(PC::SwapIntervalRule& rule, int64_t& displayRefresh, const int64_t frames, const int64_t workNanoseconds,
                              const bool late, const int64_t gapRefreshes = 1)
  {
    PC::SwapIntervalChange last = PC::SwapIntervalChange::Unchanged;
    for (int64_t frame = 0; frame < frames; ++frame)
    {
      displayRefresh += gapRefreshes;
      const PC::SwapIntervalChange change = rule.AddFrame(rule.Refresh().TimeFor(displayRefresh), Span(workNanoseconds), late);
      if (change != PC::SwapIntervalChange::Unchanged)
      {
        last = change;
      }
    }
    return last;
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
  EXPECT_EQ(g_hz60.ToNanosecondTimeSpan(), Span(16'666'667));
  // 60 Hz for an hour is 216 000 refreshes of exactly 1/60 s: no drift, where whole nanoseconds would be 72 µs off
  EXPECT_EQ(g_hz60.TimeFor(216'000), Span(3'600 * Second));
  EXPECT_EQ(g_hz60.TimeFor(3), Span(50'000'000));
  EXPECT_EQ(g_hz60.TimeFor(1), Span(16'666'667));
  EXPECT_EQ(g_hz60.TimeFor(2), Span(33'333'333));
  EXPECT_EQ(g_hz60.TimeFor(0), Span(0));
  // 59.94 Hz as DXGI states it: 60000 refreshes take 1001 s
  EXPECT_EQ(g_ntsc.TimeFor(60'000), Span(1'001 * Second));
  EXPECT_EQ(g_ntsc.ToNanosecondTimeSpan(), Span(16'683'333));
  // wl_output's mHz
  EXPECT_EQ(PC::RefreshPeriod::FromRate(59'940, 1'000).TimeFor(59'940), Span(1'000 * Second));
}

TEST(RefreshPeriod, AWholeNumberOfNanoseconds)
{
  // A period as a platform gives one, in whole nanoseconds, is kept as it is: no refresh of it is rounded
  EXPECT_EQ(PC::RefreshPeriod::FromNanosecondTimeSpan(Span(16'666'667)).ToNanosecondTimeSpan(), Span(16'666'667));
  EXPECT_EQ(PC::RefreshPeriod::FromNanosecondTimeSpan(Span(16'666'667)).TimeFor(216'000), Span(int64_t{16'666'667} * 216'000));
  // 240.016 Hz as a display timing states it: a tick of 100 ns can not hold it (41'663.89), and a nanosecond does
  const PC::RefreshPeriod measured = PC::RefreshPeriod::FromNanosecondTimeSpan(Span(4'166'389));
  EXPECT_EQ(measured.ToNanosecondTimeSpan(), Span(4'166'389));
  EXPECT_EQ(measured.TimeFor(240), Span(int64_t{4'166'389} * 240));
  EXPECT_EQ(measured.FloorRefreshes(Span((int64_t{4'166'389} * 240) - 1)), 239);
  EXPECT_EQ(g_hz60, PC::RefreshPeriod::FromRate(120, 2));
  EXPECT_NE(g_hz60, g_hz120);
  // A rate keeps the fraction of a nanosecond that a whole number of them does not have
  EXPECT_NE(g_hz60, PC::RefreshPeriod::FromNanosecondTimeSpan(Span(16'666'667)));
}

TEST(RefreshPeriod, ItIsAlwaysValid)
{
  const PC::RefreshPeriod shortest = PC::RefreshPeriod::FromNanosecondTimeSpan(PC::RefreshPeriod::MinPeriod);
  const PC::RefreshPeriod longest = PC::RefreshPeriod::FromNanosecondTimeSpan(PC::RefreshPeriod::MaxPeriod);
  // The range's ends: 100 µs (10 kHz) and 1 s (1 Hz)
  static_assert(PC::RefreshPeriod::MinPeriod == Span(100'000) && PC::RefreshPeriod::MaxPeriod == Span(Second));
  EXPECT_EQ(shortest.ToNanosecondTimeSpan(), Span(100'000));
  EXPECT_EQ(longest.ToNanosecondTimeSpan(), Span(Second));
  EXPECT_EQ(PC::RefreshPeriod::FromRate(10'000), shortest);
  EXPECT_EQ(PC::RefreshPeriod::FromRate(1), longest);
#ifdef NDEBUG
  // Without asserts a period outside the range is clamped into it
  EXPECT_EQ(PC::RefreshPeriod::FromRate(0), longest);
  EXPECT_EQ(PC::RefreshPeriod::FromRate(60, 0), shortest);
  EXPECT_EQ(PC::RefreshPeriod::FromRate(1, 2), longest);
  EXPECT_EQ(PC::RefreshPeriod::FromRate(20'000), shortest);
  EXPECT_EQ(PC::RefreshPeriod::FromNanosecondTimeSpan(Span(0)), shortest);
  EXPECT_EQ(PC::RefreshPeriod::FromNanosecondTimeSpan(FP::NanosecondTimeSpan::MinValue()), shortest);
  EXPECT_EQ(PC::RefreshPeriod::FromNanosecondTimeSpan(FP::NanosecondTimeSpan::MaxValue()), longest);
  EXPECT_EQ(PC::RefreshPeriod::FromNanosecondTimeSpan(Span(-1)), shortest);
  EXPECT_EQ(PC::RefreshPeriod::FromNanosecondTimeSpan(Span(50'000)), shortest);
  EXPECT_EQ(PC::RefreshPeriod::FromNanosecondTimeSpan(Span(Second + 1)), longest);
  // A negative number of refreshes is none
  EXPECT_EQ(g_hz60.TimeFor(-3), Span(0));
#elif GTEST_HAS_DEATH_TEST
  EXPECT_DEATH(static_cast<void>(PC::RefreshPeriod::FromRate(0)), "");
  EXPECT_DEATH(static_cast<void>(PC::RefreshPeriod::FromRate(60, 0)), "");
  EXPECT_DEATH(static_cast<void>(PC::RefreshPeriod::FromNanosecondTimeSpan(Span(0))), "");
  EXPECT_DEATH(static_cast<void>(PC::RefreshPeriod::FromNanosecondTimeSpan(Span(50'000))), "");
  EXPECT_DEATH(static_cast<void>(g_hz60.TimeFor(-3)), "");
#else
  GTEST_SKIP() << "asserts are on and death tests are not available";
#endif
}

TEST(RefreshPeriod, RefreshesInATime)
{
  EXPECT_EQ(g_hz60.FloorRefreshes(Span(16'666'666)), 0);
  EXPECT_EQ(g_hz60.FloorRefreshes(Span(16'666'667)), 1);
  EXPECT_EQ(g_hz60.FloorRefreshes(Span(0)), 0);
  EXPECT_EQ(g_hz60.FloorRefreshes(Span(-5)), 0);
  // The estimate from the rounded period is corrected both ways: 60 Hz rounds up (too few), 59.94 Hz rounds down (too many)
  EXPECT_EQ(g_hz60.FloorRefreshes(Span(3'600 * Second)), 216'000);
  EXPECT_EQ(g_ntsc.FloorRefreshes(Span(g_ntsc.TimeFor(1'000'000).Nanoseconds() - 1)), 999'999);
  EXPECT_EQ(g_ntsc.FloorRefreshes(g_ntsc.TimeFor(1'000'000)), 1'000'000);
  EXPECT_EQ(g_hz60.NearestRefreshes(Span(8'333'333)), 0);
  EXPECT_EQ(g_hz60.NearestRefreshes(Span(8'333'334)), 1);
  EXPECT_EQ(g_hz60.NearestRefreshes(Span(25'000'000)), 2);    // a tie: the later refresh
  EXPECT_EQ(g_hz60.NearestRefreshes(Span(24'999'999)), 1);
  EXPECT_EQ(g_hz60.NearestRefreshes(Span(0)), 0);
  EXPECT_EQ(g_hz60.NearestRefreshes(Span(-5)), 0);
  // The fewest refreshes that take at least a time: 2 refreshes are 33 333 333 ns
  EXPECT_EQ(g_hz60.RefreshesToFit(Span(33'333'333)), 2);
  EXPECT_EQ(g_hz60.RefreshesToFit(Span(33'333'334)), 3);
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
  EXPECT_EQ(time.ToNanosecondTimeSpan(), Span(0));
  EXPECT_EQ(time, PC::RefreshTime(Span(0)));
  // 60 Hz is 16 666 666.67 ns: one refresh rounds up, two round down, three are exact
  time.Add(1, g_hz60);
  EXPECT_EQ(time.ToNanosecondTimeSpan(), Span(16'666'667));
  time.Add(1, g_hz60);
  EXPECT_EQ(time.ToNanosecondTimeSpan(), Span(33'333'333));
  time.Add(1, g_hz60);
  EXPECT_EQ(time.ToNanosecondTimeSpan(), Span(50'000'000));
  EXPECT_NE(time, PC::RefreshTime(Span(50'000'001)));
  // After leaves the time as it is
  EXPECT_EQ(time.After(2, g_hz60).ToNanosecondTimeSpan(), Span(83'333'333));
  EXPECT_EQ(time.After(0, g_hz60), time);
  EXPECT_EQ(time.ToNanosecondTimeSpan(), Span(50'000'000));
  // An hour added a refresh at a time is an hour to the nanosecond, from any start
  PC::RefreshTime hour(Span(-7 * Second));
  for (int frame = 0; frame < 216'000; ++frame)
  {
    hour.Add(1, g_hz60);
  }
  EXPECT_EQ(hour.ToNanosecondTimeSpan(), Span(3'593 * Second));
  // Many refreshes at once
  EXPECT_EQ(PC::RefreshTime().After(216'000, g_hz60).ToNanosecondTimeSpan(), Span(3'600 * Second));
  EXPECT_EQ(PC::RefreshTime().After(60'000, g_ntsc).ToNanosecondTimeSpan(), Span(1'001 * Second));
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

TEST(PacerSettings, TheDefaultFrameMarginIsAMillisecondAndAtMostAnEighthOfARefresh)
{
  // Up to 125 Hz the margin is 1 ms; above, an eighth of the refresh period, so that twice the margin always leaves room to speed up
  const PC::PacerSettings defaults(g_hz60);
  EXPECT_EQ(defaults.FrameMargin(), Span(Ms));
  EXPECT_EQ(defaults.FrameMarginAt(g_hz120), Span(Ms));
  EXPECT_EQ(defaults.FrameMarginAt(PC::RefreshPeriod::FromRate(125)), Span(Ms));
  EXPECT_EQ(defaults.FrameMarginAt(PC::RefreshPeriod::FromRate(144)), Span(6'944'444 / 8));
  EXPECT_EQ(defaults.FrameMarginAt(PC::RefreshPeriod::FromRate(500)), Span(250'000));
  EXPECT_EQ(defaults.FrameMarginAt(g_khz10), Span(12'500));
  // The settings' own margin is the one on their display
  EXPECT_EQ(PC::PacerSettings(PC::RefreshPeriod::FromRate(500)).FrameMargin(), Span(250'000));
  PC::PacerSettings moved(g_hz60);
  moved.SetRefresh(PC::RefreshPeriod::FromRate(240));
  EXPECT_EQ(moved.FrameMargin(), Span(4'166'667 / 8));

  // A margin that was set is that margin on every display, also when it is the default's value
  PC::PacerSettings set(g_hz60);
  set.SetFrameMargin(Span(Ms));
  EXPECT_EQ(set.FrameMargin(), Span(Ms));
  EXPECT_EQ(set.FrameMarginAt(PC::RefreshPeriod::FromRate(500)), Span(Ms));
  EXPECT_NE(set, defaults);
  set.SetFrameMargin(Span(3 * Ms));
  EXPECT_EQ(set.FrameMarginAt(g_hz60), Span(3 * Ms));
  EXPECT_EQ(set.FrameMarginAt(g_khz10), Span(3 * Ms));
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
  EXPECT_EQ(settings.PreferredFrameTime(), Span(33'333'333));
  // 30 fps: every second refresh at 60 Hz, every fourth at 120 Hz
  EXPECT_EQ(settings.PreferredSwapIntervalAt(g_hz60), 2u);
  EXPECT_EQ(settings.PreferredSwapIntervalAt(g_hz120), 4u);
  // 29.97 fps on a 59.94 Hz display, both as rationals
  settings.SetPreferredFrameRate(30'000, 1'001);
  EXPECT_EQ(settings.PreferredFrameTime(), Span(33'366'667));
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
  // A rate faster than a nanosecond a frame is a nanosecond
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
      const double frameNanoseconds = static_cast<double>(Second) / fps;
      const auto expected = static_cast<uint32_t>(
        std::max(1.0, std::ceil((frameNanoseconds / static_cast<double>(display.ToNanosecondTimeSpan().Nanoseconds())) - 0.05)));
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
  EXPECT_EQ(Feed(rule, refresh, 121, 20 * Ms, true), PC::SwapIntervalChange::Unchanged);
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
  EXPECT_EQ(Feed(rule, refresh, 12, 20 * Ms, true), PC::SwapIntervalChange::Unchanged);
  EXPECT_EQ(Feed(rule, refresh, 1, 20 * Ms, true), PC::SwapIntervalChange::Slower);
  EXPECT_EQ(rule.SwapInterval(), 2u);
  // After the change only the late frames at the new interval count: a full window at 30 fps holds 60 frames, so 7 late ones
  EXPECT_EQ(Feed(rule, refresh, 6, 40 * Ms, true, 3), PC::SwapIntervalChange::Unchanged);
  EXPECT_EQ(Feed(rule, refresh, 1, 40 * Ms, true, 3), PC::SwapIntervalChange::Slower);
  EXPECT_EQ(rule.SwapInterval(), 3u);
}

TEST(SwapIntervalRule, AnotherRuleIsTakenOverWithItsSwapIntervalAndItsFrameWindow)
{
  // A rule that slowed down, with late frames in its frame window
  PC::SwapIntervalRule rule(Settings());
  const PC::RefreshPeriod period = rule.Refresh();
  for (int64_t frame = 0; frame < 400 && rule.SwapInterval() == 1; ++frame)
  {
    static_cast<void>(rule.AddFrame(period.TimeFor(2 * frame), Span(20'000'000), true));
  }
  ASSERT_EQ(rule.SwapInterval(), 2u);
  for (int64_t frame = 0; frame < 30; ++frame)
  {
    static_cast<void>(rule.AddFrame(period.TimeFor(1'000 + (2 * frame)), Span(9'000'000), (frame % 7) == 0, Span(1'000)));
  }
  const PC::FrameWindowState window = rule.FrameWindow();
  ASSERT_EQ(window.Frames, 30u);
  ASSERT_EQ(window.LateFrames, 5u);

  // A rule with the same settings goes on from it: the same swap interval, the same frame window, and the same decisions after
  PC::SwapIntervalRule other(Settings());
  other.TakeOver(rule);
  EXPECT_EQ(other.SwapInterval(), 2u);
  EXPECT_EQ(other.FrameWindow(), window);
  for (int64_t frame = 30; frame < 200; ++frame)
  {
    const PC::SwapIntervalChange expected = rule.AddFrame(period.TimeFor(1'000 + (2 * frame)), Span(3'000'000), false);
    ASSERT_EQ(other.AddFrame(period.TimeFor(1'000 + (2 * frame)), Span(3'000'000), false), expected) << frame;
    ASSERT_EQ(other.FrameWindow(), rule.FrameWindow()) << frame;
  }
  EXPECT_EQ(other.SwapInterval(), rule.SwapInterval());

  // A rule with a frame window of another size takes the swap interval and starts with an empty frame window; and a swap
  // interval below the one its own settings prefer is not taken
  static_cast<void>(rule.AddFrame(period.TimeFor(2'000), Span(3'000'000), false));
  PC::PacerSettings longer = Settings();
  longer.SetFrameWindowLength(Span(8 * FP::NanosecondTimeSpan::NanosecondsPerSecond));
  PC::SwapIntervalRule larger(longer);
  rule.Reset(3);
  static_cast<void>(rule.AddFrame(period.TimeFor(3'000), Span(3'000'000), false));
  larger.TakeOver(rule);
  EXPECT_EQ(larger.SwapInterval(), 3u);
  EXPECT_EQ(larger.FrameWindow().Frames, 0u);
  longer.SetPreferredSwapInterval(4);
  PC::SwapIntervalRule slower(longer);
  slower.TakeOver(rule);
  EXPECT_EQ(slower.SwapInterval(), 4u);
}

TEST(SwapIntervalRule, TheFrameWindowIsMovedOntoAnotherClockAndGoesOn)
{
  PC::SwapIntervalRule rule(Settings());
  const PC::RefreshPeriod period = rule.Refresh();
  // Nothing to move in an empty frame window
  rule.RebaseNewest(Span(5));
  EXPECT_EQ(rule.FrameWindow().Frames, 0u);

  // Half a frame window of frames on a clock that is at an hour
  const int64_t hour = int64_t{3'600} * FP::NanosecondTimeSpan::NanosecondsPerSecond;
  for (int64_t frame = 0; frame < 60; ++frame)
  {
    static_cast<void>(rule.AddFrame(Span(hour + period.TimeFor(frame).Nanoseconds()), Span(3'000'000), (frame % 10) == 0));
  }
  const PC::FrameWindowState before = rule.FrameWindow();
  ASSERT_EQ(before.Frames, 60u);
  // The frames to come count from zero: the frame window is moved, the newest of it to one refresh before zero, and it is
  // what it was
  rule.RebaseNewest(Span(-period.TimeFor(1).Nanoseconds()));
  EXPECT_EQ(rule.FrameWindow(), before);
  // The frames that come are counted on from it, and the old ones leave the frame window when they are its length old: not
  // one stays beyond that, as it would with times an hour apart
  for (int64_t frame = 0; frame < 400; ++frame)
  {
    static_cast<void>(rule.AddFrame(period.TimeFor(frame), Span(3'000'000), false));
    ASSERT_LE(rule.FrameWindow().Span, Span(rule.Settings().FrameWindowLength().Nanoseconds() + period.TimeFor(1).Nanoseconds())) << frame;
  }
  EXPECT_EQ(rule.FrameWindow().LateFrames, 0u);
}

TEST(SwapIntervalRule, TheShareIsRoundedAsTheSimulationRoundsIt)
{
  // A 10 kHz display and a window of 198.5 refreshes: a full window holds exactly 200 frames
  PC::PacerSettings settings = Settings(g_khz10, PC::SlowDownRule::FullWindow);
  settings.SetFrameWindowLength(Span(19'850'000));
  const auto run = [&settings](const int64_t lateFrames)
  {
    PC::SwapIntervalRule rule(settings);
    PC::SwapIntervalChange change = PC::SwapIntervalChange::Unchanged;
    for (int64_t frame = 0; frame < 200; ++frame)
    {
      change = rule.AddFrame(g_khz10.TimeFor(frame), Span(5 * Ms), frame < lateFrames);
    }
    return change;
  };
  // 21 of 200 is 10.5 %: rounded half to even, 10 %, not more than 10 %
  EXPECT_EQ(run(21), PC::SwapIntervalChange::Unchanged);
  // 22 of 200 is 11 %
  EXPECT_EQ(run(22), PC::SwapIntervalChange::Slower);
  // With 11 % as the share: 22 of 200 is not more, and 23 of 200 is 11.5 %, rounded half to even, 12 %
  settings.SetSlowDownLatePercent(11);
  EXPECT_EQ(run(22), PC::SwapIntervalChange::Unchanged);
  EXPECT_EQ(run(23), PC::SwapIntervalChange::Slower);
}

TEST(SwapIntervalRule, ItSpeedsUpOnlyOnAFullWindowWithoutLateFramesAndRoomToSpare)
{
  PC::SwapIntervalRule rule(Settings());
  rule.Reset(2);
  int64_t refresh = 0;
  // 15 ms: with 1 ms margin and 1 ms to spare, 17 ms does not fit a 16.7 ms refresh
  EXPECT_EQ(Feed(rule, refresh, 80, 15 * Ms, false, 2), PC::SwapIntervalChange::Unchanged);
  EXPECT_EQ(rule.SwapInterval(), 2u);
  // 14 ms: 16 ms fits; the window is full after 2 s of frames at 30 fps (62 frames)
  rule.Clear();
  EXPECT_EQ(Feed(rule, refresh, 61, 14 * Ms, false, 2), PC::SwapIntervalChange::Unchanged);
  EXPECT_EQ(Feed(rule, refresh, 1, 14 * Ms, false, 2), PC::SwapIntervalChange::Faster);
  EXPECT_EQ(rule.SwapInterval(), 1u);
}

TEST(SwapIntervalRule, OneLateFrameInTheWindowKeepsItSlow)
{
  PC::SwapIntervalRule rule(Settings());
  rule.Reset(2);
  int64_t refresh = 0;
  static_cast<void>(Feed(rule, refresh, 1, 14 * Ms, true, 2));
  EXPECT_EQ(Feed(rule, refresh, 61, 14 * Ms, false, 2), PC::SwapIntervalChange::Unchanged);
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
  EXPECT_EQ(Feed(rule, refresh, 200, 120 * Ms, true, 8), PC::SwapIntervalChange::Unchanged);
  EXPECT_EQ(rule.SwapInterval(), 5u);
}

TEST(SwapIntervalRule, ItSlowsDownNoFurtherThanTheLongestSwapInterval)
{
  // On a 10 kHz display 100 refreshes are 10 ms, well within the slowest frame time: the swap interval's own limit stops it
  PC::SwapIntervalRule rule(Settings(g_khz10));
  int64_t refresh = 0;
  rule.Reset(PC::PacerSettings::MaxSwapInterval - 1);
  // 30 ms of work needs 310 refreshes: it goes to the longest
  EXPECT_EQ(Feed(rule, refresh, 300, 30 * Ms, true, 40'000), PC::SwapIntervalChange::Slower);
  EXPECT_EQ(rule.SwapInterval(), PC::PacerSettings::MaxSwapInterval);
  EXPECT_EQ(Feed(rule, refresh, 300, 30 * Ms, true, 40'000), PC::SwapIntervalChange::Unchanged);
  EXPECT_EQ(rule.SwapInterval(), PC::PacerSettings::MaxSwapInterval);
}

TEST(SwapIntervalRule, AnyRemainderNeedsAnotherRefresh)
{
  // Two refreshes are 33 333 333.33 ns; the average work plus the 1 ms margin is compared
  const auto neededAfter = [](const int64_t averageWork)
  {
    PC::SwapIntervalRule rule(Settings());
    int64_t refresh = 0;
    static_cast<void>(Feed(rule, refresh, 13, averageWork, true));
    return rule.SwapInterval();
  };
  EXPECT_EQ(neededAfter(33'333'333 - Ms), 2u);
  EXPECT_EQ(neededAfter(33'333'334 - Ms), 3u);
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
  EXPECT_EQ(Feed(rule, refresh, 200, 2 * Ms, false, 2), PC::SwapIntervalChange::Unchanged);
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
  EXPECT_EQ(Feed(rule, refresh, 300, 40 * Ms, true), PC::SwapIntervalChange::Unchanged);
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
  EXPECT_EQ(window.Span, Span(g_hz60.TimeFor(4).Nanoseconds() - g_hz60.TimeFor(1).Nanoseconds()));
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
  PC::SwapIntervalChange change = PC::SwapIntervalChange::Unchanged;
  int64_t frames = 0;
  while (change == PC::SwapIntervalChange::Unchanged && frames < 1'000)
  {
    ++frames;
    change = rule.AddFrame(fast.TimeFor(frames), Span(6 * Ms), true);
    if (change == PC::SwapIntervalChange::Unchanged)
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

TEST(SwapIntervalRule, OtherSettingsStartAgainWithRoomForTheirWindow)
{
  // Made for a 30 Hz display: room for twice the 62 frames of its window. Given settings for a 240 Hz display it has room for the
  // 480 frames of the window there: the window is full when its oldest frame is 2 s old, not when it holds all it could before
  PC::SwapIntervalRule rule(Settings(PC::RefreshPeriod::FromRate(30), PC::SlowDownRule::FullWindow));
  int64_t refresh = 0;
  static_cast<void>(Feed(rule, refresh, 20, 4 * Ms, false));
  EXPECT_EQ(rule.FrameWindow().Frames, 20u);

  const PC::RefreshPeriod fast = PC::RefreshPeriod::FromRate(240);
  const PC::PacerSettings settings = Settings(fast, PC::SlowDownRule::FullWindow);
  rule.SetSettings(settings);
  EXPECT_EQ(rule.Settings(), settings);
  EXPECT_EQ(rule.Refresh(), fast);
  EXPECT_EQ(rule.SwapInterval(), 1u);
  EXPECT_EQ(rule.FrameWindow().Frames, 0u);
  refresh = 0;
  static_cast<void>(Feed(rule, refresh, 400, Ms, false));
  EXPECT_EQ(rule.FrameWindow().Frames, 400u);
  EXPECT_FALSE(rule.FrameWindow().Full);
  static_cast<void>(Feed(rule, refresh, 200, Ms, false));
  EXPECT_TRUE(rule.FrameWindow().Full);
  EXPECT_EQ(rule.FrameWindow().Frames, 482u);

  // The same settings change nothing; settings that need less room keep the room there is
  rule.SetSettings(settings);
  EXPECT_EQ(rule.FrameWindow().Frames, 482u);
  PC::PacerSettings slower = settings;
  slower.SetPreferredSwapInterval(4);
  rule.SetSettings(slower);
  EXPECT_EQ(rule.SwapInterval(), 4u);
  EXPECT_EQ(rule.PreferredSwapInterval(), 4u);
  EXPECT_EQ(rule.FrameWindow().Frames, 0u);
}
