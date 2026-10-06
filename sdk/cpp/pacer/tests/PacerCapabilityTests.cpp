// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The capabilities an application gives the pacer, and the tiers as sets of them (sdk/doc/pacer-design.md): a set reaches a tier
// when it holds all of one of the tier's sets, and its rating is the best tier it reaches. Every set there is is rated here, and
// no set is rated below a set it contains.
#include <mb/framepacing/pacer/capability/HoldTier.hpp>
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerRating.hpp>
#include <mb/framepacing/pacer/capability/PacerTierUtil.hpp>
#include <mb/framepacing/pacer/capability/QueueTier.hpp>
#include <gtest/gtest.h>
#include <array>
#include <cstdint>

namespace PC = MB::FramePacing::Pacer;
using PC::HoldTier;
using PC::PacerCapabilities;
using PC::PacerCapability;
using PC::PacerRating;
using PC::QueueTier;

namespace
{
  constexpr std::array<PacerCapability, 15> Each = {
    PacerCapability::PresentSwapInterval,  PacerCapability::PresentAtTime,    PacerCapability::PresentAfterDuration, PacerCapability::PresentWaits,
    PacerCapability::PresentReturnsAtOnce, PacerCapability::AcquireWaits,     PacerCapability::AcquireReturnsAtOnce, PacerCapability::WaitForPresent,
    PacerCapability::WaitForGpuWork,       PacerCapability::WaitForImage,     PacerCapability::FrameCallback,        PacerCapability::VBlankTimes,
    PacerCapability::GpuWorkTimes,         PacerCapability::GpuWorkDurations, PacerCapability::DisplayTimes,
  };

  constexpr PacerCapability PresentPair = PacerCapability::PresentWaits | PacerCapability::PresentReturnsAtOnce;
  constexpr PacerCapability AcquirePair = PacerCapability::AcquireWaits | PacerCapability::AcquireReturnsAtOnce;

  //! Whether the bits are a set an application can have: not both of a pair that excludes itself
  constexpr bool IsValid(const PacerCapability capabilities) noexcept
  {
    return !PC::HasCapability(capabilities, PresentPair) && !PC::HasCapability(capabilities, AcquirePair);
  }

  constexpr uint32_t Number(const HoldTier tier) noexcept
  {
    return static_cast<uint32_t>(tier);
  }

  constexpr uint32_t Number(const QueueTier tier) noexcept
  {
    return static_cast<uint32_t>(tier);
  }

