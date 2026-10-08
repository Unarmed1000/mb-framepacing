// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The simulation's display (DisplayModel): a present is taken at once and queued, the display takes the oldest queued frame at a
// vertical blank. What the frame loop tests build on: when a frame is shown, how many wait, and what a blank without a frame
// taken does to every frame after it.
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <gtest/gtest.h>
#include <cstdint>
#include "DisplayModel.hpp"
#include "DisplayModelSettings.hpp"

namespace PC = MB::FramePacing::Pacer;
namespace Sim = MB::FramePacing::Pacer::Simulation;

namespace
{
  // 100 Hz: a refresh of exactly 10,000,000 ns
  constexpr int64_t Period = 10'000'000;
  constexpr int64_t Blank0 = 1'000'000'000;

  const PC::RefreshPeriod g_hz100 = PC::RefreshPeriod::FromRate(100);

  constexpr int64_t Blank(const int64_t blank) noexcept
  {
    return Blank0 + (blank * Period);
  }
}

TEST(DisplayModel, ItsVerticalBlanksAreAPeriodApartFromTheFirst)
{
  const Sim::DisplayModel display(g_hz100, {});

  EXPECT_EQ(display.BlankNanoseconds(0), Blank0);
  EXPECT_EQ(display.BlankNanoseconds(7), Blank(7));
  EXPECT_EQ(display.BlankAtOrBefore(Blank0 - 1), 0);
  EXPECT_EQ(display.BlankAtOrBefore(Blank(3)), 3);
  EXPECT_EQ(display.BlankAtOrBefore(Blank(4) - 1), 3);
}

