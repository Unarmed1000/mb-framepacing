// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. A change of the active capabilities in the middle of a run, on the simulation's display (SimulateTierLoop): what
// the display shows around the handover from one way of pacing to another, with the frames that are on their way. The unit
// tests of the handover (TierPacerTests) have no display; the faults a real system showed first (a frame made ahead again, a
// first frame back to back with the last one) are ones only a display shows.
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <array>
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
  constexpr int32_t ChangeFrame = 400;
  constexpr std::size_t Settled = 100;

  //! What is active of vertical blank times and the wait for a present: the four ways a frame is paced without a timed present
  constexpr std::array<PacerCapability, 4> Kinds = {PacerCapability::NoCapabilities, PacerCapability::WaitForPresent, PacerCapability::VBlankTimes,
                                                    PacerCapability::VBlankTimes | PacerCapability::WaitForPresent};

  constexpr PacerCapability Both = PacerCapability::VBlankTimes | PacerCapability::WaitForPresent;

  //! Light work at 240 Hz (a tenth of a refresh on the CPU, a fifth on the GPU), no pause after start-up
  Sim::LoopSettings Loop(const PC::PacerAim aim, const uint32_t swapInterval)
  {
    Sim::LoopSettings settings;
    settings.Frames = 800;
    settings.Aim = aim;
    settings.PreferredSwapInterval = swapInterval;
    settings.StartupPauseRefreshes = 0;
    const int64_t period = PC::RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToNanosecondTimeSpan().Nanoseconds();
    settings.GpuWork = {period / 5, period / 5};
    return settings;
  }

  int64_t PeriodNanoseconds(const Sim::LoopSettings& settings)
  {
    return PC::RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToNanosecondTimeSpan().Nanoseconds();
  }

  //! The frames from first on that were not shown their swap interval after the frame before them
  int32_t OffTheSwapInterval(const std::vector<Sim::LoopFrame>& frames, const std::size_t first, const int64_t periodNanoseconds)
  {
    int32_t off = 0;
    for (std::size_t index = first; index < frames.size(); ++index)
    {
      const int64_t refreshes = ((frames[index].ShownNanoseconds - frames[index - 1].ShownNanoseconds) + (periodNanoseconds / 2)) / periodNanoseconds;
      off += refreshes != int64_t{frames[index].SwapInterval} ? 1 : 0;
    }
    return off;
  }

  //! The most frames that waited when a frame started, from first on
  int32_t MostWaiting(const std::vector<Sim::LoopFrame>& frames, const std::size_t first)
  {
    int32_t most = 0;
    for (std::size_t index = first; index < frames.size(); ++index)
    {
      most = std::max(most, frames[index].PendingAtStart);
    }
    return most;
  }
}

TEST(ActiveSetChangeLoop, ARunThatBeginsWithOneSetActiveIsTheRunOfThatSet)
{
  // The application has both capabilities and has one set of them active from the first frame on: the frames are the ones
  // an application that has only that set gets
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    for (const PacerCapability kind : Kinds)
    {
      Sim::LoopSettings settings = Loop(aim, 1);
      const std::vector<Sim::LoopFrame> expected = Sim::SimulateTierLoop(settings, kind);
      settings.ActiveSetChanges = {{0, kind}};
      const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, Both);
      ASSERT_EQ(frames.size(), expected.size());
      for (std::size_t index = 0; index < frames.size(); ++index)
      {
        ASSERT_EQ(frames[index].StartNanoseconds, expected[index].StartNanoseconds) << static_cast<uint32_t>(kind) << ' ' << index;
        ASSERT_EQ(frames[index].ShownNanoseconds, expected[index].ShownNanoseconds) << static_cast<uint32_t>(kind) << ' ' << index;
        ASSERT_EQ(frames[index].ActiveCapabilities, static_cast<uint32_t>(kind)) << index;
      }
    }
  }
}

TEST(ActiveSetChangeLoop, EveryChangeOfTheActiveSetLeavesEveryFrameOnScreenForItsSwapIntervalAndNoFrameMoreWaiting)
{
  // Each of the twelve changes between the four ways of pacing, with both aims, at one, two and four refreshes per frame, made
  // in the middle of a run of light work
  for (const uint32_t swapInterval : {1u, 2u, 4u})
  {
    for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
    {
      // What a run of each way by itself has waiting when a frame starts, once it has settled
      std::array<int32_t, 4> waitingAlone{};
      for (std::size_t kind = 0; kind < Kinds.size(); ++kind)
      {
        waitingAlone[kind] = MostWaiting(Sim::SimulateTierLoop(Loop(aim, swapInterval), Kinds[kind]), Settled);
      }
      for (std::size_t from = 0; from < Kinds.size(); ++from)
      {
        for (std::size_t to = 0; to < Kinds.size(); ++to)
        {
          if (from == to)
          {
            continue;
          }
          Sim::LoopSettings settings = Loop(aim, swapInterval);
          settings.ActiveSetChanges = {{0, Kinds[from]}, {ChangeFrame, Kinds[to]}};
          const int64_t period = PeriodNanoseconds(settings);
          const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, Both);
          const auto change = static_cast<std::size_t>(ChangeFrame);
          SCOPED_TRACE(testing::Message() << swapInterval << " refreshes, aim " << static_cast<int32_t>(aim) << ", "
                                          << static_cast<uint32_t>(Kinds[from]) << " -> " << static_cast<uint32_t>(Kinds[to]));

          ASSERT_EQ(frames[change - 1u].ActiveCapabilities, static_cast<uint32_t>(Kinds[from]));
          ASSERT_EQ(frames[change].ActiveCapabilities, static_cast<uint32_t>(Kinds[to]));
          // No frame is on screen a refresh more or less than its swap interval: not before the change, not at it, not after
          EXPECT_EQ(OffTheSwapInterval(frames, Settled, period), 0);
          // The swap interval stays, the frame ids go on, and the animation steps by the swap interval
          for (std::size_t index = change - 2u; index < change + 20u; ++index)
          {
            ASSERT_EQ(frames[index].SwapInterval, swapInterval) << index;
            ASSERT_EQ(frames[index].FrameId, index + 1u) << index;
            ASSERT_NEAR(static_cast<double>(frames[index].AnimationStepNanoseconds), static_cast<double>(int64_t{swapInterval} * period), 2.0)
              << index;
          }
          // The animation time does not fall behind the clock by the change
          EXPECT_EQ(frames.back().RefreshesBehindClock, frames[change - 1u].RefreshesBehindClock);
          // And no frame more waits than in a run of the new way by itself: the part that takes over makes none again, and
          // starts none back to back with the last one
          EXPECT_LE(MostWaiting(frames, change + 20u), waitingAlone[to]);
        }
      }
    }
  }
}
