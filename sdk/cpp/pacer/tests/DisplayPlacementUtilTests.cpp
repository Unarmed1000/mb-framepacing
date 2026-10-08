// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The values a timed present is given (sdk/doc/pacer-design.md, "How a pacer is put together"): which of the two
// times a pacer gives with a capability set, and the times themselves.

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/placement/DisplayPlacementUtil.hpp>
#include <mb/framepacing/pacer/placement/PresentTiming.hpp>
#include <gtest/gtest.h>
#include <cstdint>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;
namespace Placement = MB::FramePacing::Pacer::DisplayPlacementUtil;
using PC::PacerCapabilities;
using PC::PacerCapability;
using PC::PresentTiming;

namespace
{
  // 100 Hz: a refresh period of exactly 10,000,000 ns
  constexpr int64_t Period = 10'000'000;
  constexpr int64_t Start = 1'000'000'000;

  const PC::RefreshPeriod g_hz100 = PC::RefreshPeriod::FromRate(100);
}

TEST(DisplayPlacementUtil, TheTimeBeforeWhichAFrameIsNotShownIsTakenWhereThePresentHasOne)
{
  EXPECT_EQ(Placement::TimingFor(PacerCapabilities()), PresentTiming::Untimed);
  EXPECT_EQ(Placement::TimingFor(PacerCapabilities(PacerCapability::VBlankTimes | PacerCapability::WaitForPresent)), PresentTiming::Untimed);
  EXPECT_EQ(Placement::TimingFor(PacerCapabilities(PacerCapability::PresentSwapInterval, 4)), PresentTiming::Untimed);
  EXPECT_EQ(Placement::TimingFor(PacerCapabilities(PacerCapability::PresentAfterDuration)), PresentTiming::AfterDuration);
  EXPECT_EQ(Placement::TimingFor(PacerCapabilities(PacerCapability::PresentAtTime)), PresentTiming::AtTime);
  EXPECT_EQ(Placement::TimingFor(PacerCapabilities(PacerCapability::PresentAtTime | PacerCapability::PresentAfterDuration)), PresentTiming::AtTime);
  EXPECT_EQ(Placement::TimingFor(PacerCapabilities(PacerCapability::PresentAfterDuration | PacerCapability::VBlankTimes)),
            PresentTiming::AfterDuration);
}

TEST(DisplayPlacementUtil, AFrameMayBeShownFromHalfAPeriodBeforeTheRefreshItIsFor)
{
  const FP::NanosecondTickCount intended(Start + (7 * Period));
  EXPECT_EQ(Placement::EarlyTimeFor(g_hz100).Nanoseconds(), Period / 2);
  EXPECT_EQ(Placement::NotBeforeTimeFor(intended, g_hz100), FP::NanosecondTickCount(Start + (7 * Period) - (Period / 2)));

  // The frame before it stays its swap interval's refreshes, less the same half
  EXPECT_EQ(Placement::MinimumDurationFor(1, g_hz100).Nanoseconds(), Period / 2);
  EXPECT_EQ(Placement::MinimumDurationFor(2, g_hz100).Nanoseconds(), (2 * Period) - (Period / 2));
  EXPECT_EQ(Placement::MinimumDurationFor(4, g_hz100).Nanoseconds(), (4 * Period) - (Period / 2));

  // 240 Hz, a period that is no whole number of nanoseconds (4,166,666.67): two of them are 8,333,333
  const PC::RefreshPeriod hz240 = PC::RefreshPeriod::FromRate(240);
  EXPECT_EQ(Placement::MinimumDurationFor(2, hz240).Nanoseconds(), 6'250'000);
}

TEST(DisplayPlacementUtil, ThePlanGetsTheOneValueOfItsTiming)
{
  const FP::NanosecondTickCount intended(Start + (3 * Period));

  PC::PresentPlan untimed;
  Placement::Place(untimed, PresentTiming::Untimed, intended, 2, g_hz100);
  EXPECT_EQ(untimed.NotBeforeTime, FP::NanosecondTickCount());
  EXPECT_EQ(untimed.MinimumDuration, FP::NanosecondTimeDuration());

  PC::PresentPlan atTime;
  Placement::Place(atTime, PresentTiming::AtTime, intended, 2, g_hz100);
  EXPECT_EQ(atTime.NotBeforeTime, FP::NanosecondTickCount(Start + (3 * Period) - (Period / 2)));
  EXPECT_EQ(atTime.MinimumDuration, FP::NanosecondTimeDuration());

  PC::PresentPlan afterDuration;
  Placement::Place(afterDuration, PresentTiming::AfterDuration, intended, 2, g_hz100);
  EXPECT_EQ(afterDuration.NotBeforeTime, FP::NanosecondTickCount());
  EXPECT_EQ(afterDuration.MinimumDuration.Nanoseconds(), (2 * Period) - (Period / 2));
  // Nothing else of a plan is touched
  EXPECT_EQ(afterDuration.PresentTime, FP::NanosecondTickCount());
  EXPECT_EQ(afterDuration.SwapInterval, 1u);
}

TEST(DisplayPlacementUtil, ThePresentsSwapIntervalIsTheFramesUpToTheLongestThePresentTakes)
{
  using PC::PacerCapabilities;
  using PC::PacerCapability;
  // A present that takes none: 1, whatever the frame's swap interval
  EXPECT_EQ(Placement::PresentSwapIntervalFor(PacerCapabilities(), 3), 1u);
  EXPECT_EQ(Placement::PresentSwapIntervalFor(PacerCapabilities(PacerCapability::PresentAfterDuration), 3), 1u);
  // One that takes up to four: the frame's own, and four for a frame of more (the loop holds it for the rest)
  const PacerCapabilities four(PacerCapability::PresentSwapInterval | PacerCapability::VBlankTimes, 4);
  EXPECT_EQ(Placement::PresentSwapIntervalFor(four, 1), 1u);
  EXPECT_EQ(Placement::PresentSwapIntervalFor(four, 2), 2u);
  EXPECT_EQ(Placement::PresentSwapIntervalFor(four, 4), 4u);
  EXPECT_EQ(Placement::PresentSwapIntervalFor(four, 6), 4u);
  EXPECT_EQ(Placement::PresentSwapIntervalFor(four, 0), 1u);
  // A longest one of 1 holds nothing
  EXPECT_EQ(Placement::PresentSwapIntervalFor(PacerCapabilities(PacerCapability::PresentSwapInterval, 1), 3), 1u);
  // With a time before which the frame is not shown, that time says the refresh: 1
  EXPECT_EQ(Placement::PresentSwapIntervalFor(PacerCapabilities(PacerCapability::PresentSwapInterval | PacerCapability::PresentAtTime, 4), 2), 1u);
  // With a time the frame before stays, both say the same and both are given
  EXPECT_EQ(Placement::PresentSwapIntervalFor(PacerCapabilities(PacerCapability::PresentSwapInterval | PacerCapability::PresentAfterDuration, 4), 2),
            2u);
}
