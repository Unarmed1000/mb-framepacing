// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// How far ahead of the pacer's times an application's frames begin (FrameWindowState::StartsAhead): the pacer can not wait for an
// application, so it shows where the application did not. A loop that waits for the time, one that nothing holds (the first
// integration's fault: with work close to a refresh every frame began 0.10 ms early, and frames were dropped), one whose frame
// starts jitter around the refreshes, one with late frames, and the sum's life in the frame window.
//
// A count of the frames that began early was tried first and did not do: the frame starts of a loop in step with the display fall
// a few microseconds before or after the pacer's times, so about half of them count as early in a healthy loop and in a faulty one
// alike (the first integration's 507 stored runs). A sum cancels that jitter and keeps what adds up.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalRule.hpp>
#include <gtest/gtest.h>
#include <cstdint>
#include <cstdlib>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  constexpr int64_t Second = FP::NanosecondTimeSpan::NanosecondsPerSecond;
  constexpr int64_t StartNanoseconds = 100 * Second;

  const PC::RefreshPeriod g_hz60 = PC::RefreshPeriod::FromRate(60);
  const PC::RefreshPeriod g_hz240 = PC::RefreshPeriod::FromRate(240);

  FP::NanosecondTimeSpan Span(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTimeSpan(nanoseconds);
  }

  FP::NanosecondTickCount At(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTickCount(nanoseconds);
  }

  //! A frame with work of a tenth of a refresh, begun at start: its schedule
  PC::FrameSchedule Frame(PC::FramePacer& rPacer, const FP::NanosecondTickCount start)
  {
    const PC::FrameSchedule schedule = rPacer.BeginFrame(start);
    const FP::NanosecondTimeSpan work(rPacer.Refresh().ToNanosecondTimeSpan().Nanoseconds() / 10);
    static_cast<void>(rPacer.EndFrame(start + work, work));
    return schedule;
  }
}

