// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The tiers with a timed present (PacerTier 1 to 4) on the simulation's display (FrameLoopSimulation), which honours a
// time on a present: where a time before which a frame is not shown, and a time the frame before it stays, change what the
// display shows against the same loop without one, and where they do not. The simulation only: no system has been measured
// with them.
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <gtest/gtest.h>
#include <array>
#include <cstddef>
#include <cstdint>
#include <vector>
#include "FrameLoopSimulation.hpp"
#include "LoopFrame.hpp"
#include "LoopSettings.hpp"

namespace PC = MB::FramePacing::Pacer;
namespace Sim = MB::FramePacing::Pacer::Simulation;

namespace
{
  //! The time a present is given
  enum class Timed
  {
    No,
    AtTime,
    AfterDuration,
  };

  //! Light work at 240 Hz (a tenth of a refresh on the CPU, a fifth on the GPU), no pause after start-up
  Sim::LoopSettings LightLoop(const PC::PacerAim aim, const Timed timed)
  {
    Sim::LoopSettings settings;
    settings.Frames = 2'000;
    settings.Aim = aim;
    settings.StartupPauseRefreshes = 0;
    settings.PresentsAtTime = timed == Timed::AtTime;
    settings.PresentsAfterDuration = timed == Timed::AfterDuration;
    const int64_t period = PC::RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToNanosecondTimeSpan().Nanoseconds();
    settings.GpuWork = {period / 5, period / 5};
    return settings;
  }

  int64_t PeriodNanoseconds(const Sim::LoopSettings& settings)
  {
    return PC::RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToNanosecondTimeSpan().Nanoseconds();
  }

  //! The refreshes a frame was on screen after the frame before it
  int64_t RefreshesOnScreen(const std::vector<Sim::LoopFrame>& frames, const std::size_t index, const int64_t periodNanoseconds)
  {
    return ((frames[index].ShownNanoseconds - frames[index - 1].ShownNanoseconds) + (periodNanoseconds / 2)) / periodNanoseconds;
  }

  //! The frames from the 100th on that were not shown their swap interval after the frame before them
  int32_t OffTheSwapInterval(const std::vector<Sim::LoopFrame>& frames, const int64_t periodNanoseconds)
  {
    int32_t off = 0;
    for (std::size_t index = 100; index < frames.size(); ++index)
    {
      off += RefreshesOnScreen(frames, index, periodNanoseconds) != int64_t{frames[index].SwapInterval} ? 1 : 0;
    }
    return off;
  }

  //! The refreshes a frame was shown after the vertical blank the pacer made it for
  int64_t RefreshesAfterIntended(const Sim::LoopFrame& frame, const int64_t periodNanoseconds)
  {
    return ((frame.ShownNanoseconds - frame.IntendedDisplayNanoseconds) + (periodNanoseconds / 2)) / periodNanoseconds;
  }

  //! From a frame's start to its display, in half refreshes
  int64_t HalfRefreshesToDisplay(const Sim::LoopFrame& frame, const int64_t periodNanoseconds)
  {
    return (((frame.ShownNanoseconds - frame.StartNanoseconds) * 2) + (periodNanoseconds / 2)) / periodNanoseconds;
  }
}

TEST(TimedPresentLoop, WhereNothingGoesWrongATimedPresentShowsTheFramesTheLoopShowsWithoutOne)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    for (const Timed timed : {Timed::AtTime, Timed::AfterDuration})
    {
      const Sim::LoopSettings plain = LightLoop(aim, Timed::No);
      const Sim::LoopSettings settings = LightLoop(aim, timed);
      const int64_t period = PeriodNanoseconds(settings);
      const std::array<std::vector<Sim::LoopFrame>, 4> expected = {
        Sim::SimulateTimerPeriodOnlyLoop(plain), Sim::SimulateTimerWaitForPresentLoop(plain), Sim::SimulateVBlankPeriodOnlyLoop(plain),
        Sim::SimulateVBlankWaitForPresentLoop(plain)};
      const std::array<std::vector<Sim::LoopFrame>, 4> frames = {
        Sim::SimulateTimerPeriodOnlyLoop(settings), Sim::SimulateTimerWaitForPresentLoop(settings), Sim::SimulateVBlankPeriodOnlyLoop(settings),
        Sim::SimulateVBlankWaitForPresentLoop(settings)};
      for (std::size_t tier = 0; tier < 4; ++tier)
      {
        ASSERT_EQ(frames[tier].size(), expected[tier].size());
        EXPECT_EQ(OffTheSwapInterval(frames[tier], period), OffTheSwapInterval(expected[tier], period)) << tier;
        for (std::size_t index = 100; index < frames[tier].size(); ++index)
        {
          // Shown at the same vertical blank, with as many frames waiting
          ASSERT_EQ(frames[tier][index].ShownNanoseconds, expected[tier][index].ShownNanoseconds) << tier << ' ' << index;
          ASSERT_EQ(frames[tier][index].PendingAtStart, expected[tier][index].PendingAtStart) << tier << ' ' << index;
          ASSERT_EQ(frames[tier][index].SwapInterval, 1u) << tier << ' ' << index;
        }
      }
    }
  }
}

