// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The tier pacer at the refresh rates monitors have, 50 to 540 Hz, in the simulation's loop (SimulateTierLoop): the
// four ways of pacing that have a pacer without a time on the present, both aims. What the pacer this one replaced was checked
// for at those rates, as far as it carries over. The simulation only.
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <gtest/gtest.h>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <string>
#include <vector>
#include "FrameLoopSimulation.hpp"
#include "LoopFrame.hpp"
#include "LoopSettings.hpp"

namespace PC = MB::FramePacing::Pacer;
namespace Sim = MB::FramePacing::Pacer::Simulation;
using PC::PacerCapability;

namespace
{
  struct MonitorRate
  {
    uint32_t Numerator{0};
    uint32_t Denominator{1};

    [[nodiscard]] std::string Name() const
    {
      return std::to_string(Numerator) + (Denominator == 1 ? "" : "/" + std::to_string(Denominator)) + " Hz";
    }
  };

  constexpr std::array<MonitorRate, 23> MonitorRates{{{50},  {60'000, 1'001}, {60},  {72},  {75},  {85},  {90},  {100}, {120'000, 1'001},
                                                      {120}, {144},           {160}, {165}, {170}, {180}, {200}, {240}, {280},
                                                      {300}, {360},           {480}, {500}, {540}}};

  //! The four ways of pacing without a time on the present
  constexpr std::array<PacerCapability, 4> Ways = {PacerCapability::NoCapabilities, PacerCapability::WaitForPresent, PacerCapability::VBlankTimes,
                                                   PacerCapability::VBlankTimes | PacerCapability::WaitForPresent};

  constexpr std::array<PC::PacerAim, 2> Aims = {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness};

  int64_t PeriodNanoseconds(const MonitorRate rate)
  {
    return PC::RefreshPeriod::FromRate(rate.Numerator, rate.Denominator).ToNanosecondTimeSpan().Nanoseconds();
  }

  //! A loop at that rate with light work: a tenth of a refresh on the CPU and a fifth on the GPU, no pause after start-up
  Sim::LoopSettings Loop(const MonitorRate rate, const PC::PacerAim aim)
  {
    Sim::LoopSettings settings;
    settings.RateNumerator = rate.Numerator;
    settings.RateDenominator = rate.Denominator;
    settings.Frames = 1'500;
    settings.Aim = aim;
    settings.StartupPauseRefreshes = 0;
    const int64_t period = PeriodNanoseconds(rate);
    settings.CpuWork = {period / 10, period / 10};
    settings.GpuWork = {period / 5, period / 5};
    settings.LoopNanoseconds = period / 100;
    settings.PresentWaitReturn = {period / 100, period / 4};
    return settings;
  }

  //! The refreshes a frame was on screen after the frame before it, on a display of that period
  int64_t RefreshesOnScreen(const std::vector<Sim::LoopFrame>& frames, const std::size_t index, const int64_t periodNanoseconds)
  {
    return ((frames[index].ShownNanoseconds - frames[index - 1].ShownNanoseconds) + (periodNanoseconds / 2)) / periodNanoseconds;
  }

  std::string NameOf(const MonitorRate rate, const PacerCapability way, const PC::PacerAim aim)
  {
    return rate.Name() + ", way " + std::to_string(static_cast<uint32_t>(way)) + (aim == PC::PacerAim::Smoothness ? ", smoothness" : ", low latency");
  }
}

TEST(TierMonitorRates, FramesOnTimeAreOneRefreshEachAtEveryRate)
{
  for (const MonitorRate rate : MonitorRates)
  {
    for (const PacerCapability way : Ways)
    {
      for (const PC::PacerAim aim : Aims)
      {
        SCOPED_TRACE(NameOf(rate, way, aim));
        const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(Loop(rate, aim), way);
        const int64_t period = PeriodNanoseconds(rate);
        int64_t wrongFrames = 0;
        for (std::size_t index = 100; index < frames.size(); ++index)
        {
          const bool right = frames[index].SwapInterval == 1u && RefreshesOnScreen(frames, index, period) == 1 &&
                             std::abs(frames[index].AnimationStepNanoseconds - period) <= 1;
          wrongFrames += right ? 0 : 1;
        }
        EXPECT_EQ(wrongFrames, 0);
        EXPECT_EQ(frames.back().WindowLateFrames, 0u);
      }
    }
  }
}

TEST(TierMonitorRates, ALongerSwapIntervalHoldsEveryFrameForItsRefreshesAtEveryRate)
{
  for (const MonitorRate rate : MonitorRates)
  {
    for (const PacerCapability way : Ways)
    {
      for (const PC::PacerAim aim : Aims)
      {
        for (const uint32_t swapInterval : {2u, 3u})
        {
          SCOPED_TRACE(NameOf(rate, way, aim) + ", swap interval " + std::to_string(swapInterval));
          Sim::LoopSettings settings = Loop(rate, aim);
          settings.PreferredSwapInterval = swapInterval;
          const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, way);
          const int64_t period = PeriodNanoseconds(rate);
          int64_t wrongFrames = 0;
          for (std::size_t index = 100; index < frames.size(); ++index)
          {
            const bool right = frames[index].SwapInterval == swapInterval && RefreshesOnScreen(frames, index, period) == int64_t{swapInterval} &&
                               std::abs(frames[index].AnimationStepNanoseconds - (int64_t{swapInterval} * period)) <= int64_t{swapInterval};
            wrongFrames += right ? 0 : 1;
          }
          EXPECT_EQ(wrongFrames, 0);
          EXPECT_EQ(frames.back().WindowLateFrames, 0u);
        }
      }
    }
  }
}

TEST(TierMonitorRates, OnVerticalBlanksADisplayATenthOfAPercentOffItsRateNeverHitches)
{
  // The display's own period is a thousandth off the one the pacer is told. With vertical blank times the frames follow the
  // display: every one is on screen for one of its refreshes, and no frame's animation time steps two refreshes
  for (const MonitorRate rate : MonitorRates)
  {
    for (const PacerCapability way : {PacerCapability::VBlankTimes, PacerCapability::VBlankTimes | PacerCapability::WaitForPresent})
    {
      for (const PC::PacerAim aim : Aims)
      {
        for (const int64_t ppm : {int64_t{1'000}, int64_t{-1'000}})
        {
          SCOPED_TRACE(NameOf(rate, way, aim) + (ppm > 0 ? ", the display slower" : ", the display faster"));
          Sim::LoopSettings settings = Loop(rate, aim);
          settings.DisplayPeriodPpm = ppm;
          const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, way);
          const int64_t period = PeriodNanoseconds(rate);
          const int64_t displayPeriod = period + ((period * ppm) / 1'000'000);
          int64_t wrongFrames = 0;
          for (std::size_t index = 100; index < frames.size(); ++index)
          {
            // One refresh of the display on screen, and an animation step of a refresh as the pacer has it, to a thousandth
            const bool right = frames[index].SwapInterval == 1u && RefreshesOnScreen(frames, index, displayPeriod) == 1 &&
                               std::abs(frames[index].AnimationStepNanoseconds - period) <= (period / 500);
            wrongFrames += right ? 0 : 1;
          }
          EXPECT_EQ(wrongFrames, 0);
          EXPECT_EQ(frames.back().WindowLateFrames, 0u);
        }
      }
    }
  }
}