TEST(StartsAhead, ALoopThatWaitsForTheNextFrameStartTimeIsInStepOrALittleBehind)
{
  PC::PacerSettings settings(g_hz240);
  settings.SetPreferredFrameRate(60);
  PC::FramePacer exact(settings);
  PC::FramePacer waking(settings);
  FP::NanosecondTickCount exactStart = At(StartNanoseconds);
  FP::NanosecondTickCount wakingStart = At(StartNanoseconds);
  for (int64_t frame = 0; frame < 400; ++frame)
  {
    const PC::FrameSchedule schedule = Frame(exact, exactStart);
    EXPECT_EQ(schedule.SwapInterval, 4u);
    exactStart = schedule.NextFrameStartTime;
    // A wait that wakes 15 microseconds after the time, every frame
    wakingStart = Frame(waking, wakingStart).NextFrameStartTime + Span(15'000);
  }
  EXPECT_GT(exact.FrameWindow().Frames, 100u);
  EXPECT_EQ(exact.FrameWindow().StartsAhead, Span(0));
  EXPECT_EQ(exact.FrameWindow().LateFrames, 0u);
  // The delays add up: the loop is slower than the display by that much, which the sum shows as behind
  const PC::FrameWindowState window = waking.FrameWindow();
  EXPECT_EQ(window.StartsAhead, Span(-15'000 * int64_t{window.Frames}));
  EXPECT_EQ(window.LateFrames, 0u);
}

TEST(StartsAhead, ALoopThatNothingHoldsRunsAhead)
{
  // The first integration's loop at a swap interval of one, with GPU work of 94 % of a refresh on a swap chain whose present does not
  // wait: the next frame begins when the work is done, 0.10 ms before a refresh has passed. Rounded, that is one refresh: no frame
  // is late and the rule stays, but the loop makes more frames than the display shows
  PC::FramePacer pacer{PC::PacerSettings(g_hz240)};
  const int64_t period = g_hz240.ToNanosecondTimeSpan().Nanoseconds();
  const FP::NanosecondTimeSpan work(period * 94 / 100);
  FP::NanosecondTickCount start = At(StartNanoseconds);
  for (int64_t frame = 0; frame < 300; ++frame)
  {
    const PC::FrameSchedule schedule = pacer.BeginFrame(start);
    EXPECT_EQ(schedule.SwapInterval, 1u) << frame;
    EXPECT_EQ(schedule.Change, PC::SwapIntervalChange::Unchanged) << frame;
    static_cast<void>(pacer.EndFrame(start + work, work));
    start = start + Span(period - 100'000);
  }
  const PC::FrameWindowState window = pacer.FrameWindow();
  EXPECT_EQ(window.Frames, 299u);
  EXPECT_EQ(window.LateFrames, 0u) << "the pacer's own count of late frames does not show it";
  // 0.10 ms a frame, and a nanosecond now and then from the refresh period's fraction: seven refreshes over the 299 frames
  EXPECT_LE(std::abs(window.StartsAhead.Nanoseconds() - (100'000 * int64_t{window.Frames})), int64_t{window.Frames});
  EXPECT_GT(window.StartsAhead, g_hz240.TimeFor(7));
}

TEST(StartsAhead, FrameStartsThatJitterAroundTheRefreshesCancel)
{
  // A loop held by a present that waits for the display: the frames start on the refreshes, a little after them, by more in every
  // second frame. Half of them begin before the time the frame before gave, by nothing that adds up
  PC::FramePacer pacer{PC::PacerSettings(g_hz60)};
  for (int64_t frame = 0; frame < 100; ++frame)
  {
    static_cast<void>(Frame(pacer, At(StartNanoseconds + g_hz60.TimeFor(frame).Nanoseconds() + (frame % 2 == 0 ? 200'000 : 50'000))));
  }
  const PC::FrameWindowState window = pacer.FrameWindow();
  EXPECT_EQ(window.Frames, 99u);
  EXPECT_EQ(window.LateFrames, 0u);
  EXPECT_LE(std::abs(window.StartsAhead.Nanoseconds()), 150'002) << "the first and the last frame's jitter, and a nanosecond of rounding";
}

TEST(StartsAhead, LateFramesAreLeftOut)
{
  // Every fifth frame takes two refreshes: it is late, and the start after it says how late, not that the loop runs ahead or behind
  PC::PacerSettings settings(g_hz60);
  settings.SetAutoSwapInterval(false);
  PC::FramePacer pacer(settings);
  FP::NanosecondTickCount start = At(StartNanoseconds);
  for (int64_t frame = 0; frame < 100; ++frame)
  {
    const PC::FrameSchedule schedule = Frame(pacer, start);
    start = schedule.NextFrameStartTime + (frame % 5 == 4 ? g_hz60.TimeFor(1) : Span(0));
  }
  const PC::FrameWindowState window = pacer.FrameWindow();
  EXPECT_EQ(window.LateFrames, 19u);
  EXPECT_EQ(window.StartsAhead, Span(0));
}

TEST(StartsAhead, TheSumIsOfTheFramesInTheFrameWindow)
{
  PC::FramePacer pacer{PC::PacerSettings(g_hz60)};
  const int64_t period = g_hz60.ToNanosecondTimeSpan().Nanoseconds();
  FP::NanosecondTickCount start = At(StartNanoseconds);
  // A second of frames that begin 0.3 ms early, then frames held to their time: the early ones leave the frame window with their
  // frames
  for (int64_t frame = 0; frame < 60; ++frame)
  {
    static_cast<void>(Frame(pacer, start));
    start = start + Span(period - 300'000);
  }
  EXPECT_LE(std::abs(pacer.FrameWindow().StartsAhead.Nanoseconds() - (59 * 300'000)), 59);
  for (int64_t frame = 0; frame < 200; ++frame)
  {
    start = Frame(pacer, start).NextFrameStartTime;
  }
  EXPECT_EQ(pacer.FrameWindow().StartsAhead, Span(0));
  EXPECT_GT(pacer.FrameWindow().Frames, 100u);

  // A pause and Reset start the frame window again, and nothing is added up across them
  for (int64_t frame = 0; frame < 10; ++frame)
  {
    static_cast<void>(Frame(pacer, start));
    start = start + Span(period - 300'000);
  }
  EXPECT_GT(pacer.FrameWindow().StartsAhead, Span(9 * 299'000));
  start = start + Span(10 * Second);
  static_cast<void>(Frame(pacer, start));
  EXPECT_EQ(pacer.FrameWindow().StartsAhead, Span(0));
  static_cast<void>(Frame(pacer, start + Span(period - 300'000)));
  EXPECT_GT(pacer.FrameWindow().StartsAhead, Span(299'000));
  pacer.Reset();
  EXPECT_EQ(pacer.FrameWindow().StartsAhead, Span(0));
  static_cast<void>(Frame(pacer, start + Span(period)));
  EXPECT_EQ(pacer.FrameWindow().StartsAhead, Span(0)) << "the first frame after Reset is measured against nothing";
}

TEST(StartsAhead, TheRuleAddsItUpAndDecidesWithoutIt)
{
  const PC::PacerSettings settings(g_hz60);
  PC::SwapIntervalRule given(settings);
  PC::SwapIntervalRule plain(settings);
  const FP::NanosecondTimeSpan work(4 * FP::NanosecondTimeSpan::NanosecondsPerMillisecond);
  int64_t changes = 0;
  for (int64_t frame = 0; frame < 400; ++frame)
  {
    const FP::NanosecondTimeSpan displayTime = g_hz60.TimeFor(frame);
    const bool late = frame % 4 == 0 && frame < 200;
    const PC::SwapIntervalChange change = given.AddFrame(displayTime, work, late, Span(70'000));
    EXPECT_EQ(change, plain.AddFrame(displayTime, work, late)) << frame;
    EXPECT_EQ(given.SwapInterval(), plain.SwapInterval()) << frame;
    EXPECT_EQ(given.FrameWindow().LateFrames, plain.FrameWindow().LateFrames) << frame;
    EXPECT_EQ(plain.FrameWindow().StartsAhead, Span(0)) << frame;
    // The late frames are in the frame window and not in the sum
    const PC::FrameWindowState window = given.FrameWindow();
    EXPECT_EQ(window.StartsAhead, Span(70'000 * int64_t{window.Frames - window.LateFrames})) << frame;
    changes += change != PC::SwapIntervalChange::Unchanged ? 1 : 0;
  }
  EXPECT_GT(changes, 0) << "the comparison covered a change of swap interval, which empties the sum";
  EXPECT_GT(given.FrameWindow().StartsAhead, Span(0));
}