TEST(TimedPresentLoop, OnATimerAPresentHeldToTheEdgeOfARefreshFallsOnBothSidesOfItAndATimedPresentDoesNot)
{
  // 120 frames a second at 240 Hz on a timer that wakes up to 0.1 ms late, and a display that takes a frame 85 % of a refresh
  // before its vertical blank: where the loop holds a frame's present to is at the edge of what the display takes
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    Sim::LoopSettings settings = LightLoop(aim, Timed::No);
    settings.PreferredSwapInterval = 2;
    settings.TimerLate = {0, 100'000};
    const int64_t period = PeriodNanoseconds(settings);
    settings.Display.LatchLeadNanoseconds = (period * 85) / 100;

    // Without a timed present the frames fall on either side: on screen for one refresh, or three
    const std::vector<Sim::LoopFrame> untimed = Sim::SimulateTimerPeriodOnlyLoop(settings);
    EXPECT_GT(OffTheSwapInterval(untimed, period), 500);

    // A time the frame before it stays: the first frame that falls on the far side moves every frame after it a refresh later,
    // where the loop's present has the whole refresh, and every frame is on screen for two
    settings.PresentsAfterDuration = true;
    const std::vector<Sim::LoopFrame> afterDuration = Sim::SimulateTimerPeriodOnlyLoop(settings);
    EXPECT_EQ(OffTheSwapInterval(afterDuration, period), 0);

    // A time before which the frame is not shown: presented when it is done, shown at its refresh, and no frame waits
    settings.PresentsAfterDuration = false;
    settings.PresentsAtTime = true;
    const std::vector<Sim::LoopFrame> atTime = Sim::SimulateTimerPeriodOnlyLoop(settings);
    EXPECT_EQ(OffTheSwapInterval(atTime, period), 0);
    for (std::size_t index = 100; index < atTime.size(); ++index)
    {
      ASSERT_EQ(atTime[index].SwapInterval, 2u) << index;
      ASSERT_EQ(HalfRefreshesToDisplay(atTime[index], period), 4) << index;
      ASSERT_EQ(atTime[index].PendingAtStart, 0) << index;
      ASSERT_EQ(atTime[index].PresentWaitTargetNanoseconds, 0) << index;
      // A refresh later with the time the frame before it stays, and a frame waits when the next one starts
      ASSERT_EQ(HalfRefreshesToDisplay(afterDuration[index], period), 6) << index;
      ASSERT_EQ(afterDuration[index].PendingAtStart, 1) << index;
    }
  }
}