TEST(DisplayModel, AFrameIsShownAtTheFirstVerticalBlankItIsReadyFor)
{
  Sim::DisplayModel display(g_hz100, {});

  // Presented with its GPU work done: the next blank
  EXPECT_EQ(display.Present(Blank0 + 1'000'000, Blank0 + 500'000), Blank(1));
  // Presented before its GPU work is done: the first blank after the work, however early the present was
  EXPECT_EQ(display.Present(Blank(1) + 1'000'000, Blank(2) + 5'000'000), Blank(3));
  // Ready exactly at a blank is in time for it
  EXPECT_EQ(display.Present(Blank(4), Blank(4)), Blank(4));
}

TEST(DisplayModel, AFrameHasToBeReadyTheLatchLeadBeforeAVerticalBlank)
{
  Sim::DisplayModelSettings settings;
  settings.LatchLeadNanoseconds = 1'000'000;
  Sim::DisplayModel display(g_hz100, settings);

  EXPECT_EQ(display.Present(Blank0 + 100'000, Blank0 + 9'000'000), Blank(1));
  // 0.5 ms before the blank is too late for it
  EXPECT_EQ(display.Present(Blank(1) + 100'000, Blank(1) + 9'500'000), Blank(3));
}

TEST(DisplayModel, OneFrameIsTakenPerVerticalBlankInTheOrderOfThePresents)
{
  Sim::DisplayModel display(g_hz100, {});
  const int64_t now = Blank0 + 1'000'000;

  EXPECT_EQ(display.Present(now, now), Blank(1));
  EXPECT_EQ(display.Present(now, now), Blank(2));
  EXPECT_EQ(display.Present(now, now), Blank(3));

  EXPECT_EQ(display.Pending(now - 1), 0);
  EXPECT_EQ(display.Pending(now), 3);
  EXPECT_EQ(display.Pending(Blank(1) - 1), 3);
  EXPECT_EQ(display.Pending(Blank(1)), 2);
  EXPECT_EQ(display.Pending(Blank(2)), 1);
  EXPECT_EQ(display.Pending(Blank(3)), 0);
}

TEST(DisplayModel, AFrameStaysItsSwapIntervalBehindTheFrameBeforeIt)
{
  Sim::DisplayModel display(g_hz100, {});
  const int64_t now = Blank0 + 1'000'000;

  EXPECT_EQ(display.Present(now, now), Blank(1));
  EXPECT_EQ(display.Present(now, now, 2), Blank(3));
  EXPECT_EQ(display.Present(now, now, 3), Blank(6));
  // The first frame has no frame before it
  Sim::DisplayModel other(g_hz100, {});
  EXPECT_EQ(other.Present(now, now, 4), Blank(1));
}

TEST(DisplayModel, APresentWithATimeIsNotShownBeforeIt)
{
  Sim::DisplayModel display(g_hz100, {});
  const int64_t now = Blank0 + 1'000'000;

  // Without a time it is a present at a swap interval of 1
  EXPECT_EQ(display.PresentTimed(now, now, 0, 0), Blank(1));
  EXPECT_EQ(display.PresentTimed(now, now, 0, 0), Blank(2));
  // The first blank at or after the time, whenever the present was made
  EXPECT_EQ(display.PresentTimed(now, now, Blank(5) - (Period / 2), 0), Blank(5));
  EXPECT_EQ(display.PresentTimed(now, now, Blank(7), 0), Blank(7));
  EXPECT_EQ(display.PresentTimed(now, now, Blank(7) + 1, 0), Blank(8));
  // A time that has passed holds nothing: the first blank the frame is ready for, after the frame before it
  EXPECT_EQ(display.PresentTimed(now, now, Blank(3), 0), Blank(9));
  EXPECT_EQ(display.PresentTimed(Blank(11) + 1, Blank(12) + 1, Blank(3), 0), Blank(13));
}

TEST(DisplayModel, APresentWithAMinimumDurationKeepsTheFrameBeforeItOnScreenThatLong)
{
  Sim::DisplayModel display(g_hz100, {});
  const int64_t now = Blank0 + 1'000'000;

  // The first frame has no frame before it
  EXPECT_EQ(display.PresentTimed(now, now, 0, 4 * Period), Blank(1));
  // Half a period less than its refreshes, as a pacer gives it: one refresh is what the display does by itself
  EXPECT_EQ(display.PresentTimed(now, now, 0, Period / 2), Blank(2));
  EXPECT_EQ(display.PresentTimed(now, now, 0, (2 * Period) - (Period / 2)), Blank(4));
  EXPECT_EQ(display.PresentTimed(now, now, 0, 3 * Period), Blank(7));
  // Counted from when the frame before it was shown, not from when it was to be: a frame that is late moves the next one
  EXPECT_EQ(display.PresentTimed(Blank(9) + 1, Blank(9) + 1, 0, Period / 2), Blank(10));
  EXPECT_EQ(display.PresentTimed(Blank(9) + 2, Blank(9) + 2, 0, (2 * Period) - (Period / 2)), Blank(12));
  // With both, the later of the two
  EXPECT_EQ(display.PresentTimed(Blank(9) + 3, Blank(9) + 3, Blank(20), Period / 2), Blank(20));
  EXPECT_EQ(display.PresentTimed(Blank(9) + 4, Blank(9) + 4, Blank(20), 3 * Period), Blank(23));
}

TEST(DisplayModel, ATimeOnAPresentIsTheTimeTheFrameIsShownAtBehindACompositor)
{
  Sim::DisplayModelSettings settings;
  settings.PipelineRefreshes = 1;
  Sim::DisplayModel display(g_hz100, settings);
  const int64_t now = Blank0 + 1'000'000;

  // Taken for the blank before the one it is shown at
  EXPECT_EQ(display.PresentTimed(now, now, Blank(5) - (Period / 2), 0), Blank(5));
  EXPECT_EQ(display.PresentTimed(now, now, 0, (2 * Period) - (Period / 2)), Blank(7));
  // And no sooner than it can be: a frame is shown a refresh after the blank it was ready for
  EXPECT_EQ(display.PresentTimed(Blank(9) + 1, Blank(9) + 1, Blank(9), 0), Blank(11));
}

TEST(DisplayModel, AVerticalBlankThatTakesNoFramePutsEveryFramePresentedAtTheDisplaysRateOneBehind)
{
  Sim::DisplayModelSettings settings;
  settings.HeldBlanks = {5};
  Sim::DisplayModel display(g_hz100, settings);

  // One present per refresh, a tenth of a refresh after each blank
  for (int64_t frame = 0; frame < 12; ++frame)
  {
    const int64_t now = Blank(frame) + 1'000'000;
    const int32_t waiting = display.Pending(now);
    const int64_t shown = display.Present(now, now);
    if (frame < 4)
    {
      EXPECT_EQ(shown, Blank(frame + 1)) << frame;
      EXPECT_EQ(waiting, 0) << frame;
    }
    else
    {
      // The frame aimed at blank 5 is shown at 6, and so is every frame after it one blank later than it would have been: one
      // more frame waits from then on, and nothing takes it away
      EXPECT_EQ(shown, Blank(frame + 2)) << frame;
      EXPECT_EQ(waiting, frame < 5 ? 0 : 1) << frame;
    }
  }
}

TEST(DisplayModel, AHeldVerticalBlankWithNoFrameWaitingChangesNothing)
{
  Sim::DisplayModelSettings settings;
  settings.HeldBlanks = {2};
  Sim::DisplayModel display(g_hz100, settings);

  EXPECT_EQ(display.Present(Blank0 + 1'000'000, Blank0 + 1'000'000), Blank(1));
  // Nothing was presented for blank 2
  EXPECT_EQ(display.Present(Blank(2) + 1'000'000, Blank(2) + 1'000'000), Blank(3));
  EXPECT_EQ(display.Present(Blank(3) + 1'000'000, Blank(3) + 1'000'000), Blank(4));
}

TEST(DisplayModel, APipelineRefreshShowsEveryFrameOneRefreshAfterItIsTaken)
{
  Sim::DisplayModelSettings settings;
  settings.PipelineRefreshes = 1;
  Sim::DisplayModel display(g_hz100, settings);
  const int64_t now = Blank0 + 1'000'000;

  EXPECT_EQ(display.Present(now, now), Blank(2));
  EXPECT_EQ(display.Present(now, now), Blank(3));
  EXPECT_EQ(display.Pending(Blank(2) - 1), 2);
}

TEST(DisplayModel, NothingBoundsTheWaitingFramesWithoutImages)
{
  Sim::DisplayModel display(g_hz100, {});
  const int64_t now = Blank0 + 1'000'000;
  for (int32_t frame = 0; frame < 8; ++frame)
  {
    EXPECT_EQ(display.AcquireNanoseconds(now), now);
    static_cast<void>(display.Present(now, now));
  }
  EXPECT_EQ(display.Pending(now), 8);
}

TEST(DisplayModel, WithTwoImagesAnAcquireWaitsUntilTheFramePresentedBeforeIsShown)
{
  Sim::DisplayModelSettings settings;
  settings.Images = 2;
  Sim::DisplayModel display(g_hz100, settings);
  const int64_t now = Blank0 + 1'000'000;

  EXPECT_EQ(display.AcquireNanoseconds(now), now);
  EXPECT_EQ(display.Present(now, now), Blank(1));
  EXPECT_EQ(display.AcquireNanoseconds(now + 100'000), Blank(1));
  // After it is shown an acquire returns at once
  EXPECT_EQ(display.AcquireNanoseconds(Blank(1) + 100'000), Blank(1) + 100'000);
}

TEST(DisplayModel, WithThreeImagesOneFrameMayWaitWhileTheNextIsDrawn)
{
  Sim::DisplayModelSettings settings;
  settings.Images = 3;
  Sim::DisplayModel display(g_hz100, settings);
  const int64_t now = Blank0 + 1'000'000;

  static_cast<void>(display.Present(now, now));
  EXPECT_EQ(display.AcquireNanoseconds(now), now);
  static_cast<void>(display.Present(now, now));
  // Two wait: the acquire returns when the first of them is shown
  EXPECT_EQ(display.AcquireNanoseconds(now), Blank(1));
  static_cast<void>(display.Present(Blank(1), Blank(1)));
  static_cast<void>(display.Present(Blank(1), Blank(1)));
  // Three wait at blank 1 (shown at 2, 3 and 4): the acquire returns when two of them are shown
  EXPECT_EQ(display.Pending(Blank(1)), 3);
  EXPECT_EQ(display.AcquireNanoseconds(Blank(1)), Blank(3));
}