  // The rating is usable where a constant is needed
  static_assert(PC::PacerTierUtil::Rate(PacerCapabilities()).Hold == HoldTier::Timer);
  static_assert(PC::PacerTierUtil::Rate(PacerCapabilities(PacerCapability::VBlankTimes | PacerCapability::WaitForPresent)) ==
                PacerRating{HoldTier::VBlank, QueueTier::WaitForPresent,
                            PacerCapability::PresentAtTime | PacerCapability::PresentAfterDuration | PacerCapability::PresentSwapInterval,
                            PacerCapability::NoCapabilities});
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

TEST(PacerTier, TheHoldTiersAreTheirCapabilitySets)
{
  using PC::PacerTierUtil::Reaches;
  // Every set reaches the timer
  EXPECT_TRUE(Reaches(PacerCapabilities(), HoldTier::Timer));
  EXPECT_TRUE(Reaches(PacerCapabilities(PacerCapability::AllCapabilities, 8), HoldTier::Timer));
  // Vertical blank times, and nothing else, reach the tier of the loop that knows where the refreshes are
  EXPECT_TRUE(Reaches(PacerCapabilities(PacerCapability::VBlankTimes), HoldTier::VBlank));
  EXPECT_FALSE(Reaches(PacerCapabilities(), HoldTier::VBlank));
  EXPECT_FALSE(Reaches(PacerCapabilities(PacerCapability::DisplayTimes | PacerCapability::WaitForPresent), HoldTier::VBlank));
  EXPECT_FALSE(Reaches(PacerCapabilities(PacerCapability::PresentAtTime), HoldTier::VBlank));
  // Three ways to have the display side hold a frame
  EXPECT_TRUE(Reaches(PacerCapabilities(PacerCapability::PresentAtTime), HoldTier::DisplaySide));
  EXPECT_TRUE(Reaches(PacerCapabilities(PacerCapability::PresentAfterDuration), HoldTier::DisplaySide));
  EXPECT_TRUE(Reaches(PacerCapabilities(PacerCapability::PresentSwapInterval, 2), HoldTier::DisplaySide));
  // A present whose longest swap interval is 1 holds no frame for more than one refresh
  EXPECT_FALSE(Reaches(PacerCapabilities(PacerCapability::PresentSwapInterval, 1), HoldTier::DisplaySide));
  EXPECT_FALSE(Reaches(PacerCapabilities(PacerCapability::VBlankTimes), HoldTier::DisplaySide));
  EXPECT_FALSE(Reaches(PacerCapabilities(), HoldTier::DisplaySide));
}

TEST(PacerTier, TheQueueTiersAreTheirCapabilitySets)
{
  using PC::PacerTierUtil::Reaches;
  EXPECT_TRUE(Reaches(PacerCapabilities(), QueueTier::PeriodOnly));
  EXPECT_TRUE(Reaches(PacerCapabilities(PacerCapability::DisplayTimes), QueueTier::DisplayTimes));
  EXPECT_FALSE(Reaches(PacerCapabilities(), QueueTier::DisplayTimes));
  EXPECT_FALSE(Reaches(PacerCapabilities(PacerCapability::WaitForPresent), QueueTier::DisplayTimes));
  EXPECT_TRUE(Reaches(PacerCapabilities(PacerCapability::WaitForPresent), QueueTier::WaitForPresent));
  EXPECT_FALSE(Reaches(PacerCapabilities(PacerCapability::DisplayTimes), QueueTier::WaitForPresent));
  // What slows a loop when its queue is full, and what only measures, reach no tier above the baseline
  const PacerCapabilities noTier(PacerCapability::WaitForImage | PacerCapability::FrameCallback | PacerCapability::PresentWaits |
                                 PacerCapability::AcquireWaits | PacerCapability::WaitForGpuWork | PacerCapability::GpuWorkTimes |
                                 PacerCapability::GpuWorkDurations | PacerCapability::VBlankTimes);
  EXPECT_FALSE(Reaches(noTier, QueueTier::DisplayTimes));
  EXPECT_FALSE(Reaches(noTier, QueueTier::WaitForPresent));
}

TEST(PacerTier, ARatingIsTheBestTierOfEachQuestionAndWhatWouldRaiseIt)
{
  using PC::PacerTierUtil::Rate;
  const PacerCapability displaySide = PacerCapability::PresentAtTime | PacerCapability::PresentAfterDuration | PacerCapability::PresentSwapInterval;

  // The baseline: a timer, and the refresh period only
  EXPECT_EQ(Rate(PacerCapabilities()), (PacerRating{HoldTier::Timer, QueueTier::PeriodOnly, displaySide | PacerCapability::VBlankTimes,
                                                    PacerCapability::WaitForPresent | PacerCapability::DisplayTimes}));
  // A present with a swap interval and nothing else: its frames are held exactly, and its display is not seen
  EXPECT_EQ(Rate(PacerCapabilities(PacerCapability::PresentSwapInterval, 4)),
            (PacerRating{HoldTier::DisplaySide, QueueTier::PeriodOnly, PacerCapability::NoCapabilities,
                         PacerCapability::WaitForPresent | PacerCapability::DisplayTimes}));
  // The other way round: the loop on the vertical blank with a wait for a present
  EXPECT_EQ(Rate(PacerCapabilities(PacerCapability::VBlankTimes | PacerCapability::WaitForPresent)),
            (PacerRating{HoldTier::VBlank, QueueTier::WaitForPresent, displaySide, PacerCapability::NoCapabilities}));
  // A timed present with display times, as one extension gives both
  EXPECT_EQ(Rate(PacerCapabilities(PacerCapability::PresentAfterDuration | PacerCapability::DisplayTimes)),
            (PacerRating{HoldTier::DisplaySide, QueueTier::DisplayTimes, PacerCapability::NoCapabilities, PacerCapability::WaitForPresent}));
  // Everything
  EXPECT_EQ(Rate(PacerCapabilities(PC::Without(PacerCapability::AllCapabilities, PresentPair | AcquirePair), 4)),
            (PacerRating{HoldTier::DisplaySide, QueueTier::WaitForPresent, PacerCapability::NoCapabilities, PacerCapability::NoCapabilities}));
  // "What would I get without the timed present": the rating of a set is asked without a pacer
  const PacerCapabilities has(PacerCapability::PresentAfterDuration | PacerCapability::VBlankTimes | PacerCapability::DisplayTimes);
  EXPECT_EQ(Rate(has).Hold, HoldTier::DisplaySide);
  EXPECT_EQ(Rate(has.Without(PacerCapability::PresentAfterDuration)).Hold, HoldTier::VBlank);
  EXPECT_EQ(Rate(has.Without(PacerCapability::PresentAfterDuration | PacerCapability::VBlankTimes)).Hold, HoldTier::Timer);
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
      ASSERT_TRUE(Reaches(set, rating.Hold)) << bits;
      ASSERT_TRUE(Reaches(set, rating.Queue)) << bits;
      for (const HoldTier better : {HoldTier::DisplaySide, HoldTier::VBlank})
      {
        ASSERT_TRUE(Number(better) >= Number(rating.Hold) || !Reaches(set, better)) << bits;
      }
      for (const QueueTier better : {QueueTier::WaitForPresent, QueueTier::DisplayTimes})
      {
        ASSERT_TRUE(Number(better) >= Number(rating.Queue) || !Reaches(set, better)) << bits;
      }
      ASSERT_EQ(rating.RaisesHold == PacerCapability::NoCapabilities, rating.Hold == HoldTier::DisplaySide) << bits;
      ASSERT_EQ(rating.RaisesQueue == PacerCapability::NoCapabilities, rating.Queue == QueueTier::WaitForPresent) << bits;

      // One capability more: the rating is never worse, and it is better exactly where the rating said it would be
      for (const PacerCapability added : Each)
      {
        if (set.Has(added) || !IsValid(capabilities | added))
        {
          continue;
        }
        // A present that begins to take a swap interval takes one that holds a frame
        const PacerCapabilities larger(capabilities | added, added == PacerCapability::PresentSwapInterval ? 2u : maxSwapInterval);
        const PacerRating largerRating = Rate(larger);
        ASSERT_LE(Number(largerRating.Hold), Number(rating.Hold)) << bits;
        ASSERT_LE(Number(largerRating.Queue), Number(rating.Queue)) << bits;
        ASSERT_EQ(Number(largerRating.Hold) < Number(rating.Hold), PC::HasCapability(rating.RaisesHold, added)) << bits;
        ASSERT_EQ(Number(largerRating.Queue) < Number(rating.Queue), PC::HasCapability(rating.RaisesQueue, added)) << bits;
      }
    }
  }
  // 15 capabilities, two pairs of which a set has one or neither (3 of 4 combinations each), three swap intervals
  EXPECT_EQ(rated, (32768u / 16u) * 9u * 3u);
}

TEST(PacerTier, ALongerSwapIntervalNeverLowersARating)
{
  using PC::PacerTierUtil::Rate;
  const PacerCapabilities one(PacerCapability::PresentSwapInterval | PacerCapability::VBlankTimes, 1);
  const PacerCapabilities two(PacerCapability::PresentSwapInterval | PacerCapability::VBlankTimes, 2);

  // The present takes a swap interval, and a longest one of 1 holds nothing: the set is rated by its vertical blank times, and
  // a longer swap interval is among what would raise it
  EXPECT_EQ(Rate(one).Hold, HoldTier::VBlank);
  EXPECT_TRUE(PC::HasCapability(Rate(one).RaisesHold, PacerCapability::PresentSwapInterval));
  EXPECT_EQ(Rate(two).Hold, HoldTier::DisplaySide);
  EXPECT_TRUE(two.Contains(one));
  EXPECT_FALSE(one.Contains(two));
}
