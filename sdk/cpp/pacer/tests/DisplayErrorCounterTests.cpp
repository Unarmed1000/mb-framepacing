// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The animation error from the display times an application reports (sdk/doc/pacer-design.md, the "+" beside a
// tier): which frames are judged, what counts as an error frame and as a frame at another refresh, and the last second.

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/display/DisplayErrorCounter.hpp>
#include <mb/framepacing/pacer/display/DisplayErrorState.hpp>
#include <mb/framepacing/pacer/display/DisplayReport.hpp>
#include <gtest/gtest.h>
#include <cstdint>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  // 100 Hz: a refresh period of exactly 10,000,000 ns, so half a refresh is 5 ms and the error threshold a tenth of one
  constexpr int64_t Period = 10'000'000;
  constexpr int64_t Start = 1'000'000'000;

  const PC::RefreshPeriod g_hz100 = PC::RefreshPeriod::FromRate(100);

  PC::DisplayReport Shown(const uint64_t frameId, const int64_t nanoseconds) noexcept
  {
    PC::DisplayReport report;
    report.FrameId = frameId;
    report.DisplayTime = FP::NanosecondTickCount(nanoseconds);
    return report;
  }

  PC::DisplayReport NotShown(const uint64_t frameId) noexcept
  {
    PC::DisplayReport report;
    report.FrameId = frameId;
    report.Shown = false;
    return report;
  }

  //! Frames first to last begin, each with an animation time step of one refresh, all started before any display time of
  //! these tests
  void Begin(PC::DisplayErrorCounter& rCounter, const uint64_t first, const uint64_t last)
  {
    for (uint64_t frameId = first; frameId <= last; ++frameId)
    {
      rCounter.AddFrame(frameId, FP::NanosecondTimeSpan(Period), 1, FP::NanosecondTickCount(0));
    }
  }
}

TEST(DisplayErrorCounter, FramesShownTheirAnimationTimeStepApartAreJudgedAndHaveNoError)
{
  PC::DisplayErrorCounter counter;
  EXPECT_EQ(counter.State(), PC::DisplayErrorState());
  Begin(counter, 1, 20);
  for (uint64_t frameId = 1; frameId <= 20; ++frameId)
  {
    counter.AddDisplayReport(Shown(frameId, Start + (static_cast<int64_t>(frameId) * Period)), g_hz100);
  }
  const PC::DisplayErrorState state = counter.State();
  EXPECT_EQ(state.Reports, 20u);
  EXPECT_EQ(state.Refused, 0u);
  // The first frame has no frame before it
  EXPECT_EQ(state.JudgedFrames, 19u);
  EXPECT_EQ(state.ErrorFrames, 0u);
  EXPECT_EQ(state.OffTargetFrames, 0u);
  EXPECT_EQ(state.LateFrames, 0u);
  EXPECT_EQ(state.RecentJudgedFrames, 19u);
}

TEST(DisplayErrorCounter, AnErrorOfMoreThanAMillisecondIsAnErrorFrameAndHalfARefreshIsAnotherRefresh)
{
  PC::DisplayErrorCounter counter;
  Begin(counter, 1, 12);
  int64_t shown = Start;
  counter.AddDisplayReport(Shown(1, shown), g_hz100);
  // Exactly the threshold either way is no error frame
  shown += Period + 1'000'000;
  counter.AddDisplayReport(Shown(2, shown), g_hz100);
  shown += Period - 1'000'000;
  counter.AddDisplayReport(Shown(3, shown), g_hz100);
  EXPECT_EQ(counter.State().ErrorFrames, 0u);
  EXPECT_EQ(counter.State().JudgedFrames, 2u);

  // A nanosecond more is one: a display time step that is longer, then one that is shorter
  shown += Period + 1'000'001;
  counter.AddDisplayReport(Shown(4, shown), g_hz100);
  shown += Period - 1'000'001;
  counter.AddDisplayReport(Shown(5, shown), g_hz100);
  EXPECT_EQ(counter.State().ErrorFrames, 2u);
  EXPECT_EQ(counter.State().OffTargetFrames, 0u);

  // Just under half a refresh is still the frame's own refresh
  shown += Period + 4'999'999;
  counter.AddDisplayReport(Shown(6, shown), g_hz100);
  EXPECT_EQ(counter.State().ErrorFrames, 3u);
  EXPECT_EQ(counter.State().OffTargetFrames, 0u);
  // Half a refresh later is another refresh, and late
  shown += Period + 5'000'000;
  counter.AddDisplayReport(Shown(7, shown), g_hz100);
  EXPECT_EQ(counter.State().OffTargetFrames, 1u);
  EXPECT_EQ(counter.State().LateFrames, 1u);
  // A whole refresh late: the frame before it was on screen for two
  shown += 2 * Period;
  counter.AddDisplayReport(Shown(8, shown), g_hz100);
  EXPECT_EQ(counter.State().OffTargetFrames, 2u);
  EXPECT_EQ(counter.State().LateFrames, 2u);
  // Shown in the same refresh as the frame before it: another refresh too, and not late
  counter.AddDisplayReport(Shown(9, shown), g_hz100);
  EXPECT_EQ(counter.State().OffTargetFrames, 3u);
  EXPECT_EQ(counter.State().LateFrames, 2u);
  EXPECT_EQ(counter.State().ErrorFrames, 6u);
  EXPECT_EQ(counter.State().JudgedFrames, 8u);
}

