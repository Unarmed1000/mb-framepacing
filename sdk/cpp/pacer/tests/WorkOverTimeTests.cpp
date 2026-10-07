// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Frames that work longer than their swap interval's time, in a loop no vsync holds: a swap chain with room takes the present at once,
// so the next frame starts when the work is done and not on a refresh. Such a frame is late whatever the frame starts say, and the
// time between the starts counts as real time. The refresh clock on its own, the pacer, and a frame log of a real swap chain
// (test-data/pacer/120-vulkan-work-130-log.csv).
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/clock/FrameMeasurement.hpp>
#include <mb/framepacing/pacer/clock/PacerRefreshClock.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <gtest/gtest.h>
#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <optional>
#include <sstream>
#include <string>
#include <vector>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  constexpr int64_t Second = FP::NanosecondTimeSpan::NanosecondsPerSecond;
  constexpr int64_t StartNanoseconds = 100 * Second;

  const PC::RefreshPeriod g_hz60 = PC::RefreshPeriod::FromRate(60);
  const PC::RefreshPeriod g_hz120 = PC::RefreshPeriod::FromRate(120);

  FP::NanosecondTimeSpan Span(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTimeSpan(nanoseconds);
  }

  FP::NanosecondTickCount At(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTickCount(nanoseconds);
  }

  //! A share of a refresh period, in hundredths
  FP::NanosecondTimeSpan Share(const PC::RefreshPeriod period, const int64_t hundredths) noexcept
  {
    return Span(period.ToNanosecondTimeSpan().Nanoseconds() * hundredths / 100);
  }

  //! What a loop that no vsync holds gave: the pacer's count against the time that passed
  struct LoopResult
  {
    int64_t Frames{0};
    //! The refreshes the animation stepped, in all
    int64_t AnimationRefreshes{0};
    //! The frames until the rule slowed down, -1: never
    int64_t SlowerAtFrame{-1};
    //! The rule's frame window after the last frame
    uint32_t WindowFrames{0};
    uint32_t LateFrames{0};
    uint32_t SwapInterval{0};
    FP::NanosecondTimeSpan Passed;
  };

  //! Frames that start when the work of the one before is done, each working workPercent of a refresh: no present waits for the
  //! display. The pacer is given the work (the GPU's part of it as the work alone, its EndFrame coming when the CPU is done), or no
  //! EndFrame at all.
  LoopResult RunUnheldLoop(PC::FramePacer& rPacer, const PC::RefreshPeriod period, const int64_t workPercent, const int64_t frames,
                           const bool endFrame = true, const int64_t gpuPercent = 0)
  {
    LoopResult result;
    const FP::NanosecondTimeSpan work = Share(period, workPercent);
    FP::NanosecondTickCount start = At(StartNanoseconds);
    const FP::NanosecondTickCount first = start;
    for (int64_t frame = 0; frame < frames; ++frame)
    {
      const PC::FrameSchedule schedule = rPacer.BeginFrame(start);
      result.AnimationRefreshes += period.NearestRefreshes(schedule.AnimationStep);
      if (schedule.Change == PC::SwapIntervalChange::Slower && result.SlowerAtFrame < 0)
      {
        result.SlowerAtFrame = frame;
      }
      if (endFrame)
      {
        static_cast<void>(rPacer.EndFrame(start + Share(period, workPercent - gpuPercent), work));
      }
      result.Passed = (start + work) - first;
      start += work;
      ++result.Frames;
    }
    result.WindowFrames = rPacer.FrameWindow().Frames;
    result.LateFrames = rPacer.FrameWindow().LateFrames;
    result.SwapInterval = rPacer.SwapInterval();
    return result;
  }

  std::optional<std::filesystem::path> FindTestData()
  {
    for (auto folder = std::filesystem::path(MB_FRAMEPACING_PACER_SOURCE_DIR); !folder.empty(); folder = folder.parent_path())
    {
      auto candidate = folder / "test-data" / "pacer" / "120-vulkan-work-130-log.csv";
      if (std::filesystem::exists(candidate))
      {
        return candidate;
      }
      if (folder == folder.parent_path())
      {
        break;
      }
    }
    return std::nullopt;
  }

  //! A row of the frame log. Times in nanoseconds (the file has ticks); -1 where the platform gave none
  struct LogFrame
  {
    int64_t StartNanoseconds{0};
    int64_t EndFrameNanoseconds{0};
    int64_t WorkNanoseconds{0};
    int64_t DisplayNanoseconds{-1};
  };

  std::vector<LogFrame> ReadLog(const std::filesystem::path& path)
  {
    std::vector<LogFrame> frames;
    std::ifstream file(path, std::ios::binary);
    std::string line;
    std::getline(file, line);
    while (std::getline(file, line))
    {
      std::vector<std::string> fields;
      std::stringstream stream(line + ",");
      std::string field;
      while (std::getline(stream, field, ','))
      {
        fields.push_back(field);
      }
      const auto number = [&fields](const std::size_t index) { return fields[index].empty() ? int64_t{-1} : std::stoll(fields[index]); };
      // The log is in ticks of 100 ns, as it was recorded
      const auto time = [&number](const std::size_t index)
      { return number(index) < 0 ? int64_t{-1} : number(index) * FP::NanosecondTimeSpan::NanosecondsPerTick; };
      frames.push_back({time(1), time(2), time(3), time(4)});
    }
    return frames;
  }
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// PacerRefreshClock::Measure with the previous frame's work
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(WorkOverTime, AFrameThatWorkedLongerThanItsSwapIntervalIsLate)
{
  PC::PacerRefreshClock clock(g_hz60, Span(2 * Second));
  int64_t start = StartNanoseconds;
  static_cast<void>(clock.Measure(At(start)));
  static_cast<void>(clock.Step(1));

  // Work that fits: late only by the frame starts, as without the work
  start += g_hz60.TimeFor(1).Nanoseconds();
  PC::FrameMeasurement measured = clock.Measure(At(start), Share(g_hz60, 90));
  EXPECT_EQ(measured.Refreshes, 1u);
  EXPECT_FALSE(measured.Late);
  static_cast<void>(clock.Step(1));
  // Exactly the swap interval's time still fits
  start += g_hz60.TimeFor(1).Nanoseconds();
  measured = clock.Measure(At(start), g_hz60.TimeFor(1));
  EXPECT_FALSE(measured.Late);
  static_cast<void>(clock.Step(1));

  // 1.3 refreshes of work, and the next frame starts when it is done: one refresh by the frame starts, and late all the same
  start += Share(g_hz60, 130).Nanoseconds();
  measured = clock.Measure(At(start), Share(g_hz60, 130));
  EXPECT_FALSE(measured.Restarted);
  EXPECT_EQ(measured.Refreshes, 1u);
  EXPECT_TRUE(measured.Late);
  // At a swap interval of two the same work fits
  static_cast<void>(clock.Step(2));
  start += g_hz60.TimeFor(2).Nanoseconds();
  measured = clock.Measure(At(start), Share(g_hz60, 130));
  EXPECT_EQ(measured.Refreshes, 2u);
  EXPECT_FALSE(measured.Late);
}

