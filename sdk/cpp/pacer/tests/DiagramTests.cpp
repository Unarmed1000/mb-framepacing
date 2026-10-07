// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The timing diagrams of mb-framepacing-explained (tools/timing_diagrams/generate_diagrams.py, doc/frame-pacing-strategies.md) as tests:
// their frames, swap intervals and clock readings, and the display refreshes and animation times they show. A diagram's frame model: 60 Hz,
// the first frame starts 0.2 refresh into the refresh before it is shown, every other frame when the previous one is shown; a frame is
// shown at the first refresh after it is done, and no sooner than its swap interval after the previous one. The vsync timer (the
// refresh clock) animates it for the previous frame's display plus its swap interval.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/clock/AnimationTime.hpp>
#include <mb/framepacing/pacer/clock/PacerRefreshClock.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <cstdint>
#include <cstdlib>
#include <string>
#include <utility>
#include <vector>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  const PC::RefreshPeriod g_hz60 = PC::RefreshPeriod::FromRate(60);
  //! A hundredth of a 60 Hz refresh, the diagrams' unit, as a period of its own
  const PC::RefreshPeriod g_unit = PC::RefreshPeriod::FromRate(6'000);
  //! The diagrams' unit: 100 is one refresh
  constexpr int64_t Period = 100;
  constexpr int64_t FirstStart = 20;
  //! Refresh 0 on the steady clock
  constexpr int64_t Origin = 10 * FP::NanosecondTimeSpan::NanosecondsPerSecond;

  //! A diagram's frame: its render time (units), its swap interval, and its clock reading's error (vsync timer diagrams, nanoseconds)
  struct DiagramFrame
  {
    char Name{'A'};
    int64_t Render{75};
    int64_t SwapInterval{1};
    int64_t TimerErrorNanoseconds{0};
  };

  //! A diagram frame's times: when it starts, when it is shown (whole refreshes), and the vsync timer's animation time (refreshes)
  struct FrameTimes
  {
    int64_t Start{0};
    int64_t Shown{0};
    int64_t Animation{0};
  };

  //! A time in units as nanoseconds on the steady clock
  int64_t Nanoseconds(const int64_t units) noexcept
  {
    return units >= 0 ? Origin + g_unit.TimeFor(units).Nanoseconds() : Origin - g_unit.TimeFor(-units).Nanoseconds();
  }

  //! a / b rounded up (b > 0)
  int64_t CeilDiv(const int64_t a, const int64_t b) noexcept
  {
    return a >= 0 ? (a + b - 1) / b : -((-a) / b);
  }

  //! generate_diagrams.py's simulate (not VRR, no CPU cap): the times of every frame
  std::vector<FrameTimes> Simulate(const std::vector<DiagramFrame>& frames)
  {
    std::vector<FrameTimes> times;
    int64_t previousShown = -frames.front().SwapInterval * Period;
    for (std::size_t index = 0; index < frames.size(); ++index)
    {
      const DiagramFrame& frame = frames[index];
      const int64_t start = previousShown + (index == 0 ? FirstStart : 0);
      const int64_t end = start + frame.Render;
      const int64_t shown = std::max(CeilDiv(end, Period), (previousShown / Period) + frame.SwapInterval);
      times.push_back({start, shown, (previousShown / Period) + frame.SwapInterval});
      previousShown = shown * Period;
    }
    return times;
  }

  std::vector<DiagramFrame> Frames(const std::string& names, const std::vector<int64_t>& renders, const std::vector<int64_t>& intervals)
  {
    std::vector<DiagramFrame> frames;
    frames.reserve(names.size());
    for (std::size_t index = 0; index < names.size(); ++index)
    {
      frames.push_back({names[index], renders[index], intervals[index], 0});
    }
    return frames;
  }

  //! The vsync timer without a pacer: every frame's measured start (when it starts, or its clock reading) and swap interval
  std::vector<int64_t> MeasuredAnimation(const std::vector<DiagramFrame>& frames, const std::vector<FrameTimes>& times)
  {
    PC::PacerRefreshClock clock(g_hz60, FP::NanosecondTimeSpan(2 * FP::NanosecondTimeSpan::NanosecondsPerSecond));
    std::vector<int64_t> animation;
    for (std::size_t index = 0; index < frames.size(); ++index)
    {
      const int64_t start = Nanoseconds(times[index].Start) + frames[index].TimerErrorNanoseconds;
      const PC::AnimationTime time = clock.Advance(FP::NanosecondTickCount(start), static_cast<uint32_t>(frames[index].SwapInterval));
      animation.push_back(g_hz60.NearestRefreshes(time.Time));
    }
    return animation;
  }

  std::vector<int64_t> Column(const std::vector<FrameTimes>& times, int64_t FrameTimes::* member)
  {
    std::vector<int64_t> values;
    values.reserve(times.size());
    for (const FrameTimes& time : times)
    {
      values.push_back(time.*member);
    }
    return values;
  }

  // The frame sets of generate_diagrams.py (render times in units: 75 is 12.5 ms, 125 is 20.8 ms)
  const std::vector<int64_t> g_busy{75, 125, 75, 125, 75, 75, 75, 75};
  const std::vector<int64_t> g_hitch{75, 125, 75, 75, 75, 75, 75};
}