TEST(DisplayErrorCounter, TheAnimationTimeStepIsTheFramesOwn)
{
  PC::DisplayErrorCounter counter;
  counter.AddFrame(1, FP::NanosecondTimeSpan(Period), 1, FP::NanosecondTickCount(0));
  counter.AddFrame(2, FP::NanosecondTimeSpan(2 * Period), 2, FP::NanosecondTickCount(0));
  counter.AddFrame(3, FP::NanosecondTimeSpan(3 * Period), 3, FP::NanosecondTickCount(0));
  counter.AddFrame(4, FP::NanosecondTimeSpan(2 * Period), 2, FP::NanosecondTickCount(0));
  counter.AddDisplayReport(Shown(1, Start), g_hz100);
  counter.AddDisplayReport(Shown(2, Start + (2 * Period)), g_hz100);
  counter.AddDisplayReport(Shown(3, Start + (5 * Period)), g_hz100);
  EXPECT_EQ(counter.State().JudgedFrames, 2u);
  EXPECT_EQ(counter.State().ErrorFrames, 0u);
  // Made for two refreshes after the frame before it, shown after three
  counter.AddDisplayReport(Shown(4, Start + (8 * Period)), g_hz100);
  EXPECT_EQ(counter.State().ErrorFrames, 1u);
  EXPECT_EQ(counter.State().LateFrames, 1u);
}

TEST(DisplayErrorCounter, AStepAcrossAFrameWithoutAReportIsNotJudged)
{
  PC::DisplayErrorCounter counter;
  Begin(counter, 1, 12);
  counter.AddDisplayReport(Shown(1, Start), g_hz100);
  counter.AddDisplayReport(Shown(2, Start + Period), g_hz100);
  EXPECT_EQ(counter.State().JudgedFrames, 1u);
  // No report for frame 3: it may have been shown, so nothing is said of frame 4
  counter.AddDisplayReport(Shown(4, Start + (7 * Period)), g_hz100);
  EXPECT_EQ(counter.State().JudgedFrames, 1u);
  counter.AddDisplayReport(Shown(5, Start + (8 * Period)), g_hz100);
  EXPECT_EQ(counter.State().JudgedFrames, 2u);
  // No report for frame 6, and frame 7 never shown: the same
  counter.AddDisplayReport(NotShown(7), g_hz100);
  counter.AddDisplayReport(Shown(8, Start + (20 * Period)), g_hz100);
  EXPECT_EQ(counter.State().JudgedFrames, 2u);
  counter.AddDisplayReport(Shown(9, Start + (21 * Period)), g_hz100);
  EXPECT_EQ(counter.State().JudgedFrames, 3u);
  EXPECT_EQ(counter.State().ErrorFrames, 0u);
  EXPECT_EQ(counter.State().NotShown, 1u);
}

