// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Frames that begin before the time the pacer gave for them (FrameSchedule::NextFrameStartTime): the pacer can not wait for an
// application, so it counts where the application did not (FrameWindowState::EarlyStarts). A loop that waits for the time, one that
// nothing holds (the first integration's fault: with work close to a refresh every frame began 0.10 ms early, and frames were
// dropped), one that a present waiting for the display holds, and the count's life in the frame window.
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalRule.hpp>
#include <gtest/gtest.h>
#include <cstdint>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  constexpr int64_t Second = FP::TimeSpan::TicksPerSecond;
  constexpr int64_t StartTicks = 100 * Second;

  const PC::RefreshPeriod g_hz60 = PC::RefreshPeriod::FromRate(60);
  const PC::RefreshPeriod g_hz240 = PC::RefreshPeriod::FromRate(240);

  FP::TimeSpan Span(const int64_t ticks) noexcept
  {
    return FP::TimeSpan(ticks);
  }

  FP::TickCount64 At(const int64_t ticks) noexcept
  {
    return FP::TickCount64(ticks);
  }

  //! A frame with work of a tenth of a refresh, begun at start: its schedule
  PC::FrameSchedule Frame(PC::FramePacer& rPacer, const FP::TickCount64 start)
  {
    const PC::FrameSchedule schedule = rPacer.BeginFrame(start);
    const FP::TimeSpan work(rPacer.Refresh().ToTimeSpan().Ticks() / 10);
    static_cast<void>(rPacer.EndFrame(start + work, work));
    return schedule;
  }
}

TEST(EarlyStarts, ALoopThatWaitsForTheNextFrameStartTimeHasNone)
{
  PC::PacerSettings settings(g_hz240);
  settings.SetPreferredFrameRate(60);
  PC::FramePacer pacer(settings);
  FP::TickCount64 start = At(StartTicks);
  for (int64_t frame = 0; frame < 400; ++frame)
  {
    const PC::FrameSchedule schedule = Frame(pacer, start);
    EXPECT_EQ(schedule.SwapInterval, 4u);
    // A sleep wakes on the time or a little after it, never before
    start = schedule.NextFrameStartTime + Span(frame % 3 == 0 ? 150 : 0);
  }
  const PC::FrameWindowState window = pacer.FrameWindow();
  EXPECT_GT(window.Frames, 100u);
  EXPECT_EQ(window.EarlyStarts, 0u);
  EXPECT_EQ(window.LateFrames, 0u);
}

