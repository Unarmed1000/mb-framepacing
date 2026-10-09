// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The capabilities an application gives the pacer, and the tiers as sets of them (sdk/doc/pacer-design.md): one list of tiers, a
// pacer for each. A set reaches a tier when it has what the tier needs, and its rating is the best tier it reaches. Every set
// there is is rated here, and no set is rated below a set it contains.
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerMajorTier.hpp>
#include <mb/framepacing/pacer/capability/PacerRating.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <mb/framepacing/pacer/capability/PacerTierText.hpp>
#include <mb/framepacing/pacer/capability/PacerTierUtil.hpp>
#include <gtest/gtest.h>
#include <array>
#include <cstdint>
#include <cstring>
#include <set>
#include <string_view>

namespace PC = MB::FramePacing::Pacer;
using PC::PacerCapabilities;
using PC::PacerCapability;
using PC::PacerRating;
using PC::PacerTier;

namespace
{
  constexpr std::array<PacerCapability, 16> Each = {
    PacerCapability::PresentSwapInterval,  PacerCapability::PresentAtTime,        PacerCapability::PresentAfterDuration,
    PacerCapability::PresentWaits,         PacerCapability::PresentReturnsAtOnce, PacerCapability::AcquireWaits,
    PacerCapability::AcquireReturnsAtOnce, PacerCapability::WaitForPresent,       PacerCapability::WaitForGpuWork,
    PacerCapability::WaitForImage,         PacerCapability::FrameCallback,        PacerCapability::VBlankTimes,
    PacerCapability::GpuWorkTimes,         PacerCapability::GpuWorkDurations,     PacerCapability::DisplayTimes,
    PacerCapability::PresentSkipsOverdue,
  };

  constexpr PacerCapability PresentPair = PacerCapability::PresentWaits | PacerCapability::PresentReturnsAtOnce;
  constexpr PacerCapability AcquirePair = PacerCapability::AcquireWaits | PacerCapability::AcquireReturnsAtOnce;

  //! Whether the bits are a set an application can have: not both of a pair that excludes itself
  constexpr bool IsValid(const PacerCapability capabilities) noexcept
  {
    return !PC::HasCapability(capabilities, PresentPair) && !PC::HasCapability(capabilities, AcquirePair);
  }

  constexpr uint32_t Number(const PacerTier tier) noexcept
  {
    return static_cast<uint32_t>(tier);
  }

  constexpr std::array<PacerTier, 12> EveryTier = {PacerTier::TimedSkipVBlankWaitForPresent, PacerTier::TimedSkipTimerWaitForPresent,
                                                   PacerTier::TimedSkipVBlankPeriodOnly,     PacerTier::TimedSkipTimerPeriodOnly,
                                                   PacerTier::TimedVBlankWaitForPresent,     PacerTier::TimedTimerWaitForPresent,
                                                   PacerTier::TimedVBlankPeriodOnly,         PacerTier::TimedTimerPeriodOnly,
                                                   PacerTier::VBlankWaitForPresent,          PacerTier::VBlankPeriodOnly,
                                                   PacerTier::TimerWaitForPresent,           PacerTier::TimerPeriodOnly};

  // The one present that places a frame, and what makes its display's side the better one
  constexpr PacerCapability TimedPresent = PacerCapability::PresentAtTime;
  constexpr PacerCapability Skips = PacerCapability::PresentSkipsOverdue;
  constexpr PacerCapability EveryRaise = TimedPresent | PacerCapability::WaitForPresent | PacerCapability::VBlankTimes;

  // The rating is usable where a constant is needed
  static_assert(PC::PacerTierUtil::Rate(PacerCapabilities()).Tier == PacerTier::TimerPeriodOnly);
  static_assert(PC::PacerTierUtil::Rate(PacerCapabilities(PacerCapability::VBlankTimes | PacerCapability::WaitForPresent)) ==
                PacerRating{PacerTier::VBlankWaitForPresent, TimedPresent, false, false});
}

TEST(PacerCapability, EveryCapabilityIsABitOfItsOwnAndAllOfThemAreAllCapabilities)
{
  PacerCapability all = PacerCapability::NoCapabilities;
  for (const PacerCapability capability : Each)
  {
    const auto bits = static_cast<uint32_t>(capability);
    EXPECT_NE(bits, 0u);
    EXPECT_EQ(bits & (bits - 1u), 0u);
    EXPECT_FALSE(PC::HasCapability(all, capability));
    all = all | capability;
  }
  EXPECT_EQ(all, PacerCapability::AllCapabilities);
}

TEST(PacerCapability, CapabilitiesCombineAndComeApart)
{
  const PacerCapability two = PacerCapability::VBlankTimes | PacerCapability::DisplayTimes;

  EXPECT_TRUE(PC::HasCapability(two, PacerCapability::VBlankTimes));
  EXPECT_TRUE(PC::HasCapability(two, two));
  EXPECT_FALSE(PC::HasCapability(two, PacerCapability::WaitForPresent));
  EXPECT_FALSE(PC::HasCapability(two, two | PacerCapability::WaitForPresent));
  // Every set has no capability
  EXPECT_TRUE(PC::HasCapability(two, PacerCapability::NoCapabilities));
  EXPECT_EQ(two & PacerCapability::DisplayTimes, PacerCapability::DisplayTimes);
  EXPECT_EQ(two & PacerCapability::WaitForPresent, PacerCapability::NoCapabilities);
  EXPECT_EQ(PC::Without(two, PacerCapability::VBlankTimes), PacerCapability::DisplayTimes);
  EXPECT_EQ(PC::Without(two, PacerCapability::WaitForPresent), two);
  EXPECT_EQ(PC::Without(two, two), PacerCapability::NoCapabilities);
}