TEST(WorkOverTime, TheRefreshesOfFramesOverTheirTimeAddUpToTheTimeThatPassed)
{
  PC::PacerRefreshClock clock(g_hz60, Span(2 * Second));
  const FP::NanosecondTimeSpan work = Share(g_hz60, 137);
  FP::NanosecondTickCount start = At(StartNanoseconds);
  static_cast<void>(clock.Measure(start));
  static_cast<void>(clock.Step(1));
  // 1.37 refreshes a frame: one refresh, two, one, one, two, ... and never more than half a refresh from the time that passed
  int64_t refreshes = 0;
  int64_t twos = 0;
  for (int64_t frame = 1; frame <= 1'000; ++frame)
  {
    start += work;
    const PC::FrameMeasurement measured = clock.Measure(start, work);
    static_cast<void>(clock.Step(1));
    EXPECT_TRUE(measured.Late);
    ASSERT_TRUE(measured.Refreshes == 1u || measured.Refreshes == 2u) << frame;
    refreshes += measured.Refreshes;
    twos += measured.Refreshes == 2u ? 1 : 0;
    ASSERT_EQ(refreshes, g_hz60.NearestRefreshes(Span(work.Nanoseconds() * frame))) << frame;
  }
  EXPECT_EQ(refreshes, 1'370);
  EXPECT_EQ(twos, 370);

  // Without the work the same starts are one refresh each: the frames' own rounding, as for a loop that vsync holds
  PC::PacerRefreshClock unaware(g_hz60, Span(2 * Second));
  start = At(StartNanoseconds);
  static_cast<void>(unaware.Measure(start));
  static_cast<void>(unaware.Step(1));
  for (int64_t frame = 0; frame < 100; ++frame)
  {
    start += work;
    EXPECT_EQ(unaware.Measure(start).Refreshes, 1u);
    static_cast<void>(unaware.Step(1));
  }
}

TEST(WorkOverTime, WhatRoundingLeftIsDroppedByAFrameThatFitsAndByARestart)
{
  PC::PacerRefreshClock clock(g_hz60, Span(2 * Second));
  const FP::NanosecondTimeSpan over = Share(g_hz60, 140);
  FP::NanosecondTickCount start = At(StartNanoseconds);
  static_cast<void>(clock.Measure(start));
  static_cast<void>(clock.Step(1));
  // 1.4 refreshes: one counted, 0.4 left over. Another would be two (1.8)
  start += over;
  EXPECT_EQ(clock.Measure(start, over).Refreshes, 1u);
  static_cast<void>(clock.Step(1));
  // A frame that fits in between: rounded on its own, and nothing is left for the frame after it
  start += over;
  EXPECT_EQ(clock.Measure(start, Share(g_hz60, 50)).Refreshes, 1u);
  static_cast<void>(clock.Step(1));
  start += over;
  EXPECT_EQ(clock.Measure(start, over).Refreshes, 1u) << "1.4 again, not 1.8";
  static_cast<void>(clock.Step(1));
  start += over;
  EXPECT_EQ(clock.Measure(start, over).Refreshes, 2u) << "1.4 and the 0.4 of the frame before";
  static_cast<void>(clock.Step(1));

  // 0.8 left over... and a restart: the first measurement after it starts from nothing
  start += over;
  EXPECT_EQ(clock.Measure(start, over).Refreshes, 1u) << "1.4 - 0.2";
  static_cast<void>(clock.Step(1));
  start += over;
  EXPECT_EQ(clock.Measure(start, over).Refreshes, 2u) << "1.4 + 0.2";
  static_cast<void>(clock.Step(1));
  clock.Restart();
  start += over;
  EXPECT_TRUE(clock.Measure(start, over).Restarted);
  static_cast<void>(clock.Step(1));
  start += over;
  EXPECT_EQ(clock.Measure(start, over).Refreshes, 1u) << "1.4, with nothing from before the restart";
}

TEST(WorkOverTime, WorkLongerThanTheTimeBetweenTheStartsLeavesNoMoreThanARefresh)
{
  // An application that gives more work than the frame took (the GPU's time of an older frame): the frame counts its swap interval,
  // and what is left over stays within a refresh however long that goes on
  PC::PacerRefreshClock clock(g_hz60, Span(2 * Second));
  FP::NanosecondTickCount start = At(StartNanoseconds);
  static_cast<void>(clock.Measure(start));
  static_cast<void>(clock.Step(1));
  for (int64_t frame = 0; frame < 100; ++frame)
  {
    start += Share(g_hz60, 20);
    const PC::FrameMeasurement measured = clock.Measure(start, Share(g_hz60, 150));
    EXPECT_EQ(measured.Refreshes, 1u);
    EXPECT_TRUE(measured.Late);
    static_cast<void>(clock.Step(1));
  }
  // One refresh is owed, no more: a frame of 2.4 refreshes counts 1.4 of them
  start += Share(g_hz60, 240);
  EXPECT_EQ(clock.Measure(start, Share(g_hz60, 240)).Refreshes, 1u);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// FramePacer
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(WorkOverTime, TheRuleSlowsDownALoopThatNoVsyncHolds)
{
  // Work of 1.3 refreshes, frames 1.3 refreshes apart: by the frame starts alone none of them is late
  PC::FramePacer pacer{PC::PacerSettings(g_hz60)};
  const LoopResult result = RunUnheldLoop(pacer, g_hz60, 130, 60);
  // More late frames than a tenth of a full frame window (12 of 120): the thirteenth, measured as the fourteenth begins
  EXPECT_EQ(result.SlowerAtFrame, 13);
  EXPECT_EQ(result.SwapInterval, 2u);
  // At two refreshes the work fits: the frames after the change are on time (this loop does not wait for them, a real one does)
  EXPECT_EQ(result.LateFrames, 0u);

  // The GPU's time in the work, with EndFrame when the CPU is done: the same
  PC::FramePacer gpuLimited{PC::PacerSettings(g_hz60)};
  EXPECT_EQ(RunUnheldLoop(gpuLimited, g_hz60, 130, 60, true, 110).SlowerAtFrame, 13);
}

TEST(WorkOverTime, AtAFixedSwapIntervalTheAnimationKeepsToTheTimeThatPassed)
{
  for (const int64_t workPercent : {105, 130, 137, 150, 190, 260})
  {
    SCOPED_TRACE(workPercent);
    PC::PacerSettings settings(g_hz60);
    settings.SetAutoSwapInterval(false);
    PC::FramePacer pacer(settings);
    const LoopResult result = RunUnheldLoop(pacer, g_hz60, workPercent, 2'000);
    EXPECT_EQ(result.SwapInterval, 1u);
    // The animation time of the last frame is the time to its start: within a refresh of the frames before it
    const int64_t passed = g_hz60.NearestRefreshes(Span(result.Passed.Nanoseconds() * (result.Frames - 1) / result.Frames));
    EXPECT_LE(std::abs(result.AnimationRefreshes - passed), 1);
    EXPECT_GT(result.WindowFrames, 40u);
    EXPECT_EQ(result.LateFrames, result.WindowFrames) << "every frame of the window";
  }
}

TEST(WorkOverTime, AFrameWithoutEndFrameIsNotJudgedByItsWork)
{
  // Without EndFrame a frame's work is the time to the next frame's start: a refresh, give or take the wake-up. That must not read
  // as work over the frame time
  PC::FramePacer pacer{PC::PacerSettings(g_hz60)};
  FP::NanosecondTickCount start = At(StartNanoseconds);
  for (int64_t frame = 0; frame < 600; ++frame)
  {
    const PC::FrameSchedule schedule = pacer.BeginFrame(start + Span(frame % 2 == 0 ? 0 : 300'000));
    EXPECT_EQ(schedule.SwapInterval, 1u);
    EXPECT_EQ(schedule.Change, PC::SwapIntervalChange::Unchanged);
    start += g_hz60.TimeFor(frame + 1) - g_hz60.TimeFor(frame);
  }
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);

  // And a loop no vsync holds, without EndFrame: nothing says its work is over, so it is measured by its starts alone, as before
  PC::FramePacer unheld{PC::PacerSettings(g_hz60)};
  const LoopResult result = RunUnheldLoop(unheld, g_hz60, 130, 300, false);
  EXPECT_EQ(result.SlowerAtFrame, -1);
  EXPECT_EQ(result.LateFrames, 0u);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// A real swap chain
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(WorkOverTime, ARealLoopWithWorkOverARefreshIsLateAndItsAnimationKeepsTime)
{
  // 1500 frames of a Vulkan FIFO swap chain on a 120 Hz display (fixed refresh, an idle machine), CPU work of 11.1 ms a frame against
  // a refresh of 8.33 ms: the frame starts are 1.37 refreshes apart, and the display showed the frames for one or two refreshes
  const std::optional<std::filesystem::path> path = FindTestData();
  if (!path)
  {
    GTEST_SKIP() << "test-data/pacer/120-vulkan-work-130-log.csv not found (a copy outside the repository)";
  }
  const std::vector<LogFrame> log = ReadLog(*path);
  ASSERT_EQ(log.size(), 1500u);

  // What the display did, where two frames next to each other have a display time
  int64_t shownTwice = 0;
  int64_t shownSteps = 0;
  for (std::size_t index = 1; index < log.size(); ++index)
  {
    if (log[index].DisplayNanoseconds >= 0 && log[index - 1].DisplayNanoseconds >= 0)
    {
      ++shownSteps;
      shownTwice += g_hz120.NearestRefreshes(Span(log[index].DisplayNanoseconds - log[index - 1].DisplayNanoseconds)) == 2 ? 1 : 0;
    }
  }
  EXPECT_EQ(shownSteps, 1'495);
  EXPECT_EQ(shownTwice, 550);

  // At a fixed swap interval of one, as the log was taken: the animation's refreshes are the time that passed
  PC::PacerSettings settings(g_hz120);
  settings.SetAutoSwapInterval(false);
  PC::FramePacer fixed(settings);
  int64_t animationRefreshes = 0;
  for (const LogFrame& frame : log)
  {
    animationRefreshes += g_hz120.NearestRefreshes(fixed.BeginFrame(At(frame.StartNanoseconds)).AnimationStep);
    static_cast<void>(fixed.EndFrame(At(frame.EndFrameNanoseconds), Span(frame.WorkNanoseconds)));
  }
  const int64_t passed = g_hz120.NearestRefreshes(Span(log.back().StartNanoseconds - log.front().StartNanoseconds));
  EXPECT_EQ(passed, 2'052);
  EXPECT_LE(std::abs(animationRefreshes - passed), 1);
  EXPECT_EQ(fixed.FrameWindow().LateFrames, fixed.FrameWindow().Frames) << "every frame's work is over a refresh";

  // With the rule: slower as soon as a tenth of a full frame window's frames (24 of 240) were late, a quarter of a second in
  PC::FramePacer adaptive{PC::PacerSettings(g_hz120)};
  int64_t slowerAtFrame = -1;
  for (std::size_t index = 0; index < log.size() && slowerAtFrame < 0; ++index)
  {
    const PC::FrameSchedule schedule = adaptive.BeginFrame(At(log[index].StartNanoseconds));
    static_cast<void>(adaptive.EndFrame(At(log[index].EndFrameNanoseconds), Span(log[index].WorkNanoseconds)));
    slowerAtFrame = schedule.Change == PC::SwapIntervalChange::Slower ? static_cast<int64_t>(index) : -1;
  }
  EXPECT_EQ(slowerAtFrame, 25);
  EXPECT_EQ(adaptive.SwapInterval(), 2u);
}
