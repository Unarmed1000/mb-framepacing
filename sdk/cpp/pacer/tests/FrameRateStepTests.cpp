// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The frame rates a display can show every frame at (FrameRateStep, FrameRateStepUtil): a frame every so many refreshes, down to 20
// frames a second, each with its rate as a number to show; and what a frame rate that is no step becomes.
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/FrameRateStep.hpp>
#include <mb/framepacing/pacer/FrameRateStepUtil.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <gtest/gtest.h>
#include <cstdint>
#include <vector>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;
namespace Steps = MB::FramePacing::Pacer::FrameRateStepUtil;

namespace
{
  constexpr int64_t Ms = FP::NanosecondTimeSpan::NanosecondsPerMillisecond;

  const PC::RefreshPeriod g_hz60 = PC::RefreshPeriod::FromRate(60);
  const PC::RefreshPeriod g_ntsc = PC::RefreshPeriod::FromRate(60'000, 1'001);
  const PC::RefreshPeriod g_hz144 = PC::RefreshPeriod::FromRate(144);
  const PC::RefreshPeriod g_hz240 = PC::RefreshPeriod::FromRate(240);

  constexpr FP::NanosecondTimeSpan Span(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTimeSpan(nanoseconds);
  }

  //! The rates of a display's steps, in millihertz, as a menu lists them
  std::vector<uint32_t> Menu(const PC::RefreshPeriod refresh)
  {
    std::vector<uint32_t> rates;
    for (uint32_t swapInterval = 1; swapInterval <= Steps::StepCount(refresh); ++swapInterval)
    {
      rates.push_back(Steps::StepAt(refresh, swapInterval).RateMillihertz);
    }
    return rates;
  }
}