// On screen a, a, c: frame 3 is never shown and frame 4 replaces it, animated for its display time. It has moved two refreshes and
// stayed away two: 0 by the sum, a stop of one refresh and a step of one more by the parts
TEST(DisplayErrorCounter, AFrameThatReplacesOneNeverShownIsAnErrorFrameAndNotLate)
{
  PC::DisplayErrorCounter counter;
  Begin(counter, 1, 5);
  counter.AddDisplayReport(Shown(1, Start), g_hz100);
  counter.AddDisplayReport(Shown(2, Start + Period), g_hz100);
  counter.AddDisplayReport(NotShown(3), g_hz100);
  counter.AddDisplayReport(Shown(4, Start + (3 * Period)), g_hz100);

  PC::DisplayErrorState state = counter.State();
  EXPECT_EQ(state.NotShown, 1u);
  EXPECT_EQ(state.JudgedFrames, 2u);
  EXPECT_EQ(state.ErrorFrames, 1u);
  EXPECT_EQ(state.OffTargetFrames, 0u);
  EXPECT_EQ(state.LateFrames, 0u);

  // The frame after it is on time again
  counter.AddDisplayReport(Shown(5, Start + (4 * Period)), g_hz100);
  state = counter.State();
  EXPECT_EQ(state.JudgedFrames, 3u);
  EXPECT_EQ(state.ErrorFrames, 1u);
}

// Frames 3 and 4 never shown: frame 5 has moved three refreshes and stayed away three
TEST(DisplayErrorCounter, TheStepAcrossFramesNeverShownAddsUpTheirAnimationTimeSteps)
{
  PC::DisplayErrorCounter counter;
  Begin(counter, 1, 5);
  counter.AddDisplayReport(Shown(1, Start), g_hz100);
  counter.AddDisplayReport(Shown(2, Start + Period), g_hz100);
  counter.AddDisplayReport(NotShown(3), g_hz100);
  counter.AddDisplayReport(NotShown(4), g_hz100);
  counter.AddDisplayReport(Shown(5, Start + (4 * Period)), g_hz100);

  const PC::DisplayErrorState state = counter.State();
  EXPECT_EQ(state.NotShown, 2u);
  EXPECT_EQ(state.ErrorFrames, 1u);
  EXPECT_EQ(state.OffTargetFrames, 0u);
  EXPECT_EQ(state.LateFrames, 0u);
}

// The clock lost the frame: frame 3 never shown and the animation did not move for it, so frame 4 is a refresh behind where it stayed
TEST(DisplayErrorCounter, AFrameNeverShownThatTheAnimationDidNotMoveForLeavesTheNextOneLate)
{
  PC::DisplayErrorCounter counter;
  Begin(counter, 1, 2);
  counter.AddFrame(3, FP::NanosecondTimeSpan(0), 1, FP::NanosecondTickCount(0));
  counter.AddFrame(4, FP::NanosecondTimeSpan(Period), 1, FP::NanosecondTickCount(0));
  counter.AddDisplayReport(Shown(1, Start), g_hz100);
  counter.AddDisplayReport(Shown(2, Start + Period), g_hz100);
  counter.AddDisplayReport(NotShown(3), g_hz100);
  counter.AddDisplayReport(Shown(4, Start + (3 * Period)), g_hz100);

  const PC::DisplayErrorState state = counter.State();
  EXPECT_EQ(state.ErrorFrames, 1u);
  EXPECT_EQ(state.OffTargetFrames, 1u);
  EXPECT_EQ(state.LateFrames, 1u);
}

// At a target of two refreshes: frame 3 never shown, frame 4 has moved four and stayed away four
TEST(DisplayErrorCounter, AFrameThatReplacesOneNeverShownIsOffItsOwnIntendedPeriod)
{
  PC::DisplayErrorCounter counter;
  for (uint64_t frameId = 1; frameId <= 4; ++frameId)
  {
    counter.AddFrame(frameId, FP::NanosecondTimeSpan(2 * Period), 2, FP::NanosecondTickCount(0));
  }
  counter.AddDisplayReport(Shown(1, Start), g_hz100);
  counter.AddDisplayReport(Shown(2, Start + (2 * Period)), g_hz100);
  EXPECT_EQ(counter.State().ErrorFrames, 0u);
  counter.AddDisplayReport(NotShown(3), g_hz100);
  counter.AddDisplayReport(Shown(4, Start + (6 * Period)), g_hz100);

  EXPECT_EQ(counter.State().ErrorFrames, 1u);
  EXPECT_EQ(counter.State().LateFrames, 0u);
}