TEST(PacerCapabilities, TheBaselineHasNoCapability)
{
  const PacerCapabilities baseline;

  EXPECT_EQ(baseline.Capabilities(), PacerCapability::NoCapabilities);
  EXPECT_EQ(baseline.MaxPresentSwapInterval(), 0u);
  EXPECT_EQ(baseline, PacerCapabilities(PacerCapability::NoCapabilities));
  for (const PacerCapability capability : Each)
  {
    EXPECT_FALSE(baseline.Has(capability));
  }
}

TEST(PacerCapabilities, ASetKeepsItsCapabilitiesAndASwapIntervalOnlyWithAPresentThatTakesOne)
{
  const PacerCapabilities set(PacerCapability::PresentSwapInterval | PacerCapability::PresentWaits | PacerCapability::DisplayTimes, 4);

  EXPECT_TRUE(set.Has(PacerCapability::PresentSwapInterval));
  EXPECT_TRUE(set.Has(PacerCapability::PresentWaits | PacerCapability::DisplayTimes));
  EXPECT_FALSE(set.Has(PacerCapability::VBlankTimes));
  EXPECT_FALSE(set.Has(PacerCapability::DisplayTimes | PacerCapability::VBlankTimes));
  EXPECT_EQ(set.MaxPresentSwapInterval(), 4u);
  // The longest swap interval is 1 unless said
  EXPECT_EQ(PacerCapabilities(PacerCapability::PresentSwapInterval).MaxPresentSwapInterval(), 1u);
  // Without a present that takes one it has no meaning and is not kept, so two such sets are the same set
  EXPECT_EQ(PacerCapabilities(PacerCapability::DisplayTimes, 4).MaxPresentSwapInterval(), 0u);
  EXPECT_EQ(PacerCapabilities(PacerCapability::DisplayTimes, 4), PacerCapabilities(PacerCapability::DisplayTimes));
  EXPECT_NE(set, PacerCapabilities(set.Capabilities(), 3));
}

TEST(PacerCapabilities, ASetContainsTheSetsThatHaveNoMoreThanItHas)
{
  const PacerCapabilities has(PacerCapability::PresentSwapInterval | PacerCapability::VBlankTimes | PacerCapability::DisplayTimes, 3);

  EXPECT_TRUE(has.Contains(has));
  EXPECT_TRUE(has.Contains(PacerCapabilities()));
  EXPECT_TRUE(has.Contains(PacerCapabilities(PacerCapability::VBlankTimes)));
  EXPECT_TRUE(has.Contains(PacerCapabilities(PacerCapability::PresentSwapInterval | PacerCapability::DisplayTimes, 2)));
  // A capability more, or a longer swap interval, is not contained
  EXPECT_FALSE(has.Contains(PacerCapabilities(PacerCapability::VBlankTimes | PacerCapability::WaitForPresent)));
  EXPECT_FALSE(has.Contains(PacerCapabilities(PacerCapability::PresentSwapInterval, 4)));
  EXPECT_FALSE(PacerCapabilities().Contains(has));
}

TEST(PacerCapabilities, TakingACapabilityOutGivesTheActiveSet)
{
  const PacerCapabilities has(PacerCapability::PresentSwapInterval | PacerCapability::PresentAfterDuration | PacerCapability::VBlankTimes, 3);

  const PacerCapabilities noTimedPresent = has.Without(PacerCapability::PresentAfterDuration);
  EXPECT_EQ(noTimedPresent, PacerCapabilities(PacerCapability::PresentSwapInterval | PacerCapability::VBlankTimes, 3));
  EXPECT_TRUE(has.Contains(noTimedPresent));
  // Without the present's swap interval its longest one goes too
  const PacerCapabilities noSwapInterval = has.Without(PacerCapability::PresentSwapInterval);
  EXPECT_EQ(noSwapInterval, PacerCapabilities(PacerCapability::PresentAfterDuration | PacerCapability::VBlankTimes));
  EXPECT_EQ(noSwapInterval.MaxPresentSwapInterval(), 0u);
  // A capability the set does not have changes nothing, and everything out is the baseline
  EXPECT_EQ(has.Without(PacerCapability::DisplayTimes), has);
  EXPECT_EQ(has.Without(PacerCapability::AllCapabilities), PacerCapabilities());
  EXPECT_EQ(PacerCapabilities().Without(PacerCapability::VBlankTimes), PacerCapabilities());
}

TEST(PacerCapabilities, WhatTwoSetsBothHaveIsASet)
{
  const PacerCapabilities has(PacerCapability::PresentSwapInterval | PacerCapability::VBlankTimes | PacerCapability::DisplayTimes, 3);
  const PacerCapabilities active(PacerCapability::PresentSwapInterval | PacerCapability::DisplayTimes | PacerCapability::WaitForPresent, 5);

  const PacerCapabilities both = has.IntersectedWith(active);
  EXPECT_EQ(both, PacerCapabilities(PacerCapability::PresentSwapInterval | PacerCapability::DisplayTimes, 3));
  EXPECT_EQ(active.IntersectedWith(has), both);
  EXPECT_TRUE(has.Contains(both));
  EXPECT_TRUE(active.Contains(both));
  // Nothing in common is the baseline, and a set with itself is itself
  EXPECT_EQ(has.IntersectedWith(PacerCapabilities(PacerCapability::WaitForPresent)), PacerCapabilities());
  EXPECT_EQ(has.IntersectedWith(has), has);
  EXPECT_EQ(has.IntersectedWith(PacerCapabilities()), PacerCapabilities());
}