TEST(EarlyStarts, ALoopThatNothingHoldsBeginsEveryFrameEarly)
{
  // The first integration's loop at a swap interval of one, with GPU work of 94 % of a refresh on a swap chain whose present does not
  // wait: the next frame begins when the work is done, 0.10 ms before a refresh has passed. Rounded, that is one refresh: no frame
  // is late and the rule stays, but the loop makes more frames than the display shows
  PC::FramePacer pacer{PC::PacerSettings(g_hz240)};
  const int64_t period = g_hz240.ToTimeSpan().Ticks();
  const FP::TimeSpan work(period * 94 / 100);
  FP::TickCount64 start = At(StartTicks);
  for (int64_t frame = 0; frame < 300; ++frame)
  {
    const PC::FrameSchedule schedule = pacer.BeginFrame(start);
    EXPECT_EQ(schedule.SwapInterval, 1u) << frame;
    EXPECT_EQ(schedule.Change, PC::SwapIntervalChange::Unchanged) << frame;
    static_cast<void>(pacer.EndFrame(start + work, work));
    start = start + Span(period - 1'000);
  }
  const PC::FrameWindowState window = pacer.FrameWindow();
  EXPECT_EQ(window.Frames, 299u);
  EXPECT_EQ(window.EarlyStarts, window.Frames) << "every frame's next one began early";
  EXPECT_EQ(window.LateFrames, 0u) << "which the pacer's own count of late frames does not show";
}

TEST(EarlyStarts, ALoopHeldByAPresentThatWaitsBeginsAboutHalfItsFramesEarly)
{
  // The frames start on the display's refreshes, a little after them: by more in every second frame. Each frame's time for the next
  // one is its own start plus a refresh, so the frame after a later start begins early, by nothing that matters
  PC::FramePacer pacer{PC::PacerSettings(g_hz60)};
  const int64_t frames = 100;
  for (int64_t frame = 0; frame < frames; ++frame)
  {
    static_cast<void>(Frame(pacer, At(StartTicks + g_hz60.TimeFor(frame).Ticks() + (frame % 2 == 0 ? 2'000 : 500))));
  }
  const PC::FrameWindowState window = pacer.FrameWindow();
  EXPECT_EQ(window.Frames, static_cast<uint32_t>(frames - 1));
  EXPECT_EQ(window.EarlyStarts, static_cast<uint32_t>(frames / 2));
  EXPECT_EQ(window.LateFrames, 0u);
}

TEST(EarlyStarts, TheCountIsOfTheFramesInTheFrameWindow)
{
  PC::FramePacer pacer{PC::PacerSettings(g_hz60)};
  const int64_t period = g_hz60.ToTimeSpan().Ticks();
  FP::TickCount64 start = At(StartTicks);
  // A second of frames that begin early, then frames held to their time: the early ones leave the frame window with their frames
  for (int64_t frame = 0; frame < 60; ++frame)
  {
    static_cast<void>(Frame(pacer, start));
    start = start + Span(period - 3'000);
  }
  EXPECT_EQ(pacer.FrameWindow().EarlyStarts, 59u);
  uint32_t most = 0;
  for (int64_t frame = 0; frame < 200; ++frame)
  {
    start = Frame(pacer, start).NextFrameStartTime;
    most = pacer.FrameWindow().EarlyStarts > most ? pacer.FrameWindow().EarlyStarts : most;
  }
  EXPECT_EQ(most, 60u) << "the last early start is the first held frame's";
  EXPECT_EQ(pacer.FrameWindow().EarlyStarts, 0u);
  EXPECT_GT(pacer.FrameWindow().Frames, 100u);

  // A pause and Reset start the frame window again, and nothing is counted across them
  for (int64_t frame = 0; frame < 10; ++frame)
  {
    static_cast<void>(Frame(pacer, start));
    start = start + Span(period - 3'000);
  }
  EXPECT_EQ(pacer.FrameWindow().EarlyStarts, 9u) << "the first of the ten began on its time";
  start = start + Span(10 * Second);
  static_cast<void>(Frame(pacer, start));
  EXPECT_EQ(pacer.FrameWindow().EarlyStarts, 0u);
  static_cast<void>(Frame(pacer, start + Span(period - 3'000)));
  EXPECT_EQ(pacer.FrameWindow().EarlyStarts, 1u);
  pacer.Reset();
  EXPECT_EQ(pacer.FrameWindow().EarlyStarts, 0u);
  static_cast<void>(Frame(pacer, start + Span(period)));
  EXPECT_EQ(pacer.FrameWindow().EarlyStarts, 0u) << "the first frame after Reset is measured against nothing";
}

TEST(EarlyStarts, TheRuleCountsThemAndDecidesWithoutThem)
{
  const PC::PacerSettings settings(g_hz60);
  PC::SwapIntervalRule counted(settings);
  PC::SwapIntervalRule plain(settings);
  const FP::TimeSpan work(4 * FP::TimeSpan::TicksPerMillisecond);
  int64_t changes = 0;
  for (int64_t frame = 0; frame < 400; ++frame)
  {
    const FP::TimeSpan displayTime = g_hz60.TimeFor(frame);
    const bool late = frame % 4 == 0 && frame < 200;
    const PC::SwapIntervalChange change = counted.AddFrame(displayTime, work, late, frame % 3 == 0);
    EXPECT_EQ(change, plain.AddFrame(displayTime, work, late)) << frame;
    EXPECT_EQ(counted.SwapInterval(), plain.SwapInterval()) << frame;
    EXPECT_EQ(counted.FrameWindow().LateFrames, plain.FrameWindow().LateFrames) << frame;
    EXPECT_EQ(plain.FrameWindow().EarlyStarts, 0u) << frame;
    changes += change != PC::SwapIntervalChange::Unchanged ? 1 : 0;
  }
  EXPECT_GT(changes, 0) << "the comparison covered a change of swap interval, which empties the count";
  EXPECT_GT(counted.FrameWindow().EarlyStarts, 0u);
  EXPECT_LT(counted.FrameWindow().EarlyStarts, counted.FrameWindow().Frames);
}