// On demand there is no target to be off: the same frames have no error by the sum and none is looked for by a part
TEST(DisplayErrorCounter, AFrameWithoutAnIntendedPeriodKeepsTheSum)
{
  PC::DisplayErrorCounter counter;
  for (uint64_t frameId = 1; frameId <= 4; ++frameId)
  {
    counter.AddFrame(frameId, FP::NanosecondTimeSpan(Period), 0, FP::NanosecondTickCount(0));
  }
  counter.AddDisplayReport(Shown(1, Start), g_hz100);
  counter.AddDisplayReport(Shown(2, Start + Period), g_hz100);
  counter.AddDisplayReport(NotShown(3), g_hz100);
  counter.AddDisplayReport(Shown(4, Start + (3 * Period)), g_hz100);

  EXPECT_EQ(counter.State().JudgedFrames, 2u);
  EXPECT_EQ(counter.State().ErrorFrames, 0u);
}

// Shown 2 ms sooner than intended and animated for it: no error by the sum, a rush by the parts. And the display step that is off
// alone: 1.001001 ms longer than intended, with the animation step 0.002 ms longer, so the sum stays within the threshold
TEST(DisplayErrorCounter, EachPartCountsByItself)
{
  constexpr int64_t Millisecond = FP::NanosecondTimeSpan::NanosecondsPerMillisecond;
  const auto errorFrames = [](const int64_t displayStep, const int64_t animationStep)
  {
    PC::DisplayErrorCounter counter;
    Begin(counter, 1, 1);
    counter.AddFrame(2, FP::NanosecondTimeSpan(animationStep), 1, FP::NanosecondTickCount(0));
    counter.AddDisplayReport(Shown(1, Start), g_hz100);
    counter.AddDisplayReport(Shown(2, Start + displayStep), g_hz100);
    return counter.State().ErrorFrames;
  };

  EXPECT_EQ(errorFrames(Period - (2 * Millisecond), Period - (2 * Millisecond)), 1u);
  EXPECT_EQ(errorFrames(Period + Millisecond + 1'001, Period + 2'000), 1u);
  EXPECT_EQ(errorFrames(Period, Period), 0u);
}

// The display step is `early` short of the intended period and the animation step 1 ms shorter than that, so the sum is exactly the
// threshold, which is not beyond it: only the step is past the threshold, by `early`
TEST(DisplayErrorCounter, APartCountsOnlyPastTheRounding)
{
  const auto errorFrames = [](const int64_t early)
  {
    constexpr int64_t Millisecond = FP::NanosecondTimeSpan::NanosecondsPerMillisecond;
    PC::DisplayErrorCounter counter;
    Begin(counter, 1, 1);
    counter.AddFrame(2, FP::NanosecondTimeSpan(Period - early - Millisecond), 1, FP::NanosecondTickCount(0));
    counter.AddDisplayReport(Shown(1, Start), g_hz100);
    counter.AddDisplayReport(Shown(2, Start + Period - early), g_hz100);
    return counter.State().ErrorFrames;
  };

  EXPECT_EQ(errorFrames(1), 0u);
  EXPECT_EQ(errorFrames(1000), 0u);
  EXPECT_EQ(errorFrames(1001), 1u);
}

TEST(DisplayErrorCounter, AReportForAFrameItDoesNotKeepIsRefused)
{
  PC::DisplayErrorCounter counter;
  // Before any frame, and for a frame that was not begun
  counter.AddDisplayReport(Shown(1, Start), g_hz100);
  Begin(counter, 1, 4);
  counter.AddDisplayReport(Shown(5, Start), g_hz100);
  counter.AddDisplayReport(Shown(0, Start), g_hz100);
  EXPECT_EQ(counter.State().Refused, 3u);
  // Not newer than the report before it
  counter.AddDisplayReport(Shown(3, Start), g_hz100);
  counter.AddDisplayReport(Shown(3, Start), g_hz100);
  counter.AddDisplayReport(Shown(2, Start), g_hz100);
  EXPECT_EQ(counter.State().Refused, 5u);
  EXPECT_EQ(counter.State().Reports, 1u);

  // More than the frames kept back
  Begin(counter, 5, 4 + PC::DisplayErrorCounter::Capacity);
  counter.AddDisplayReport(Shown(4, Start + Period), g_hz100);
  EXPECT_EQ(counter.State().Refused, 6u);
  counter.AddDisplayReport(Shown(5, Start + (2 * Period)), g_hz100);
  EXPECT_EQ(counter.State().Reports, 2u);
  EXPECT_EQ(counter.State().JudgedFrames, 0u);
}