TEST(PacerCapabilities, ASetThatCanNotBeIsAssertedAndMadeValidWithoutAsserts)
{
  const auto unknownBit = static_cast<PacerCapability>(1u << 20u);
#ifdef NDEBUG
  // Both of a pair that excludes itself: neither, which is "the call may wait"
  EXPECT_EQ(PacerCapabilities(PresentPair | PacerCapability::VBlankTimes), PacerCapabilities(PacerCapability::VBlankTimes));
  EXPECT_EQ(PacerCapabilities(AcquirePair | PacerCapability::PresentWaits), PacerCapabilities(PacerCapability::PresentWaits));
  EXPECT_EQ(PacerCapabilities(PresentPair | AcquirePair), PacerCapabilities());
  // A bit without a meaning is dropped
  EXPECT_EQ(PacerCapabilities(unknownBit | PacerCapability::DisplayTimes), PacerCapabilities(PacerCapability::DisplayTimes));
  // A present that takes a swap interval takes one of 1 at least
  EXPECT_EQ(PacerCapabilities(PacerCapability::PresentSwapInterval, 0).MaxPresentSwapInterval(), 1u);
#elif GTEST_HAS_DEATH_TEST
  EXPECT_DEATH(static_cast<void>(PacerCapabilities(PresentPair)), "");
  EXPECT_DEATH(static_cast<void>(PacerCapabilities(AcquirePair)), "");
  EXPECT_DEATH(static_cast<void>(PacerCapabilities(unknownBit)), "");
  EXPECT_DEATH(static_cast<void>(PacerCapabilities(PacerCapability::PresentSwapInterval, 0)), "");
#else
  GTEST_SKIP() << "asserts are on and death tests are not available";
#endif
}