TEST(Diagrams, ModelMatchesTheDiagramsSlowFrames)
{
  // slow-frames: a missed vsync still costs one late frame, and the frame after it catches up exactly
  const auto frames = Frames("ABCDEF", {75, 125, 75, 75, 125, 75}, {1, 1, 1, 1, 1, 1});
  const auto times = Simulate(frames);
  EXPECT_EQ(Column(times, &FrameTimes::Shown), (std::vector<int64_t>{0, 2, 3, 4, 6, 7}));
  EXPECT_EQ(Column(times, &FrameTimes::Animation), (std::vector<int64_t>{0, 1, 3, 4, 5, 7}));
}

TEST(Diagrams, TheVsyncTimerRemovesTheClocksJitter)
{
  // vsync-timer: the naive clock's readings are off by 0, 0.2, 2.4, -1, 0.8, -1, 0.6 and 0.2 ms; rounded to refreshes, every step is one
  auto frames = Frames("ABCDEFGH", {75, 75, 75, 75, 75, 75, 75, 75}, {1, 1, 1, 1, 1, 1, 1, 1});
  const std::vector<int64_t> errorsMicroseconds{0, 200, 2'400, -1'000, 800, -1'000, 600, 200};
  for (std::size_t index = 0; index < frames.size(); ++index)
  {
    frames[index].TimerErrorNanoseconds = errorsMicroseconds[index] * 1'000;
  }
  const auto times = Simulate(frames);
  EXPECT_EQ(MeasuredAnimation(frames, times), (std::vector<int64_t>{0, 1, 2, 3, 4, 5, 6, 7}));
  EXPECT_EQ(Column(times, &FrameTimes::Shown), (std::vector<int64_t>{0, 1, 2, 3, 4, 5, 6, 7}));
}

TEST(Diagrams, TheVsyncTimerCatchesUpAfterAMissedVsync)
{
  const auto frames = Frames("ABCDEF", {75, 125, 75, 75, 125, 75}, {1, 1, 1, 1, 1, 1});
  const auto times = Simulate(frames);
  EXPECT_EQ(MeasuredAnimation(frames, times), (std::vector<int64_t>{0, 1, 3, 4, 5, 7}));
}

TEST(Diagrams, HalfRateHoldsEveryFrameForTwoRefreshes)
{
  // half-rate-even: 25 ms frames at a swap interval of 2
  const auto frames = Frames("ABCD", {150, 150, 150, 150}, {2, 2, 2, 2});
  const auto times = Simulate(frames);
  EXPECT_EQ(Column(times, &FrameTimes::Shown), (std::vector<int64_t>{0, 2, 4, 6}));
  EXPECT_EQ(MeasuredAnimation(frames, times), (std::vector<int64_t>{0, 2, 4, 6}));
}

TEST(Diagrams, SwitchingWithoutHysteresisAnimatesEveryIntervalChangeExactly)
{
  // switching-naive: four errors of 16.7 ms, all display errors; the animation follows the swap interval at every change (C and E step
  // three refreshes after two were measured, D and F one)
  const auto frames = Frames("ABCDEFGH", g_busy, {1, 1, 2, 1, 2, 1, 1, 1});
  const auto times = Simulate(frames);
  EXPECT_EQ(Column(times, &FrameTimes::Shown), (std::vector<int64_t>{0, 2, 4, 6, 8, 9, 10, 11}));
  EXPECT_EQ(MeasuredAnimation(frames, times), (std::vector<int64_t>{0, 1, 4, 5, 8, 9, 10, 11}));
}

TEST(Diagrams, SwitchingWithHysteresisLeavesOnlyTheFirstSlowFrameLate)
{
  // switching-hysteresis (back to full rate after three fast frames): only B is late
  const auto frames = Frames("ABCDEFGH", g_busy, {1, 1, 2, 2, 2, 2, 2, 1});
  const auto times = Simulate(frames);
  EXPECT_EQ(Column(times, &FrameTimes::Shown), (std::vector<int64_t>{0, 2, 4, 6, 8, 10, 12, 13}));
  EXPECT_EQ(MeasuredAnimation(frames, times), (std::vector<int64_t>{0, 1, 4, 6, 8, 10, 12, 13}));
}

TEST(Diagrams, RecoveringAtHalfRateAndWithPerFrameTargets)
{
  // recovery-half-rate: C, D and E held for two refreshes
  const auto halfRate = Frames("ABCDEFG", g_hitch, {1, 1, 2, 2, 2, 1, 1});
  const auto halfRateTimes = Simulate(halfRate);
  EXPECT_EQ(Column(halfRateTimes, &FrameTimes::Shown), (std::vector<int64_t>{0, 2, 4, 6, 8, 9, 10}));
  EXPECT_EQ(MeasuredAnimation(halfRate, halfRateTimes), (std::vector<int64_t>{0, 1, 4, 6, 8, 9, 10}));
  // recovery-targeting: back at full rate a frame sooner (E steps one refresh, although two were measured)
  const auto targeting = Frames("ABCDEFG", g_hitch, {1, 1, 2, 2, 1, 1, 1});
  const auto targetingTimes = Simulate(targeting);
  EXPECT_EQ(Column(targetingTimes, &FrameTimes::Shown), (std::vector<int64_t>{0, 2, 4, 6, 7, 8, 9}));
  EXPECT_EQ(MeasuredAnimation(targeting, targetingTimes), (std::vector<int64_t>{0, 1, 4, 6, 7, 8, 9}));
}

TEST(Diagrams, ThePacerPlansTheDiagramsFramesAtAFixedSwapInterval)
{
  // slow-frames and half-rate-even paced by the pacer (its interval fixed): it aims every frame where the diagram's frame is aimed,
  // measures the late frames from the frame starts, and animates each for its predicted display time
  for (const auto& [frames, interval] : {std::pair{Frames("ABCDEF", {75, 125, 75, 75, 125, 75}, {1, 1, 1, 1, 1, 1}), int64_t{1}},
                                         std::pair{Frames("ABCD", {150, 150, 150, 150}, {2, 2, 2, 2}), int64_t{2}}})
  {
    PC::PacerSettings settings(g_hz60);
    settings.SetPreferredSwapInterval(static_cast<uint32_t>(interval));
    settings.SetAutoSwapInterval(false);
    PC::FramePacer pacer(settings);
    const auto times = Simulate(frames);
    uint32_t lateFrames = 0;
    for (std::size_t index = 0; index < frames.size(); ++index)
    {
      const int64_t start = Nanoseconds(times[index].Start);
      const PC::FrameSchedule schedule = pacer.BeginFrame(FP::NanosecondTickCount(start));
      const int64_t work = Nanoseconds(frames[index].Render) - Origin;
      static_cast<void>(pacer.EndFrame(FP::NanosecondTickCount(start + work), FP::NanosecondTimeSpan(work)));
      // Every frame but the first starts when the previous one is shown, so it is aimed at the refresh the diagram aims it at, to the nanosecond
      // the two roundings allow. The first starts 0.2 refresh into its refresh: without a vsync time the pacer cannot know that
      if (index > 0)
      {
        EXPECT_LE(std::abs(schedule.IntendedDisplayTime.Nanoseconds() - Nanoseconds(times[index].Animation * Period)), 1) << frames[index].Name;
        lateFrames += times[index - 1].Shown > times[index - 1].Animation ? 1u : 0u;
      }
      EXPECT_EQ(g_hz60.NearestRefreshes(schedule.AnimationTime), times[index].Animation - times[0].Animation) << frames[index].Name;
      EXPECT_EQ(pacer.FrameWindow().LateFrames, lateFrames) << frames[index].Name;
    }
  }
}