TEST(DisplayErrorCounter, StartingAgainJudgesNothingAcrossAndKeepsTheCounts)
{
  PC::DisplayErrorCounter counter;
  Begin(counter, 1, 4);
  counter.AddDisplayReport(Shown(1, Start), g_hz100);
  counter.AddDisplayReport(Shown(2, Start + Period), g_hz100);
  counter.Restart();
  // The frames from before are not kept
  counter.AddDisplayReport(Shown(3, Start + (2 * Period)), g_hz100);
  EXPECT_EQ(counter.State().Refused, 1u);
  Begin(counter, 5, 7);
  counter.AddDisplayReport(Shown(5, Start + (50 * Period)), g_hz100);
  EXPECT_EQ(counter.State().JudgedFrames, 1u);
  counter.AddDisplayReport(Shown(6, Start + (51 * Period)), g_hz100);
  EXPECT_EQ(counter.State().JudgedFrames, 2u);
  EXPECT_EQ(counter.State().ErrorFrames, 0u);

  // Frame ids that do not go on by one start again by themselves: another pacer's frames
  Begin(counter, 100, 102);
  counter.AddDisplayReport(Shown(7, Start + (52 * Period)), g_hz100);
  EXPECT_EQ(counter.State().Refused, 2u);
  counter.AddDisplayReport(Shown(100, Start + (90 * Period)), g_hz100);
  counter.AddDisplayReport(Shown(101, Start + (91 * Period)), g_hz100);
  EXPECT_EQ(counter.State().JudgedFrames, 3u);
  EXPECT_EQ(counter.State().ErrorFrames, 0u);
}

TEST(DisplayErrorCounter, TheLastSecondIsCountedByItself)
{
  PC::DisplayErrorCounter counter;
  // Three seconds at 100 Hz: in the first every tenth frame is shown a refresh late and the one after it on its own refresh
  int64_t shown = Start;
  uint64_t expectedErrors = 0;
  for (uint64_t frameId = 1; frameId <= 300; ++frameId)
  {
    counter.AddFrame(frameId, FP::NanosecondTimeSpan(Period), 1, FP::NanosecondTickCount(0));
    const bool late = frameId <= 100 && (frameId % 10) == 0;
    const bool afterLate = frameId <= 101 && frameId > 1 && ((frameId - 1) % 10) == 0;
    shown += late ? 2 * Period : (afterLate ? 0 : Period);
    expectedErrors += (late || afterLate) ? 1u : 0u;
    counter.AddDisplayReport(Shown(frameId, shown), g_hz100);
    if (frameId == 100)
    {
      // The newest eighths of a second: between seven eighths of a second and a whole one, of a second that has ten
      // frames in every eleven refreshes
      EXPECT_GE(counter.State().RecentJudgedFrames, 75u);
      EXPECT_LE(counter.State().RecentJudgedFrames, 95u);
      EXPECT_GE(counter.State().RecentLateFrames, 7u);
      EXPECT_EQ(counter.State().RecentOffTargetFrames, counter.State().RecentErrorFrames);
    }
  }
  const PC::DisplayErrorState state = counter.State();
  EXPECT_EQ(state.JudgedFrames, 299u);
  EXPECT_EQ(state.ErrorFrames, expectedErrors);
  EXPECT_EQ(state.ErrorFrames, 20u);
  EXPECT_EQ(state.LateFrames, 10u);
  // Nothing of the first second is in the last one
  EXPECT_GE(state.RecentJudgedFrames, 87u);
  EXPECT_LE(state.RecentJudgedFrames, 100u);
  EXPECT_EQ(state.RecentErrorFrames, 0u);
  EXPECT_EQ(state.RecentOffTargetFrames, 0u);
  EXPECT_EQ(state.RecentLateFrames, 0u);
}