TEST(PacerTier, ATierIsItsCapabilitySet)
{
  using PC::PacerTierUtil::Reaches;
  const PacerCapability wait = PacerCapability::WaitForPresent;
  const PacerCapability vblank = PacerCapability::VBlankTimes;
  // Every set reaches the baseline: a timer and the refresh period only
  EXPECT_TRUE(Reaches(PacerCapabilities(), PacerTier::TimerPeriodOnly));
  // (every capability a set can have at once: of the two pairs that exclude themselves, neither)
  EXPECT_TRUE(Reaches(PacerCapabilities(PC::Without(PacerCapability::AllCapabilities, PresentPair | AcquirePair), 8), PacerTier::TimerPeriodOnly));
  // A wait for a present, and nothing else, reaches the tier of the timer with a wait
  EXPECT_TRUE(Reaches(PacerCapabilities(wait), PacerTier::TimerWaitForPresent));
  EXPECT_FALSE(Reaches(PacerCapabilities(), PacerTier::TimerWaitForPresent));
  EXPECT_FALSE(Reaches(PacerCapabilities(vblank), PacerTier::TimerWaitForPresent));
  // Vertical blank times, and nothing else, reach the tier of the loop that knows where the refreshes are
  EXPECT_TRUE(Reaches(PacerCapabilities(vblank), PacerTier::VBlankPeriodOnly));
  EXPECT_FALSE(Reaches(PacerCapabilities(), PacerTier::VBlankPeriodOnly));
  EXPECT_FALSE(Reaches(PacerCapabilities(PacerCapability::DisplayTimes | wait), PacerTier::VBlankPeriodOnly));
  // Both: the best tier without a timed present
  EXPECT_TRUE(Reaches(PacerCapabilities(vblank | wait), PacerTier::VBlankWaitForPresent));
  EXPECT_FALSE(Reaches(PacerCapabilities(vblank), PacerTier::VBlankWaitForPresent));
  EXPECT_FALSE(Reaches(PacerCapabilities(wait), PacerTier::VBlankWaitForPresent));

  // A present that takes a time before which the frame is not shown, and nothing else, reaches the lowest of the four tiers
  // above those: with a time the frame before it stays as well, or without
  for (const PacerCapability timed : {PacerCapability::PresentAtTime, PacerCapability::PresentAtTime | PacerCapability::PresentAfterDuration})
  {
    EXPECT_TRUE(PC::PacerTierUtil::HasTimedPresent(PacerCapabilities(timed)));
    EXPECT_TRUE(Reaches(PacerCapabilities(timed), PacerTier::TimedTimerPeriodOnly));
    EXPECT_FALSE(Reaches(PacerCapabilities(timed), PacerTier::TimedVBlankPeriodOnly));
    EXPECT_FALSE(Reaches(PacerCapabilities(timed), PacerTier::TimedTimerWaitForPresent));
    EXPECT_FALSE(Reaches(PacerCapabilities(timed), PacerTier::TimedVBlankWaitForPresent));
    // With vertical blank times, with a wait for a present, and with both
    EXPECT_TRUE(Reaches(PacerCapabilities(timed | vblank), PacerTier::TimedVBlankPeriodOnly));
    EXPECT_FALSE(Reaches(PacerCapabilities(timed | vblank), PacerTier::TimedTimerWaitForPresent));
    EXPECT_TRUE(Reaches(PacerCapabilities(timed | wait), PacerTier::TimedTimerWaitForPresent));
    EXPECT_FALSE(Reaches(PacerCapabilities(timed | wait), PacerTier::TimedVBlankPeriodOnly));
    EXPECT_FALSE(Reaches(PacerCapabilities(timed | wait), PacerTier::TimedVBlankWaitForPresent));
    EXPECT_TRUE(Reaches(PacerCapabilities(timed | vblank | wait), PacerTier::TimedVBlankWaitForPresent));
    // And none of the four of a display that skips
    EXPECT_FALSE(PC::PacerTierUtil::SkipsOverduePresents(PacerCapabilities(timed | vblank | wait)));
    EXPECT_FALSE(Reaches(PacerCapabilities(timed | vblank | wait), PacerTier::TimedSkipTimerPeriodOnly));

    // A display's side that leaves out a frame that is overdue: the four above those, by the same two capabilities
    EXPECT_TRUE(PC::PacerTierUtil::SkipsOverduePresents(PacerCapabilities(timed | Skips)));
    EXPECT_TRUE(Reaches(PacerCapabilities(timed | Skips), PacerTier::TimedSkipTimerPeriodOnly));
    EXPECT_FALSE(Reaches(PacerCapabilities(timed | Skips), PacerTier::TimedSkipVBlankPeriodOnly));
    EXPECT_FALSE(Reaches(PacerCapabilities(timed | Skips), PacerTier::TimedSkipTimerWaitForPresent));
    EXPECT_TRUE(Reaches(PacerCapabilities(timed | Skips | vblank), PacerTier::TimedSkipVBlankPeriodOnly));
    EXPECT_FALSE(Reaches(PacerCapabilities(timed | Skips | vblank), PacerTier::TimedSkipTimerWaitForPresent));
    EXPECT_TRUE(Reaches(PacerCapabilities(timed | Skips | wait), PacerTier::TimedSkipTimerWaitForPresent));
    EXPECT_FALSE(Reaches(PacerCapabilities(timed | Skips | wait), PacerTier::TimedSkipVBlankWaitForPresent));
    EXPECT_TRUE(Reaches(PacerCapabilities(timed | Skips | vblank | wait), PacerTier::TimedSkipVBlankWaitForPresent));
    // It reaches the tiers of a display that shows every frame as well: its rating is the best of them
    EXPECT_TRUE(Reaches(PacerCapabilities(timed | Skips), PacerTier::TimedTimerPeriodOnly));
  }
  // A time the frame before it stays places no frame: alone it reaches nothing above the tiers of the loop, and neither does
  // a display's side that skips without a time on the present, as nothing is due there
  for (const PacerCapability alone : {PacerCapability::PresentAfterDuration, Skips, PacerCapability::PresentAfterDuration | Skips})
  {
    const PacerCapabilities set(alone | vblank | wait);
    EXPECT_FALSE(PC::PacerTierUtil::HasTimedPresent(set));
    EXPECT_FALSE(PC::PacerTierUtil::SkipsOverduePresents(set));
    EXPECT_FALSE(Reaches(set, PacerTier::TimedTimerPeriodOnly));
    EXPECT_FALSE(Reaches(set, PacerTier::TimedSkipTimerPeriodOnly));
    EXPECT_TRUE(Reaches(set, PacerTier::VBlankWaitForPresent));
  }
  // Without a timed present no set reaches them, whatever else it has
  const PacerCapabilities untimed(PC::Without(PacerCapability::AllCapabilities, TimedPresent | PresentPair | AcquirePair), 8);
  EXPECT_FALSE(PC::PacerTierUtil::HasTimedPresent(untimed));
  EXPECT_FALSE(Reaches(untimed, PacerTier::TimedTimerPeriodOnly));
  EXPECT_FALSE(Reaches(untimed, PacerTier::TimedSkipTimerPeriodOnly));
  EXPECT_TRUE(Reaches(untimed, PacerTier::VBlankWaitForPresent));

  // What a present's swap interval or minimum duration holds, what slows a loop when its queue is full, what only measures, a
  // wait for the GPU's work and display times reach no tier above the baseline
  const PacerCapabilities noTier(PacerCapability::PresentSwapInterval | PacerCapability::PresentAfterDuration | PacerCapability::WaitForImage |
                                   PacerCapability::FrameCallback | PacerCapability::PresentWaits | PacerCapability::AcquireWaits |
                                   PacerCapability::WaitForGpuWork | PacerCapability::GpuWorkTimes | PacerCapability::GpuWorkDurations |
                                   PacerCapability::DisplayTimes,
                                 4);
  for (const PacerTier tier : EveryTier)
  {
    EXPECT_EQ(Reaches(noTier, tier), tier == PacerTier::TimerPeriodOnly) << Number(tier);
  }
  // A value that is no tier is reached by nothing better than every set reaches
  EXPECT_TRUE(Reaches(PacerCapabilities(), static_cast<PacerTier>(13)));
}