TEST(TimedPresentLoop, AGridOnTheClockThatSlidesAcrossARefreshCostsAFrameOnceWithATimeOnThePresent)
{
  // 60 frames a second at 240 Hz on a display 0.05 % slower than its mode, timers that wake up to 0.1 ms late: the grid on the
  // clock slides against the display's refreshes
  Sim::LoopSettings settings = LightLoop(PC::PacerAim::LowLatency, Timed::No);
  settings.Frames = 6'000;
  settings.PreferredSwapInterval = 4;
  settings.DisplayPeriodPpm = 500;
  settings.TimerLate = {0, 100'000};
  const int64_t period = PeriodNanoseconds(settings);

  // Without a timed present the held present slides across a vertical blank, and while it is near one frames fall on either side
  EXPECT_GT(OffTheSwapInterval(Sim::SimulateTimerPeriodOnlyLoop(settings), period), 5);

  // With a time on the present there is no moment near a vertical blank: the time is a step of the grid less half a period, and
  // when that has slid across a refresh one frame is on screen a refresh less (the loop makes its frames a little faster than
  // this display shows four refreshes). 6,000 frames of four refreshes at 0.05 % are twelve refreshes of sliding
  settings.PresentsAtTime = true;
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerPeriodOnlyLoop(settings);
  int32_t shorter = 0;
  std::size_t last = 0;
  for (std::size_t index = 100; index < frames.size(); ++index)
  {
    const int64_t refreshes = RefreshesOnScreen(frames, index, period);
    ASSERT_TRUE(refreshes == 4 || refreshes == 3) << index;
    if (refreshes == 3)
    {
      // Never two close together
      ASSERT_TRUE(shorter == 0 || index > last + 300) << index;
      last = index;
      ++shorter;
    }
  }
  EXPECT_EQ(shorter, 12);
}

TEST(TimedPresentLoop, ATimedPresentDoesNotShortenAQueue)
{
  // One refresh per frame on a display 0.2 % slower than its mode, on a timer: the loop makes more frames than the display shows,
  // and with a time on the present they wait as they do without one. What knows of it is a wait for a present
  for (const Timed timed : {Timed::No, Timed::AtTime, Timed::AfterDuration})
  {
    Sim::LoopSettings settings = LightLoop(PC::PacerAim::LowLatency, timed);
    settings.Frames = 3'000;
    settings.DisplayPeriodPpm = 2'000;
    EXPECT_EQ(Sim::SimulateTimerPeriodOnlyLoop(settings).back().PendingAtStart, 6) << static_cast<int32_t>(timed);
    EXPECT_LE(Sim::SimulateTimerWaitForPresentLoop(settings).back().PendingAtStart, 1) << static_cast<int32_t>(timed);
  }
}

TEST(TimedPresentLoop, AfterARefreshTheDisplayLosesATimeOnThePresentBringsTheNextFrameBackAndAMinimumDurationDoesNot)
{
  // 120 frames a second at 240 Hz with vertical blank times and no wait for a present: the display takes no frame at one
  // vertical blank that a frame was for, and nothing tells the pacer
  int64_t lostBlank = 0;
  for (const Timed timed : {Timed::No, Timed::AtTime, Timed::AfterDuration})
  {
    Sim::LoopSettings settings = LightLoop(PC::PacerAim::LowLatency, timed);
    settings.Frames = 600;
    settings.PreferredSwapInterval = 2;
    const int64_t period = PeriodNanoseconds(settings);
    if (lostBlank == 0)
    {
      // The vertical blank the 300th frame is shown at when the display takes every frame
      const std::vector<Sim::LoopFrame> undisturbed = Sim::SimulateVBlankPeriodOnlyLoop(settings);
      lostBlank = (undisturbed[300].ShownNanoseconds - settings.Display.FirstBlankNanoseconds + (period / 2)) / period;
    }
    settings.Display.HeldBlanks = {lostBlank};
    const std::vector<Sim::LoopFrame> frames = Sim::SimulateVBlankPeriodOnlyLoop(settings);
    // That frame is on screen a refresh late: the frame before it for three refreshes
    ASSERT_EQ(RefreshesOnScreen(frames, 300, period), 3) << static_cast<int32_t>(timed);
    if (timed == Timed::AfterDuration)
    {
      // Every frame after it stays two refreshes behind the one before it, so each is a refresh later than it was made for
      for (std::size_t index = 301; index < frames.size(); ++index)
      {
        ASSERT_EQ(RefreshesOnScreen(frames, index, period), 2) << index;
        ASSERT_EQ(RefreshesAfterIntended(frames[index], period), 1) << index;
      }
    }
    else
    {
      // The next frame is shown at the vertical blank it was made for, a refresh after the late one, and the rest as before
      ASSERT_EQ(RefreshesOnScreen(frames, 301, period), 1) << static_cast<int32_t>(timed);
      for (std::size_t index = 302; index < frames.size(); ++index)
      {
        ASSERT_EQ(RefreshesOnScreen(frames, index, period), 2) << index;
        ASSERT_EQ(RefreshesAfterIntended(frames[index], period), 0) << index;
      }
    }
  }
}

TEST(TimedPresentLoop, WithATimeOnThePresentTheDisplayShowsWhatThePacerWorkedOutAndNoSooner)
{
  // Two frames in flight, work of 72 % of a refresh on the CPU and on the GPU, reported: a frame is ready 1.44 refreshes after its
  // start, and it starts when the wait for a present returns, up to 0.58 of a refresh after the display took that frame. The
  // pacer counts a frame as ready for a vertical blank when it is ready the frame margin before it (an eighth of a refresh here)
  Sim::LoopSettings settings = LightLoop(PC::PacerAim::Smoothness, Timed::No);
  settings.Frames = 3'000;
  settings.MaxFramesInFlight = 2;
  settings.WaitsForPreviousGpuWork = false;
  settings.ReportsGpuWork = true;
  const int64_t period = PeriodNanoseconds(settings);
  settings.CpuWork = {3'000'000, 3'000'000};
  settings.GpuWork = {(period * 72) / 100, (period * 72) / 100};
  const auto slowed = [](const std::vector<Sim::LoopFrame>& frames)
  {
    int32_t count = 0;
    for (std::size_t index = 100; index < frames.size(); ++index)
    {
      count += frames[index].SwapInterval > 1 ? 1 : 0;
    }
    return count;
  };

  // A display that takes a frame up to its vertical blank shows the frames that are ready inside the margin a refresh sooner
  // than the pacer has them. Without a timed present that is what is on screen, and no frame is slowed down
  EXPECT_EQ(slowed(Sim::SimulateVBlankWaitForPresentLoop(settings)), 0);
  // With a time on the present the display does what the pacer worked out: those frames are shown a refresh later, they are
  // late, and the rule slows down
  settings.PresentsAtTime = true;
  EXPECT_GT(slowed(Sim::SimulateVBlankWaitForPresentLoop(settings)), 1'000);

  // On a display that takes a frame the margin before its vertical blank the pacer has it right, and the two agree
  settings.Display.LatchLeadNanoseconds = (period * 13) / 100;
  const int32_t timed = slowed(Sim::SimulateVBlankWaitForPresentLoop(settings));
  settings.PresentsAtTime = false;
  const int32_t untimed = slowed(Sim::SimulateVBlankWaitForPresentLoop(settings));
  EXPECT_GT(untimed, 1'000);
  EXPECT_NEAR(timed, untimed, 100);
}