TEST(DisplayErrorCounter, TheTimeFromAFramesStartToItsDisplayIsAddedUpForEveryFrameShown)
{
  PC::DisplayErrorCounter counter;
  // Five frames a refresh apart, each shown two refreshes after its start and a little more from frame to frame
  int64_t total = 0;
  for (uint64_t frameId = 1; frameId <= 5; ++frameId)
  {
    const int64_t start = Start + (static_cast<int64_t>(frameId) * Period);
    counter.AddFrame(frameId, FP::NanosecondTimeSpan(Period), 1, FP::NanosecondTickCount(start));
  }
  for (uint64_t frameId = 1; frameId <= 5; ++frameId)
  {
    const int64_t start = Start + (static_cast<int64_t>(frameId) * Period);
    const int64_t took = (2 * Period) + (static_cast<int64_t>(frameId) * 100'000);
    total += took;
    counter.AddDisplayReport(Shown(frameId, start + took), g_hz100);
  }
  PC::DisplayErrorState state = counter.State();
  // The first frame is one of them, though it has no frame before it to be judged against
  EXPECT_EQ(state.JudgedFrames, 4u);
  EXPECT_EQ(state.StartToDisplayFrames, 5u);
  EXPECT_EQ(state.StartToDisplayTotal.Nanoseconds(), total);
  EXPECT_EQ(state.StartToDisplayLongest.Nanoseconds(), (2 * Period) + 500'000);
  EXPECT_EQ(state.RecentStartToDisplayFrames, 5u);
  EXPECT_EQ(state.RecentStartToDisplayTotal, state.StartToDisplayTotal);
  EXPECT_EQ(state.RecentStartToDisplayLongest, state.StartToDisplayLongest);

  // A frame that was never shown took no time, and neither did one whose display time is before its start: that is no
  // time a frame took, though the report is taken
  for (uint64_t frameId = 6; frameId <= 8; ++frameId)
  {
    counter.AddFrame(frameId, FP::NanosecondTimeSpan(Period), 1, FP::NanosecondTickCount(Start + (static_cast<int64_t>(frameId) * Period)));
  }
  counter.AddDisplayReport(NotShown(6), g_hz100);
  counter.AddDisplayReport(Shown(7, Start + (7 * Period) - 1), g_hz100);
  state = counter.State();
  EXPECT_EQ(state.Reports, 7u);
  EXPECT_EQ(state.StartToDisplayFrames, 5u);
  EXPECT_EQ(state.StartToDisplayTotal.Nanoseconds(), total);
  // Shown at the moment it started: a time of nothing, and counted
  counter.AddDisplayReport(Shown(8, Start + (8 * Period)), g_hz100);
  state = counter.State();
  EXPECT_EQ(state.StartToDisplayFrames, 6u);
  EXPECT_EQ(state.StartToDisplayTotal.Nanoseconds(), total);
  EXPECT_EQ(state.StartToDisplayLongest.Nanoseconds(), (2 * Period) + 500'000);
}

TEST(DisplayErrorCounter, TheLastSecondsTimeFromStartToDisplayIsItsOwn)
{
  PC::DisplayErrorCounter counter;
  // Three seconds at 100 Hz: in the first a frame is shown three refreshes after its start, after that one refresh
  for (uint64_t frameId = 1; frameId <= 300; ++frameId)
  {
    const int64_t start = Start + (static_cast<int64_t>(frameId) * Period);
    counter.AddFrame(frameId, FP::NanosecondTimeSpan(Period), 1, FP::NanosecondTickCount(start));
    counter.AddDisplayReport(Shown(frameId, start + (frameId <= 100 ? 3 * Period : Period)), g_hz100);
  }
  const PC::DisplayErrorState state = counter.State();
  EXPECT_EQ(state.StartToDisplayFrames, 300u);
  EXPECT_EQ(state.StartToDisplayTotal.Nanoseconds(), (100 * 3 * Period) + (200 * Period));
  EXPECT_EQ(state.StartToDisplayLongest.Nanoseconds(), 3 * Period);
  // Nothing of the first second is in the last one: every frame of it took one refresh
  EXPECT_GE(state.RecentStartToDisplayFrames, 87u);
  EXPECT_LE(state.RecentStartToDisplayFrames, 100u);
  EXPECT_EQ(state.RecentStartToDisplayTotal.Nanoseconds(), int64_t{state.RecentStartToDisplayFrames} * Period);
  EXPECT_EQ(state.RecentStartToDisplayLongest.Nanoseconds(), Period);
}

TEST(DisplayErrorCounter, AFrameIdOfZeroStartsAgainAsAnyIdThatIsNotTheNextOne)
{
  PC::DisplayErrorCounter counter;
  Begin(counter, 1, 3);
  counter.AddDisplayReport(Shown(1, Start), g_hz100);
  // No frame has the id 0: given all the same, it starts again and keeps nothing from before
  counter.AddFrame(0, FP::NanosecondTimeSpan(Period), 1, FP::NanosecondTickCount(0));
  counter.AddDisplayReport(Shown(2, Start + Period), g_hz100);
  EXPECT_EQ(counter.State().Refused, 1u);
  Begin(counter, 1, 2);
  counter.AddDisplayReport(Shown(1, Start + (10 * Period)), g_hz100);
  counter.AddDisplayReport(Shown(2, Start + (11 * Period)), g_hz100);
  EXPECT_EQ(counter.State().JudgedFrames, 1u);
  EXPECT_EQ(counter.State().ErrorFrames, 0u);
}

TEST(DisplayErrorCounter, AFrameOnScreenForItsIntendedPeriodIsNotLateHoweverShortItsAnimationTimeStep)
{
  PC::DisplayErrorCounter counter;
  // A timer that stepped 4 ms for a frame intended for one refresh, shown one refresh after the frame before: 6 ms off, at
  // another refresh than it was made for, and not late
  counter.AddFrame(1, FP::NanosecondTimeSpan(Period), 1, FP::NanosecondTickCount(0));
  counter.AddFrame(2, FP::NanosecondTimeSpan(4'000'000), 1, FP::NanosecondTickCount(0));
  counter.AddFrame(3, FP::NanosecondTimeSpan(4'000'000), 1, FP::NanosecondTickCount(0));
  counter.AddFrame(4, FP::NanosecondTimeSpan(4'000'000), 1, FP::NanosecondTickCount(0));
  counter.AddDisplayReport(Shown(1, Start), g_hz100);
  counter.AddDisplayReport(Shown(2, Start + Period), g_hz100);
  EXPECT_EQ(counter.State().OffTargetFrames, 1u);
  EXPECT_EQ(counter.State().LateFrames, 0u);
  // Just under half a refresh past its intended period is still not late; half a refresh past it is
  counter.AddDisplayReport(Shown(3, Start + (2 * Period) + 4'999'999), g_hz100);
  EXPECT_EQ(counter.State().LateFrames, 0u);
  counter.AddDisplayReport(Shown(4, Start + (3 * Period) + 4'999'999 + 5'000'000), g_hz100);
  EXPECT_EQ(counter.State().OffTargetFrames, 3u);
  EXPECT_EQ(counter.State().LateFrames, 1u);
}

TEST(DisplayErrorCounter, AFrameIntendedForTwoRefreshesIsLateOnlyPastThem)
{
  PC::DisplayErrorCounter counter;
  // 30 fps on a display of 60, here 50 on 100: every frame made and intended for two refreshes
  for (uint64_t frameId = 1; frameId <= 4; ++frameId)
  {
    counter.AddFrame(frameId, FP::NanosecondTimeSpan(2 * Period), 2, FP::NanosecondTickCount(0));
  }
  counter.AddDisplayReport(Shown(1, Start), g_hz100);
  counter.AddDisplayReport(Shown(2, Start + (2 * Period)), g_hz100);
  counter.AddDisplayReport(Shown(3, Start + (4 * Period)), g_hz100);
  EXPECT_EQ(counter.State().JudgedFrames, 2u);
  EXPECT_EQ(counter.State().ErrorFrames, 0u);
  // Shown after three: a refresh late
  counter.AddDisplayReport(Shown(4, Start + (7 * Period)), g_hz100);
  EXPECT_EQ(counter.State().LateFrames, 1u);
}

TEST(DisplayErrorCounter, AFrameWithoutAnIntendedPeriodIsNeverLate)
{
  PC::DisplayErrorCounter counter;
  // On demand: made for one refresh, shown after three. At another refresh, and not late
  counter.AddFrame(1, FP::NanosecondTimeSpan(Period), 0, FP::NanosecondTickCount(0));
  counter.AddFrame(2, FP::NanosecondTimeSpan(Period), 0, FP::NanosecondTickCount(0));
  counter.AddDisplayReport(Shown(1, Start), g_hz100);
  counter.AddDisplayReport(Shown(2, Start + (3 * Period)), g_hz100);
  EXPECT_EQ(counter.State().OffTargetFrames, 1u);
  EXPECT_EQ(counter.State().LateFrames, 0u);
}