TEST(PacerTier, TheOrderIsWhoPlacesTheFrameThenWhatHoldsTheLoopThenWhereTheRefreshesAre)
{
  using PC::PacerTierUtil::Rate;
  const PacerCapability timed = PacerCapability::PresentAtTime;
  const PacerCapability wait = PacerCapability::WaitForPresent;
  const PacerCapability vblank = PacerCapability::VBlankTimes;
  // A display's side that places a frame and skips one that is overdue is above everything else
  EXPECT_EQ(Rate(PacerCapabilities(timed | Skips | wait | vblank)).Tier, PacerTier::TimedSkipVBlankWaitForPresent);
  EXPECT_EQ(Rate(PacerCapabilities(timed | Skips | wait)).Tier, PacerTier::TimedSkipTimerWaitForPresent);
  EXPECT_EQ(Rate(PacerCapabilities(timed | Skips | vblank)).Tier, PacerTier::TimedSkipVBlankPeriodOnly);
  EXPECT_EQ(Rate(PacerCapabilities(timed | Skips)).Tier, PacerTier::TimedSkipTimerPeriodOnly);
  // A time on the present by itself is above everything without one
  EXPECT_EQ(Rate(PacerCapabilities(timed | wait | vblank)).Tier, PacerTier::TimedVBlankWaitForPresent);
  EXPECT_EQ(Rate(PacerCapabilities(timed | wait)).Tier, PacerTier::TimedTimerWaitForPresent);
  EXPECT_EQ(Rate(PacerCapabilities(timed | vblank)).Tier, PacerTier::TimedVBlankPeriodOnly);
  EXPECT_EQ(Rate(PacerCapabilities(timed)).Tier, PacerTier::TimedTimerPeriodOnly);
  // Without one the vertical blank times come before the wait: they are what makes the loop's placing good
  EXPECT_EQ(Rate(PacerCapabilities(wait | vblank)).Tier, PacerTier::VBlankWaitForPresent);
  EXPECT_EQ(Rate(PacerCapabilities(vblank)).Tier, PacerTier::VBlankPeriodOnly);
  EXPECT_EQ(Rate(PacerCapabilities(wait)).Tier, PacerTier::TimerWaitForPresent);
  EXPECT_EQ(Rate(PacerCapabilities()).Tier, PacerTier::TimerPeriodOnly);
  // A time the frame before it stays changes none of them
  EXPECT_EQ(Rate(PacerCapabilities(PacerCapability::PresentAfterDuration | wait | vblank)).Tier, PacerTier::VBlankWaitForPresent);
  EXPECT_EQ(Rate(PacerCapabilities(PacerCapability::PresentAfterDuration)).Tier, PacerTier::TimerPeriodOnly);
  EXPECT_EQ(Rate(PacerCapabilities(PacerCapability::PresentAfterDuration | timed | wait)).Tier, PacerTier::TimedTimerWaitForPresent);
  // The numbers are 1 to 12 in that order
  for (uint32_t index = 0; index < EveryTier.size(); ++index)
  {
    EXPECT_EQ(Number(EveryTier[index]), index + 1u);
  }
}

TEST(PacerTier, ATierIsAMajorTierAndASubTier)
{
  using PC::PacerMajorTier;
  using PC::PacerTierUtil::HasPacer;
  using PC::PacerTierUtil::MajorOf;
  using PC::PacerTierUtil::PacedAs;
  using PC::PacerTierUtil::SubTierOf;
  static_assert(PC::PacerTierUtil::SubTiersPerMajorTier == 4u);
  static_assert(MajorOf(PacerTier::VBlankWaitForPresent) == PacerMajorTier::LoopPlaces);
  static_assert(SubTierOf(PacerTier::VBlankWaitForPresent) == 1u);

  // Three major tiers of four sub tiers, in the list's order: who places the frame, and the rank inside that
  for (uint32_t index = 0; index < EveryTier.size(); ++index)
  {
    const PacerTier tier = EveryTier[index];
    EXPECT_EQ(static_cast<uint32_t>(MajorOf(tier)), (index / 4u) + 1u) << index;
    EXPECT_EQ(SubTierOf(tier), (index % 4u) + 1u) << index;
    // A pacer is built for the two major tiers below the first, and a tier of the first is paced as the same sub tier of
    // the second
    EXPECT_EQ(HasPacer(tier), index >= 4u) << index;
    EXPECT_EQ(PacedAs(tier), index >= 4u ? tier : EveryTier[index + 4u]) << index;
    EXPECT_TRUE(HasPacer(PacedAs(tier))) << index;
    EXPECT_EQ(SubTierOf(PacedAs(tier)), SubTierOf(tier)) << index;
  }
  EXPECT_EQ(MajorOf(PacerTier::TimedSkipTimerPeriodOnly), PacerMajorTier::DisplayPlacesAndSkips);
  EXPECT_EQ(MajorOf(PacerTier::TimedVBlankWaitForPresent), PacerMajorTier::DisplayPlaces);
  EXPECT_EQ(MajorOf(PacerTier::TimerPeriodOnly), PacerMajorTier::LoopPlaces);
  // The sub tiers of the display's side: the wait first. Of the loop: the vertical blank times first
  EXPECT_EQ(SubTierOf(PacerTier::TimedTimerWaitForPresent), 2u);
  EXPECT_EQ(SubTierOf(PacerTier::TimedVBlankPeriodOnly), 3u);
  EXPECT_EQ(SubTierOf(PacerTier::VBlankPeriodOnly), 2u);
  EXPECT_EQ(SubTierOf(PacerTier::TimerWaitForPresent), 3u);
  // A value that is no tier is taken as the tier next to it
  EXPECT_EQ(MajorOf(static_cast<PacerTier>(0)), PacerMajorTier::DisplayPlacesAndSkips);
  EXPECT_EQ(SubTierOf(static_cast<PacerTier>(0)), 1u);
  EXPECT_EQ(MajorOf(static_cast<PacerTier>(13)), PacerMajorTier::LoopPlaces);
  EXPECT_EQ(SubTierOf(static_cast<PacerTier>(13)), 4u);
}

