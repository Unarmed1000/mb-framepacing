// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. A start the display's side held, on vertical blanks, on the simulation's display (the proposal's decision 17): a
// display that takes no frame for a while, a swap chain whose images are then all in use, and a loop that its wait for an image
// holds. The simulation only: one system showed the case at the start of its runs, and has not run this yet.
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <gtest/gtest.h>
#include <cstddef>
#include <cstdint>
#include <vector>
#include "FrameLoopSimulation.hpp"
#include "LoopFrame.hpp"
#include "LoopSettings.hpp"

namespace PC = MB::FramePacing::Pacer;
namespace Sim = MB::FramePacing::Pacer::Simulation;
using PC::PacerCapability;

namespace
{
  constexpr std::size_t Settled = 100;

  //! Light work at 240 Hz on a swap chain of three images, whose display takes no frame at six vertical blanks in a row
  Sim::LoopSettings Loop(const PC::PacerAim aim)
  {
    Sim::LoopSettings settings;
    settings.Frames = 600;
    settings.Aim = aim;
    settings.StartupPauseRefreshes = 0;
    const int64_t period = PC::RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToNanosecondTimeSpan().Nanoseconds();
    settings.GpuWork = {period / 5, period / 5};
    settings.Display.Images = 3;
    settings.Display.HeldBlanks = {200, 201, 202, 203, 204, 205};
    return settings;
  }

  int64_t PeriodNanoseconds(const Sim::LoopSettings& settings)
  {
    return PC::RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToNanosecondTimeSpan().Nanoseconds();
  }

  //! The refreshes of a frame's animation time step
  int64_t AnimationRefreshes(const Sim::LoopFrame& frame, const int64_t periodNanoseconds)
  {
    return (frame.AnimationStepNanoseconds + (periodNanoseconds / 2)) / periodNanoseconds;
  }

  //! The refreshes a frame was shown after the frame before it
  int64_t RefreshesOnScreen(const std::vector<Sim::LoopFrame>& frames, const std::size_t index, const int64_t periodNanoseconds)
  {
    return ((frames[index].ShownNanoseconds - frames[index - 1].ShownNanoseconds) + (periodNanoseconds / 2)) / periodNanoseconds;
  }
}

TEST(DisplayHeldLoop, WithVerticalBlankTimesAStartTheAcquireHeldIsNotSteppedOverOnceThePacerIsToldOfTheWait)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    Sim::LoopSettings settings = Loop(aim);
    const int64_t period = PeriodNanoseconds(settings);
    SCOPED_TRACE(testing::Message() << "aim " << static_cast<int32_t>(aim));

    // Not told of the loop's waits: the frame that starts after the display took frames again starts six refreshes late,
    // and the animation time steps over them. On screen that frame follows the one before it by one refresh, as the frames
    // made before were still on their way: the animation jumps where nothing was held
    const std::vector<Sim::LoopFrame> untold = Sim::SimulateTierLoop(settings, PacerCapability::VBlankTimes);
    int32_t jumps = 0;
    for (std::size_t index = Settled; index < untold.size(); ++index)
    {
      const int64_t step = AnimationRefreshes(untold[index], period);
      ASSERT_TRUE(step == 1 || step >= 5) << index;
      if (step >= 5)
      {
        ++jumps;
        EXPECT_EQ(RefreshesOnScreen(untold, index, period), 1) << index;
      }
    }
    EXPECT_EQ(jumps, 1);

    // Told of them: every frame's animation time is a refresh after the one before it, and the refreshes the loop was held
    // for are behind the clock. What the display did is the same: one frame on screen for seven refreshes, the others for one
    settings.SystemHoldsLoop = true;
    const std::vector<Sim::LoopFrame> told = Sim::SimulateTierLoop(settings, PacerCapability::VBlankTimes);
    ASSERT_EQ(told.size(), untold.size());
    int32_t heldOnScreen = 0;
    for (std::size_t index = Settled; index < told.size(); ++index)
    {
      ASSERT_EQ(AnimationRefreshes(told[index], period), 1) << index;
      ASSERT_EQ(told[index].ShownNanoseconds, untold[index].ShownNanoseconds) << index;
      const int64_t onScreen = RefreshesOnScreen(told, index, period);
      ASSERT_TRUE(onScreen == 1 || onScreen == 7) << index;
      heldOnScreen += onScreen == 7 ? 1 : 0;
    }
    EXPECT_EQ(heldOnScreen, 1);
    EXPECT_GE(told.back().RefreshesBehindClock, untold.back().RefreshesBehindClock + 5u);
    // The frame the display's side held is no late frame to the rule
    EXPECT_EQ(told.back().WindowLateFrames, 0u);
    EXPECT_EQ(told.back().SwapInterval, 1u);
  }
}

TEST(DisplayHeldLoop, WithAWaitForAPresentTheWaitAlreadySaysSo)
{
  // Vertical blank times and the wait: the wait says where the frames were shown, and no frame's animation time steps over
  // the refreshes, told of the loop's waits or not
  for (const bool tellsOfWaits : {false, true})
  {
    Sim::LoopSettings settings = Loop(PC::PacerAim::Smoothness);
    settings.SystemHoldsLoop = tellsOfWaits;
    const int64_t period = PeriodNanoseconds(settings);
    const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PacerCapability::VBlankTimes | PacerCapability::WaitForPresent);
    for (std::size_t index = Settled; index < frames.size(); ++index)
    {
      ASSERT_EQ(AnimationRefreshes(frames[index], period), 1) << tellsOfWaits << ' ' << index;
    }
  }
}