TEST(RefreshPeriod, GivesItsRateInMillihertz)
{
  EXPECT_EQ(g_hz60.RateMillihertz(), 60'000u);
  EXPECT_EQ(g_hz60.RateMillihertz(2), 30'000u);
  EXPECT_EQ(g_hz60.RateMillihertz(7), 8'571u);    // 8.5714
  EXPECT_EQ(g_hz60.RateMillihertz(9), 6'667u);    // 6.6667: the nearest, not cut
  // The fraction of a rate is kept: 59.94 Hz as DXGI states it, and a display mode of 240.016 Hz in whole nanoseconds
  EXPECT_EQ(g_ntsc.RateMillihertz(), 59'940u);
  EXPECT_EQ(g_ntsc.RateMillihertz(2), 29'970u);
  EXPECT_EQ(PC::RefreshPeriod::FromRate(24'000, 1'001).RateMillihertz(), 23'976u);
  EXPECT_EQ(PC::RefreshPeriod::FromNanosecondTimeSpan(Span(4'166'389)).RateMillihertz(), 240'016u);
  // The ends of the range: 10 kHz and 1 Hz, and the most refreshes there is a rate for
  EXPECT_EQ(PC::RefreshPeriod::FromRate(10'000).RateMillihertz(), 10'000'000u);
  EXPECT_EQ(PC::RefreshPeriod::FromRate(1).RateMillihertz(), 1'000u);
  EXPECT_EQ(PC::RefreshPeriod::FromRate(1).RateMillihertz(PC::RefreshPeriod::MaxRateRefreshes), 1u);
  EXPECT_EQ(PC::RefreshPeriod::FromRate(10'000).RateMillihertz(PC::RefreshPeriod::MaxRateRefreshes), 10'000u);
#ifdef NDEBUG
  // Without asserts a number of refreshes outside the range is clamped into it
  EXPECT_EQ(g_hz60.RateMillihertz(0), 60'000u);
  EXPECT_EQ(g_hz60.RateMillihertz(1'001), 60u);
#elif GTEST_HAS_DEATH_TEST
  EXPECT_DEATH(static_cast<void>(g_hz60.RateMillihertz(0)), "");
  EXPECT_DEATH(static_cast<void>(g_hz60.RateMillihertz(1'001)), "");
#endif
}

TEST(FrameRateSteps, ADisplaysStepsAreAFrameEverySoManyRefreshesDownToTwentyFramesASecond)
{
  EXPECT_EQ(Menu(g_hz60), (std::vector<uint32_t>{60'000, 30'000, 20'000}));
  EXPECT_EQ(Menu(PC::RefreshPeriod::FromRate(50)), (std::vector<uint32_t>{50'000, 25'000}));
  EXPECT_EQ(Menu(PC::RefreshPeriod::FromRate(120)), (std::vector<uint32_t>{120'000, 60'000, 40'000, 30'000, 24'000, 20'000}));
  EXPECT_EQ(Menu(g_hz144), (std::vector<uint32_t>{144'000, 72'000, 48'000, 36'000, 28'800, 24'000, 20'571}));
  EXPECT_EQ(Menu(g_hz240), (std::vector<uint32_t>{240'000, 120'000, 80'000, 60'000, 48'000, 40'000, 34'286, 30'000, 26'667, 24'000, 21'818, 20'000}));
  // The slack the pacer judges a frame rate with: every third refresh of 59.94 Hz, 19.98 frames a second, is its last step
  EXPECT_EQ(Menu(g_ntsc), (std::vector<uint32_t>{59'940, 29'970, 19'980}));
  // A display slower than 20 Hz has its own rate and nothing else; a fast one no more steps than there are swap intervals
  EXPECT_EQ(Menu(PC::RefreshPeriod::FromRate(15)), (std::vector<uint32_t>{15'000}));
  EXPECT_EQ(Menu(PC::RefreshPeriod::FromRate(1)), (std::vector<uint32_t>{1'000}));
  EXPECT_EQ(Steps::StepCount(PC::RefreshPeriod::FromRate(10'000)), PC::PacerSettings::MaxSwapInterval);
  EXPECT_EQ(Steps::SlowestFrameTime, Span(50 * Ms));
}

TEST(FrameRateSteps, AStepIsWhatTheSettingsTake)
{
  const PC::FrameRateStep step = Steps::StepAt(g_hz240, 4);
  EXPECT_EQ(step.SwapInterval, 4u);
  EXPECT_EQ(step.FrameTime, Span(16'666'667));
  EXPECT_EQ(step.RateMillihertz, 60'000u);
  EXPECT_EQ(step, (PC::FrameRateStep{4u, g_hz240.TimeFor(4), 60'000u}));
  EXPECT_NE(step, Steps::StepAt(g_hz240, 5));
  EXPECT_EQ(PC::FrameRateStep(), (PC::FrameRateStep{1u, Span(0), 0u}));

  // Either of the two gives the settings that step, and the pacer its swap interval
  PC::PacerSettings byFrameTime(g_hz240);
  byFrameTime.SetPreferredFrameTime(step.FrameTime);
  EXPECT_EQ(byFrameTime.PreferredSwapIntervalAt(g_hz240), 4u);
  PC::PacerSettings bySwapInterval(g_hz240);
  bySwapInterval.SetPreferredSwapInterval(step.SwapInterval);
  EXPECT_EQ(bySwapInterval.PreferredSwapIntervalAt(g_hz240), 4u);

  // Every step of every display is a step, and is what its own frame time becomes
  for (const PC::RefreshPeriod refresh : {g_hz60, g_ntsc, g_hz144, g_hz240, PC::RefreshPeriod::FromRate(540), PC::RefreshPeriod::FromRate(15)})
  {
    for (uint32_t swapInterval = 1; swapInterval <= Steps::StepCount(refresh); ++swapInterval)
    {
      const PC::FrameRateStep listed = Steps::StepAt(refresh, swapInterval);
      EXPECT_TRUE(Steps::IsStep(refresh, listed.FrameTime)) << swapInterval;
      EXPECT_EQ(Steps::StepFor(refresh, listed.FrameTime), listed) << swapInterval;
    }
    // And the one after the last is not: it is slower than 20 frames a second
    const PC::FrameRateStep beyond = Steps::StepAt(refresh, Steps::StepCount(refresh) + 1u);
    EXPECT_FALSE(Steps::IsStep(refresh, beyond.FrameTime));
    EXPECT_EQ(Steps::StepFor(refresh, beyond.FrameTime), beyond);
  }
#ifdef NDEBUG
  // Without asserts a swap interval outside the range is clamped into it
  EXPECT_EQ(Steps::StepAt(g_hz60, 0), Steps::StepAt(g_hz60, 1));
  EXPECT_EQ(Steps::StepAt(g_hz60, 101).SwapInterval, PC::PacerSettings::MaxSwapInterval);
#elif GTEST_HAS_DEATH_TEST
  EXPECT_DEATH(static_cast<void>(Steps::StepAt(g_hz60, 0)), "");
  EXPECT_DEATH(static_cast<void>(Steps::StepAt(g_hz60, 101)), "");
#endif
}

TEST(FrameRateSteps, AFrameRateThatIsNoStepBecomesTheNextSlowerOne)
{
  // 50 frames a second can not be hit at 60 Hz: no whole number of refreshes is 20 ms. The pacer makes it 30
  EXPECT_FALSE(Steps::IsStepRate(g_hz60, 50));
  EXPECT_EQ(Steps::StepForRate(g_hz60, 50), Steps::StepAt(g_hz60, 2));
  EXPECT_FALSE(Steps::IsStep(g_hz60, Span(20 * Ms)));
  EXPECT_EQ(Steps::StepFor(g_hz60, Span(20 * Ms)).RateMillihertz, 30'000u);
  // 24 frames a second: none at 60 Hz (it becomes 20), one at 120, 144 and 240 Hz
  EXPECT_FALSE(Steps::IsStepRate(g_hz60, 24));
  EXPECT_EQ(Steps::StepForRate(g_hz60, 24).RateMillihertz, 20'000u);
  EXPECT_TRUE(Steps::IsStepRate(PC::RefreshPeriod::FromRate(120), 24));
  EXPECT_TRUE(Steps::IsStepRate(g_hz144, 24));
  EXPECT_EQ(Steps::StepForRate(g_hz240, 24).SwapInterval, 10u);
  // Faster than the display is no step: it becomes the display's rate
  EXPECT_FALSE(Steps::IsStepRate(g_hz60, 120));
  EXPECT_EQ(Steps::StepForRate(g_hz60, 120), Steps::StepAt(g_hz60, 1));
  // Within the slack a rate is the step: 60 at 59.94 Hz, 30 at 59.94 Hz, 29.97 at 60 Hz
  EXPECT_TRUE(Steps::IsStepRate(g_ntsc, 60));
  EXPECT_TRUE(Steps::IsStepRate(g_ntsc, 30));
  EXPECT_EQ(Steps::StepForRate(g_ntsc, 30).RateMillihertz, 29'970u);
  EXPECT_TRUE(Steps::IsStepRate(g_hz60, 30'000, 1'001));
  EXPECT_TRUE(Steps::IsStepRate(g_hz60, 60));
  EXPECT_TRUE(Steps::IsStepRate(g_hz60, 30));
  EXPECT_TRUE(Steps::IsStepRate(g_hz60, 20));
  // Slower than 20 frames a second is no step, though the pacer paces at it when it is asked for
  EXPECT_FALSE(Steps::IsStepRate(g_hz60, 15));
  EXPECT_EQ(Steps::StepForRate(g_hz60, 15), Steps::StepAt(g_hz60, 4));
  EXPECT_FALSE(Steps::IsStepRate(g_hz60, 10));
}

TEST(FrameRateSteps, NoFrameRateIsTheDisplaysOwn)
{
  EXPECT_EQ(Steps::StepFor(g_hz144, Span(0)), Steps::StepAt(g_hz144, 1));
  EXPECT_EQ(Steps::StepForRate(g_hz144, 0), Steps::StepAt(g_hz144, 1));
  EXPECT_EQ(Steps::StepForRate(g_hz144, 60, 0), Steps::StepAt(g_hz144, 1));
  // And no step: there is nothing to hit
  EXPECT_FALSE(Steps::IsStep(g_hz144, Span(0)));
  EXPECT_FALSE(Steps::IsStepRate(g_hz144, 0));
  EXPECT_FALSE(Steps::IsStepRate(g_hz144, 60, 0));
  // A frame time outside what the settings take is taken as the nearest they do: none, and the longest
  EXPECT_EQ(Steps::StepFor(g_hz144, Span(-5)), Steps::StepAt(g_hz144, 1));
  EXPECT_FALSE(Steps::IsStep(g_hz144, Span(-5)));
  EXPECT_EQ(Steps::StepFor(g_hz144, FP::NanosecondTimeSpan::MaxValue()).SwapInterval, PC::PacerSettings::MaxSwapInterval);
  EXPECT_FALSE(Steps::IsStep(g_hz144, FP::NanosecondTimeSpan::MaxValue()));
  EXPECT_FALSE(Steps::IsStep(g_hz144, FP::NanosecondTimeSpan::MinValue()));
}