TEST(PacerTier, TheDisplaysSideHoldsAFrameInThreeWays)
{
  using PC::PacerTierUtil::DisplaySideHolds;
  using PC::PacerTierUtil::Rate;
  EXPECT_TRUE(DisplaySideHolds(PacerCapabilities(PacerCapability::PresentAtTime)));
  EXPECT_TRUE(DisplaySideHolds(PacerCapabilities(PacerCapability::PresentAfterDuration)));
  EXPECT_TRUE(DisplaySideHolds(PacerCapabilities(PacerCapability::PresentSwapInterval, 2)));
  // A present whose longest swap interval is 1 holds no frame for more than one refresh
  EXPECT_FALSE(DisplaySideHolds(PacerCapabilities(PacerCapability::PresentSwapInterval, 1)));
  EXPECT_FALSE(DisplaySideHolds(PacerCapabilities(PacerCapability::VBlankTimes | PacerCapability::WaitForPresent)));
  EXPECT_FALSE(DisplaySideHolds(PacerCapabilities()));
  static_assert(PC::PacerTierUtil::MinHoldingSwapInterval == 2u);

  // A swap interval on the present is in the rating beside the tier, and changes no tier
  EXPECT_EQ(Rate(PacerCapabilities(PacerCapability::PresentSwapInterval, 4)), (PacerRating{PacerTier::TimerPeriodOnly, EveryRaise, true, false}));
  EXPECT_EQ(Rate(PacerCapabilities(PacerCapability::PresentSwapInterval | PacerCapability::VBlankTimes, 4)),
            (PacerRating{PacerTier::VBlankPeriodOnly, TimedPresent | PacerCapability::WaitForPresent, true, false}));
  // So is a time the frame before it stays
  EXPECT_EQ(Rate(PacerCapabilities(PacerCapability::PresentAfterDuration | PacerCapability::VBlankTimes)),
            (PacerRating{PacerTier::VBlankPeriodOnly, TimedPresent | PacerCapability::WaitForPresent, true, false}));
  // A time before which a frame is not shown holds too, and that one does change the tier
  EXPECT_EQ(Rate(PacerCapabilities(PacerCapability::PresentAtTime | PacerCapability::VBlankTimes)),
            (PacerRating{PacerTier::TimedVBlankPeriodOnly, Skips | PacerCapability::WaitForPresent, true, false}));
}

TEST(PacerTier, ARatingIsTheBestTierWhatWouldRaiseItAndWhetherDisplayTimesAreReported)
{
  using PC::PacerTierUtil::Rate;
  // The baseline: each of the three raises it. A display's side that skips does not, without a time on the present
  EXPECT_EQ(Rate(PacerCapabilities()), (PacerRating{PacerTier::TimerPeriodOnly, EveryRaise, false, false}));
  EXPECT_EQ(PacerRating(), Rate(PacerCapabilities()));
  // A wait for a present: vertical blank times or a timed present would raise it
  EXPECT_EQ(Rate(PacerCapabilities(PacerCapability::WaitForPresent)),
            (PacerRating{PacerTier::TimerWaitForPresent, TimedPresent | PacerCapability::VBlankTimes, false, false}));
  // Vertical blank times: a wait for a present or a timed present would
  EXPECT_EQ(Rate(PacerCapabilities(PacerCapability::VBlankTimes)),
            (PacerRating{PacerTier::VBlankPeriodOnly, TimedPresent | PacerCapability::WaitForPresent, false, false}));
  // A present at a time, and nothing else: a time the frame before it stays raises nothing, a display's side that skips does
  EXPECT_EQ(Rate(PacerCapabilities(PacerCapability::PresentAtTime)),
            (PacerRating{PacerTier::TimedTimerPeriodOnly, Skips | PacerCapability::WaitForPresent | PacerCapability::VBlankTimes, true, false}));
  // All three: the best tier of a display that shows every frame, and only one that skips is above it
  EXPECT_EQ(Rate(PacerCapabilities(PacerCapability::PresentAtTime | PacerCapability::VBlankTimes | PacerCapability::WaitForPresent)),
            (PacerRating{PacerTier::TimedVBlankWaitForPresent, Skips, true, false}));
  // Everything: the best tier, and nothing raises it
  EXPECT_EQ(Rate(PacerCapabilities(PC::Without(PacerCapability::AllCapabilities, PresentPair | AcquirePair), 4)),
            (PacerRating{PacerTier::TimedSkipVBlankWaitForPresent, PacerCapability::NoCapabilities, true, true}));

  // Display times are the "+" beside a tier: reported, and no tier is changed by them
  for (const PacerCapability capabilities :
       {PacerCapability::NoCapabilities, PacerCapability::VBlankTimes, PacerCapability::PresentAtTime | PacerCapability::WaitForPresent})
  {
    const PacerRating without = Rate(PacerCapabilities(capabilities));
    const PacerRating with = Rate(PacerCapabilities(capabilities | PacerCapability::DisplayTimes));
    EXPECT_FALSE(without.ReportsDisplayTimes);
    EXPECT_TRUE(with.ReportsDisplayTimes);
    EXPECT_EQ(with.Tier, without.Tier);
    EXPECT_EQ(with.RaisesTier, without.RaisesTier);
  }

  // "What would I get without the wait for a present": the rating of a set is asked without a pacer
  const PacerCapabilities has(PacerCapability::WaitForPresent | PacerCapability::VBlankTimes | PacerCapability::DisplayTimes);
  EXPECT_EQ(Rate(has).Tier, PacerTier::VBlankWaitForPresent);
  EXPECT_EQ(Rate(has.Without(PacerCapability::WaitForPresent)).Tier, PacerTier::VBlankPeriodOnly);
  EXPECT_EQ(Rate(has.Without(PacerCapability::VBlankTimes)).Tier, PacerTier::TimerWaitForPresent);
  EXPECT_EQ(Rate(has.Without(PacerCapability::WaitForPresent | PacerCapability::VBlankTimes)).Tier, PacerTier::TimerPeriodOnly);
}

TEST(PacerTier, EverySetIsRatedAtTheBestTierItReachesAndNeverBelowASetItContains)
{
  using PC::PacerTierUtil::Rate;
  using PC::PacerTierUtil::Reaches;
  uint32_t rated = 0;
  for (uint32_t bits = 0; bits <= static_cast<uint32_t>(PacerCapability::AllCapabilities); ++bits)
  {
    const auto capabilities = static_cast<PacerCapability>(bits);
    if (!IsValid(capabilities))
    {
      continue;
    }
    for (const uint32_t maxSwapInterval : {1u, 2u, 5u})
    {
      const PacerCapabilities set(capabilities, maxSwapInterval);
      const PacerRating rating = Rate(set);
      ++rated;

      // The tier of the rating is reached, and no better one is
      ASSERT_TRUE(Reaches(set, rating.Tier)) << bits;
      for (const PacerTier tier : EveryTier)
      {
        ASSERT_TRUE(Number(tier) >= Number(rating.Tier) || !Reaches(set, tier)) << bits;
      }
      ASSERT_EQ(rating.RaisesTier == PacerCapability::NoCapabilities, rating.Tier == PacerTier::TimedSkipVBlankWaitForPresent) << bits;
      ASSERT_EQ(rating.ReportsDisplayTimes, set.Has(PacerCapability::DisplayTimes)) << bits;
      ASSERT_EQ(rating.DisplaySideHolds, PC::PacerTierUtil::DisplaySideHolds(set)) << bits;

      // One capability more: the rating is never worse, it is better exactly where the rating said it would be, and the
      // display's side goes on holding where it did
      for (const PacerCapability added : Each)
      {
        if (set.Has(added) || !IsValid(capabilities | added))
        {
          continue;
        }
        // A present that begins to take a swap interval takes one that holds a frame
        const PacerCapabilities larger(capabilities | added, added == PacerCapability::PresentSwapInterval ? 2u : maxSwapInterval);
        const PacerRating largerRating = Rate(larger);
        ASSERT_LE(Number(largerRating.Tier), Number(rating.Tier)) << bits;
        ASSERT_EQ(Number(largerRating.Tier) < Number(rating.Tier), PC::HasCapability(rating.RaisesTier, added)) << bits;
        ASSERT_TRUE(largerRating.DisplaySideHolds || !rating.DisplaySideHolds) << bits;
      }
    }
  }
  // 16 capabilities, two pairs of which a set has one or neither (3 of 4 combinations each), three swap intervals
  EXPECT_EQ(rated, (65536u / 16u) * 9u * 3u);
}

TEST(PacerTier, ALongerSwapIntervalNeverLowersARating)
{
  using PC::PacerTierUtil::Rate;
  const PacerCapabilities one(PacerCapability::PresentSwapInterval | PacerCapability::VBlankTimes, 1);
  const PacerCapabilities two(PacerCapability::PresentSwapInterval | PacerCapability::VBlankTimes, 2);

  // The present takes a swap interval, and a longest one of 1 holds nothing: the display's side holds with a longer one, and
  // the tier is the same with either
  EXPECT_EQ(Rate(one).Tier, PacerTier::VBlankPeriodOnly);
  EXPECT_FALSE(Rate(one).DisplaySideHolds);
  EXPECT_EQ(Rate(two).Tier, PacerTier::VBlankPeriodOnly);
  EXPECT_TRUE(Rate(two).DisplaySideHolds);
  EXPECT_TRUE(two.Contains(one));
  EXPECT_FALSE(one.Contains(two));
}

namespace
{
  namespace Text = PC::PacerTierText;

  // The texts are there at compile time
  static_assert(Text::NameOf(PacerTier::TimerPeriodOnly) == "timer");
  static_assert(Text::NameOf(PacerTier::VBlankWaitForPresent) == "vertical blank times, wait for a present");
  static_assert(Text::NameOf(PacerTier::TimedTimerPeriodOnly) == "timed present");
  static_assert(Text::NumberOf(PacerTier::VBlankWaitForPresent) == "3.1");
  static_assert(Text::MajorTierCount * PC::PacerTierUtil::SubTiersPerMajorTier == Text::TierCount);
  static_assert(Text::DisplayTimesMark == "+");
  static_assert(Text::NameOf(PacerCapability::VBlankTimes) == "vertical blank times");
  // The counts are the enums': the lowest tier's number, and a bit per capability
  static_assert(Text::TierCount == static_cast<uint32_t>(PacerTier::TimerPeriodOnly));
  static_assert(Text::TierCount == EveryTier.size());
  static_assert(static_cast<uint32_t>(PacerCapability::AllCapabilities) == (1u << Text::CapabilityCount) - 1u);

  //! A text an application can show and hand to a C function: something, plain ASCII, and a zero after it
  void ExpectShowable(const std::string_view text)
  {
    ASSERT_FALSE(text.empty());
    // A string literal: read as a C string it ends where the view does, which is what this checks
    // NOLINTNEXTLINE(bugprone-suspicious-stringview-data-usage)
    EXPECT_EQ(std::strlen(text.data()), text.size()) << text;
    for (const char character : text)
    {
      EXPECT_TRUE(character >= ' ' && character <= '~') << text;
    }
  }
}

TEST(PacerTierText, EveryTierHasANameOfItsOwnAndADescription)
{
  std::set<std::string_view> names;
  for (uint32_t number = 1; number <= Text::TierCount; ++number)
  {
    const auto tier = static_cast<PacerTier>(number);
    EXPECT_EQ(tier, EveryTier[number - 1u]);
    ExpectShowable(Text::NameOf(tier));
    ExpectShowable(Text::DescriptionOf(tier));
    EXPECT_EQ(Text::DescriptionOf(tier).back(), '.');
    ExpectShowable(Text::ShortDescriptionOf(tier));
    EXPECT_EQ(Text::ShortDescriptionOf(tier).back(), '.');
    EXPECT_LE(Text::ShortDescriptionOf(tier).size(), Text::ShortDescriptionMaxLength);
    EXPECT_LT(Text::ShortDescriptionOf(tier).size(), Text::DescriptionOf(tier).size());
    names.insert(Text::NameOf(tier));
    // Shown as its major tier, a point and its sub tier
    const std::string_view shown = Text::NumberOf(tier);
    ExpectShowable(shown);
    ASSERT_EQ(shown.size(), 3u);
    EXPECT_EQ(static_cast<uint32_t>(shown[0] - '0'), static_cast<uint32_t>(PC::PacerTierUtil::MajorOf(tier)));
    EXPECT_EQ(shown[1], '.');
    EXPECT_EQ(static_cast<uint32_t>(shown[2] - '0'), PC::PacerTierUtil::SubTierOf(tier));
    // A tier no pacer is built for says so, and names the tier it is paced as
    const bool saysNoPacer = Text::DescriptionOf(tier).find("No pacer is built") != std::string_view::npos;
    EXPECT_EQ(saysNoPacer, !PC::PacerTierUtil::HasPacer(tier));
    if (saysNoPacer)
    {
      EXPECT_NE(Text::DescriptionOf(tier).find(Text::NumberOf(PC::PacerTierUtil::PacedAs(tier))), std::string_view::npos);
    }
  }
  EXPECT_EQ(names.size(), Text::TierCount);

  // The major tiers
  std::set<std::string_view> majorNames;
  for (uint32_t number = 1; number <= Text::MajorTierCount; ++number)
  {
    const auto major = static_cast<PC::PacerMajorTier>(number);
    ExpectShowable(Text::NameOf(major));
    ExpectShowable(Text::DescriptionOf(major));
    EXPECT_EQ(Text::DescriptionOf(major).back(), '.');
    majorNames.insert(Text::NameOf(major));
  }
  EXPECT_EQ(majorNames.size(), Text::MajorTierCount);
  EXPECT_TRUE(Text::NameOf(static_cast<PC::PacerMajorTier>(0)).empty());
  EXPECT_TRUE(Text::DescriptionOf(static_cast<PC::PacerMajorTier>(4)).empty());

  // A number that is no tier has no text
  EXPECT_TRUE(Text::NameOf(static_cast<PacerTier>(0)).empty());
  EXPECT_TRUE(Text::NameOf(static_cast<PacerTier>(13)).empty());
  EXPECT_TRUE(Text::DescriptionOf(static_cast<PacerTier>(0)).empty());
  EXPECT_TRUE(Text::DescriptionOf(static_cast<PacerTier>(13)).empty());
  EXPECT_TRUE(Text::ShortDescriptionOf(static_cast<PacerTier>(0)).empty());
  EXPECT_TRUE(Text::ShortDescriptionOf(static_cast<PacerTier>(13)).empty());
  EXPECT_TRUE(Text::NumberOf(static_cast<PacerTier>(0)).empty());
  EXPECT_TRUE(Text::NumberOf(static_cast<PacerTier>(13)).empty());
}

TEST(PacerTierText, EveryCapabilityHasANameOfItsOwnAndASetOfSeveralHasNone)
{
  std::set<std::string_view> names;
  for (uint32_t index = 0; index < Text::CapabilityCount; ++index)
  {
    const auto capability = static_cast<PacerCapability>(1u << index);
    EXPECT_EQ(capability, Each[index]);
    ExpectShowable(Text::NameOf(capability));
    names.insert(Text::NameOf(capability));
  }
  EXPECT_EQ(names.size(), Text::CapabilityCount);
  EXPECT_EQ(Text::NameOf(PacerCapability::NoCapabilities), "baseline");

  // What would raise a rating is a set: named one capability at a time
  EXPECT_TRUE(Text::NameOf(PacerCapability::WaitForPresent | PacerCapability::DisplayTimes).empty());
  EXPECT_TRUE(Text::NameOf(PacerCapability::AllCapabilities).empty());
  EXPECT_TRUE(Text::NameOf(static_cast<PacerCapability>(1u << Text::CapabilityCount)).empty());
}
