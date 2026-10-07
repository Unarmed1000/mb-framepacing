// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer of vertical blank times with a wait for a present (VBlankWaitForPresentPacer): VBlankPeriodOnlyPacer's frames, each for
// one vertical blank, and a wait that keeps the frames that wait to what may wait, says which vertical blank a frame was shown at,
// and teaches the pacer where a frame has to be ready.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan32.hpp>
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/tier/VBlankWaitForPresentPacer.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  // 100 Hz: a refresh period of exactly 10,000,000 ns, a frame margin of 1,000,000 (1 ms), and a frame is to be ready 5,000,000 into
  // a refresh. Vertical blank n is at Blank(n)
  constexpr int64_t Period = 10'000'000;
  constexpr int64_t Margin = 1'000'000;
  constexpr int64_t Place = 5'000'000;
  constexpr int64_t Start = 1'000'000'000;
  constexpr int64_t Work = 3'000'000;

  const PC::RefreshPeriod g_hz100 = PC::RefreshPeriod::FromRate(100);

  constexpr FP::NanosecondTickCount At(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTickCount(nanoseconds);
  }

  constexpr FP::NanosecondTimeSpan Span(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTimeSpan(nanoseconds);
  }

  constexpr int64_t Blank(const int64_t number) noexcept
  {
    return Start + (number * Period);
  }

  PC::PacerSettings Settings(const PC::PacerAim aim, const uint32_t waitingPresents = 1)
  {
    PC::PacerSettings settings(g_hz100);
    settings.SetAim(aim);
    settings.SetWaitingPresents(waitingPresents);
    return settings;
  }

  //! A vertical blank of the display, read when it was
  void AddBlank(PC::VBlankWaitForPresentPacer& rPacer, const int64_t blankNanoseconds)
  {
    PC::VBlankReading reading;
    reading.VBlankTime = At(blankNanoseconds);
    reading.ReadTime = At(blankNanoseconds);
    rPacer.AddVBlank(reading);
  }

  //! What a frame's calls gave
  struct FrameResult
  {
    uint64_t AskedForId{0};
    int64_t TimeoutNanoseconds{0};
    int64_t StartNanoseconds{0};
    int64_t PresentNanoseconds{0};
    PC::FrameSchedule Schedule;
  };

  //! How the wait a frame's plan asks for ends: when (0: 50 us after it began), and with the present shown or not
  struct WaitEnd
  {
    int64_t EndNanoseconds{0};
    bool Shown{true};
  };

  //! A frame as an application makes it: planned at now, the wait for a present carried out and reported, planned again, begun
  //! at the time it is given (or at once), with CPU work of workNanoseconds, presented at the time it is given (or when the work
  //! is done), and the present reported
  FrameResult Frame(PC::VBlankWaitForPresentPacer& rPacer, const int64_t nowNanoseconds, const WaitEnd wait = {},
                    const int64_t workNanoseconds = Work, const bool accepted = true)
  {
    FrameResult result;
    int64_t now = nowNanoseconds;
    PC::FrameStartPlan plan = rPacer.PlanFrame(At(now));
    if (plan.WaitsForPresent())
    {
      result.AskedForId = plan.WaitForPresentFrameId;
      result.TimeoutNanoseconds = plan.WaitForPresentTimeout.Nanoseconds();
      PC::PresentWaitReport report;
      report.FrameId = plan.WaitForPresentFrameId;
      report.BeginTime = At(now);
      now = wait.EndNanoseconds != 0 ? wait.EndNanoseconds : now + 50'000;
      report.EndTime = At(now);
      report.Shown = wait.Shown;
      rPacer.AddPresentWait(report);
      plan = rPacer.PlanFrame(At(now));
      EXPECT_FALSE(plan.WaitsForPresent());
    }
    result.StartNanoseconds = plan.WaitsForStartTime() ? plan.StartTime.Nanoseconds() : now;
    result.Schedule = rPacer.BeginFrame(At(result.StartNanoseconds));
    const PC::PresentPlan present = rPacer.EndFrame(At(result.StartNanoseconds + workNanoseconds));
    result.PresentNanoseconds = present.WaitsForPresentTime() ? present.PresentTime.Nanoseconds() : result.StartNanoseconds + workNanoseconds;
    PC::PresentReport report;
    report.FrameId = present.FrameId;
    report.CallTime = At(result.PresentNanoseconds);
    report.ReturnTime = At(result.PresentNanoseconds + 60'000);
    report.Accepted = accepted;
    rPacer.AddPresent(report);
    return result;
  }

  //! The number of the vertical blank a frame is for
  int64_t BlankOf(const FrameResult& frame)
  {
    return (frame.Schedule.IntendedDisplayTime.Nanoseconds() - Start) / Period;
  }

  static_assert(PC::VBlankWaitForPresentPacer::Tier == PC::PacerTier::VBlankWaitForPresent);
}

TEST(VBlankWaitForPresentPacer, BeforeAFrameItAsksForAPresentAndThenForTheTimeTheFrameStarts)
{
  PC::VBlankWaitForPresentPacer pacer(Settings(PC::PacerAim::LowLatency));
  EXPECT_FALSE(pacer.HasVBlankReading());
  AddBlank(pacer, Blank(0));
  EXPECT_TRUE(pacer.HasVBlankReading());
  // Nothing was presented yet, and there is no frame to wait a time for
  EXPECT_FALSE(pacer.PlanFrame(At(Blank(0) + 1'000'000)).WaitsForPresent());
  EXPECT_FALSE(pacer.PlanFrame(At(Blank(0) + 1'000'000)).WaitsForStartTime());

  FrameResult frame = Frame(pacer, Blank(0) + 1'000'000);
  EXPECT_EQ(frame.AskedForId, 0u);
  EXPECT_EQ(frame.Schedule.FrameId, 1u);
  EXPECT_EQ(frame.Schedule.SwapInterval, 1u);
  EXPECT_EQ(frame.Schedule.AnimationTime, Span(0));
  // Ready 3 ms into the refresh, a frame margin before its end: the frame is for the first vertical blank
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(1)));
  EXPECT_EQ(frame.Schedule.TargetFrameTime, FP::NanosecondTimeSpan32(Period));
  EXPECT_EQ(frame.Schedule.PreferredFrameTime, FP::NanosecondTimeSpan32(Period));
  EXPECT_EQ(frame.Schedule.Change, PC::SwapIntervalChange::Unchanged);
  EXPECT_EQ(pacer.Refresh(), g_hz100);
  EXPECT_EQ(pacer.SwapInterval(), 1u);

  // The present of frame 1, for at most the settings' four swap intervals; planned again it is the same
  const PC::FrameStartPlan plan = pacer.PlanFrame(At(frame.PresentNanoseconds + 100'000));
  EXPECT_EQ(plan.WaitForPresentFrameId, 1u);
  EXPECT_EQ(plan.WaitForPresentTimeout, FP::NanosecondTimeDuration::FromNanoseconds(4 * Period));
  EXPECT_EQ(pacer.PlanFrame(At(frame.PresentNanoseconds + 200'000)).WaitForPresentFrameId, 1u);

  // The wait ends a little after the display took the frame, at its vertical blank. Then the time: the frame after it is for the
  // blank after, and its start is held so that it is ready at its place, the frame margin before it, and no sooner
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {Blank(1) + 500'000, true});
  EXPECT_EQ(frame.AskedForId, 1u);
  EXPECT_EQ(frame.StartNanoseconds, Blank(1) + Place - Margin - Work);
  EXPECT_EQ(frame.PresentNanoseconds, Blank(1) + Place - Margin);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(2)));
  EXPECT_EQ(frame.Schedule.AnimationStep, Span(Period));
  // The time the next frame starts, if it takes as long: its plan has it right
  EXPECT_EQ(frame.Schedule.NextFrameStartTime, At(Blank(2) + Place - Margin - Work));

  // And so on: a frame every vertical blank, none late, and the wait says nothing the pacer had not worked out
  for (int64_t number = 3; number < 100; ++number)
  {
    // A wait that ends later than that time is when the frame starts: every second one here, 2 ms after the vertical blank
    const bool late = (number % 2) == 0;
    frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {Blank(number - 1) + (late ? 2'000'000 : 500'000), true});
    ASSERT_EQ(frame.AskedForId, static_cast<uint64_t>(number - 1)) << number;
    ASSERT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(number))) << number;
    ASSERT_EQ(frame.StartNanoseconds, late ? Blank(number - 1) + 2'000'000 : Blank(number - 1) + Place - Margin - Work) << number;
  }
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.ShownLaterByWaits(), 0u);
  EXPECT_EQ(pacer.PresentWaitTimeouts(), 0u);
  EXPECT_EQ(pacer.VBlankJumps(), 0u);
  EXPECT_EQ(pacer.ReadyPlaceNow(), Span(Place));
}

TEST(VBlankWaitForPresentPacer, WithTwoPresentsThatMayWaitItAsksForThePresentBeforeTheLast)
{
  PC::PacerSettings settings = Settings(PC::PacerAim::LowLatency, 2);
  settings.SetPresentWaitSwapIntervals(3);
  PC::VBlankWaitForPresentPacer pacer(settings);
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 1'000'000);
  // One present was made: it may wait
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000);
  EXPECT_EQ(frame.AskedForId, 0u);
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000);
  EXPECT_EQ(frame.AskedForId, 1u);
  EXPECT_EQ(frame.TimeoutNanoseconds, 3 * Period);
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000);
  EXPECT_EQ(frame.AskedForId, 2u);
}

TEST(VBlankWaitForPresentPacer, AWaitThatHeldTheLoopSaysWhichVerticalBlankAFrameWasShownAt)
{
  for (const uint32_t waitingPresents : {1u, 2u})
  {
    PC::VBlankWaitForPresentPacer pacer(Settings(PC::PacerAim::LowLatency, waitingPresents));
    AddBlank(pacer, Blank(0));
    // The vertical blank each frame was shown at, by its frame id: what the display did
    std::vector<int64_t> shownAt{0};
    FrameResult frame = Frame(pacer, Blank(0) + 1'000'000);
    shownAt.push_back(BlankOf(frame));
    // A frame: the wait for the present the plan asks for ends half a millisecond after the display took that frame, or at once
    // when that has passed. lostRefreshes: the display shows the frame waited for that many vertical blanks later than it was
    // made for, and the frames after it with it
    const auto next = [&pacer, &frame, &shownAt](const int64_t lostRefreshes)
    {
      const int64_t now = frame.PresentNanoseconds + 100'000;
      const uint64_t askedFor = pacer.PlanFrame(At(now)).WaitForPresentFrameId;
      if (askedFor != 0)
      {
        for (std::size_t id = askedFor; id < shownAt.size(); ++id)
        {
          shownAt[id] += lostRefreshes;
        }
      }
      frame = Frame(pacer, now, {askedFor != 0 ? std::max(now + 50'000, Blank(shownAt[askedFor]) + 500'000) : 0, true});
      shownAt.push_back(BlankOf(frame));
    };
    for (int64_t number = 2; number <= 10; ++number)
    {
      next(0);
      ASSERT_EQ(BlankOf(frame), number) << waitingPresents;
    }
    ASSERT_EQ(pacer.ShownLaterByWaits(), 0u);
    ASSERT_EQ(pacer.FrameWindow().LateFrames, 0u);

    // The display loses a refresh by itself: the frame the wait is for is shown a vertical blank later than it was made for,
    // and the wait holds the loop until then. The frame last made is late by it, and the one that starts now is for the blank
    // after that: one later than it would have been
    next(1);
    EXPECT_EQ(BlankOf(frame), 12) << waitingPresents;
    EXPECT_EQ(pacer.ShownLaterByWaits(), 1u) << waitingPresents;
    EXPECT_EQ(pacer.RefreshesBehindClock(), 1u) << waitingPresents;
    EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u) << waitingPresents;
    // Its animation step is from where the frame before it was shown, which was a blank later than that frame was made for
    EXPECT_EQ(frame.Schedule.AnimationStep, Span(Period)) << waitingPresents;

    // One such frame is a refresh the display lost: a frame is to be ready where it was, and the frames go on a blank apart
    EXPECT_EQ(pacer.ReadyPlaceNow(), Span(Place));
    for (int64_t number = 13; number <= 40; ++number)
    {
      next(0);
      ASSERT_EQ(BlankOf(frame), number) << waitingPresents;
    }
    EXPECT_EQ(pacer.ShownLaterByWaits(), 1u) << waitingPresents;
    EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u) << waitingPresents;
  }
}

TEST(VBlankWaitForPresentPacer, AWaitThatReturnedAtOnceSaysTheFrameWasShownByThen)
{
  PC::VBlankWaitForPresentPacer pacer(Settings(PC::PacerAim::LowLatency));
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 1'000'000);
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {Blank(1) + 2'000'000, true});
  ASSERT_EQ(BlankOf(frame), 2);

  // A frame that runs long: it is for vertical blank 3 and is done 0.8 of a period after it. Ready that late in a refresh, the
  // aim of low latency does not count on it for blank 4: without a word from the display it would take it as shown at blank 5
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {Blank(2) + 500'000, true}, (Blank(3) + 8'000'000) - (Blank(2) + Place - Margin - Work));
  ASSERT_EQ(BlankOf(frame), 3);
  ASSERT_EQ(frame.PresentNanoseconds, Blank(3) + 8'000'000);

  // The loop comes around 3 ms after blank 4, and the wait for that present returns at once: it was shown by then, at blank 4.
  // So the next frame is for blank 5 and not for blank 6
  frame = Frame(pacer, Blank(4) + 3'000'000);
  EXPECT_EQ(frame.AskedForId, 3u);
  EXPECT_EQ(BlankOf(frame), 5);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);
  EXPECT_EQ(pacer.ShownLaterByWaits(), 0u);
}

TEST(VBlankWaitForPresentPacer, WithTheAimOfSmoothnessAFrameStartsWhenTheWaitIsOverAndItsPresentIsHeld)
{
  PC::VBlankWaitForPresentPacer pacer(Settings(PC::PacerAim::Smoothness, 2));
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 1'000'000);
  EXPECT_EQ(frame.StartNanoseconds, Blank(0) + 1'000'000);
  // No time to start at: the frame after this one starts when the wait is over. Its present is held so that it is ready at its
  // place a refresh before the refresh before its vertical blank: one frame of reserve
  for (int64_t number = 2; number < 60; ++number)
  {
    const int64_t now = frame.PresentNanoseconds + 100'000;
    ASSERT_FALSE(pacer.PlanFrame(At(now)).WaitsForStartTime()) << number;
    frame = Frame(pacer, now);
    ASSERT_EQ(frame.StartNanoseconds, number == 2 ? now : now + 50'000) << number;
    ASSERT_EQ(BlankOf(frame), number) << number;
    if (number > 3)
    {
      ASSERT_EQ(frame.PresentNanoseconds, Blank(number - 2) + Place) << number;
    }
  }
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);

  // A frame that takes three times as long uses up the reserve and is still shown at its vertical blank
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {}, 3 * Work);
  EXPECT_EQ(BlankOf(frame), 60);
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000);
  EXPECT_EQ(BlankOf(frame), 61);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
}

TEST(VBlankWaitForPresentPacer, AWindowThatIsNotShownTeachesNothingOfWhereAFrameIsToBeReady)
{
  PC::VBlankWaitForPresentPacer pacer(Settings(PC::PacerAim::LowLatency));
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 1'000'000);
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {Blank(1) + 2'000'000, true});
  // A frame whose wait says that the frame before it was shown a vertical blank later than it was made for
  const auto shownLate = [&pacer, &frame]()
  {
    const int64_t shownAt = BlankOf(frame) + 1;
    frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {Blank(shownAt) + 2'000'000, true});
  };
  // A frame whose wait is over at once: the frame before it was shown where it was made for
  const auto shownInTime = [&pacer, &frame]() { frame = Frame(pacer, frame.PresentNanoseconds + 100'000); };

  // The window is covered for a moment: one wait runs out, four periods after it began. The frames after it that are shown
  // later than worked out are counted, and the place a frame is to be ready at stays
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {frame.PresentNanoseconds + 100'000 + (4 * Period), false});
  EXPECT_EQ(pacer.PresentWaitTimeouts(), 1u);
  for (int32_t count = 0; count < 6; ++count)
  {
    shownLate();
  }
  EXPECT_EQ(pacer.ShownLaterByWaits(), 6u);
  EXPECT_EQ(pacer.ReadyPlaceNow(), Span(Place));
  // Eight frames after the wait that ran out the display is taken to show the window again: two more move the place
  shownInTime();
  shownInTime();
  shownLate();
  shownLate();
  EXPECT_EQ(pacer.ShownLaterByWaits(), 8u);
  EXPECT_EQ(pacer.ReadyPlaceNow(), Span(Place - (Period / 8)));
  // A wait that runs out in the frames right after the place was moved: the window was going out of view, and the place is
  // moved back
  shownInTime();
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {frame.PresentNanoseconds + 100'000 + (4 * Period), false});
  EXPECT_EQ(pacer.PresentWaitTimeouts(), 2u);
  EXPECT_EQ(pacer.ReadyPlaceNow(), Span(Place));

  // While the waits are stopped nothing is learnt either
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {frame.PresentNanoseconds + 100'000 + (4 * Period), false});
  EXPECT_TRUE(pacer.PresentWaitsStopped());
  for (int32_t count = 0; count < 40; ++count)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {0, false});
  }
  EXPECT_TRUE(pacer.PresentWaitsStopped());
  EXPECT_EQ(pacer.ReadyPlaceNow(), Span(Place));
}

TEST(VBlankWaitForPresentPacer, FramesThatAreShownLaterThanWorkedOutMoveThePlaceAFrameIsToBeReadyAt)
{
  PC::VBlankWaitForPresentPacer pacer(Settings(PC::PacerAim::LowLatency));
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 1'000'000);
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {Blank(1) + 2'000'000, true});

  // This display takes a frame sooner before a vertical blank than the pacer has it ready: every frame is shown a blank later
  // than it was made for, and the wait for it says so. Two such frames, and a frame is to be ready an eighth of a period sooner
  int64_t shownAt = BlankOf(frame) + 1;
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {Blank(shownAt) + 2'000'000, true});
  EXPECT_EQ(pacer.ShownLaterByWaits(), 1u);
  EXPECT_EQ(pacer.ReadyPlaceNow(), Span(Place));
  shownAt = BlankOf(frame) + 1;
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {Blank(shownAt) + 2'000'000, true});
  EXPECT_EQ(pacer.ShownLaterByWaits(), 2u);
  EXPECT_EQ(pacer.ReadyPlaceNow(), Span(Place - (Period / 8)));

  // It goes on while frames are shown late, down to the start of the refresh, and no further
  for (int32_t count = 0; count < 20; ++count)
  {
    shownAt = BlankOf(frame) + 1;
    frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {Blank(shownAt) + 2'000'000, true});
  }
  EXPECT_EQ(pacer.ReadyPlaceNow(), Span(0));
  EXPECT_EQ(pacer.ShownLaterByWaits(), 22u);

  // The place stays when the pacer starts again after a pause, and is the settings' again after a reset and with other settings
  pacer.Reset();
  EXPECT_EQ(pacer.ReadyPlaceNow(), Span(Place));
  PC::PacerSettings other = Settings(PC::PacerAim::LowLatency);
  other.SetReadyPlacePercent(30);
  pacer.SetSettings(other);
  EXPECT_EQ(pacer.ReadyPlaceNow(), Span(3'000'000));
  EXPECT_EQ(pacer.Settings().ReadyPlacePercent(), 30u);
}

TEST(VBlankWaitForPresentPacer, AWindowThatIsNotShownDoesNotSlowThePacerDown)
{
  PC::VBlankWaitForPresentPacer pacer(Settings(PC::PacerAim::LowLatency));
  ASSERT_TRUE(pacer.Settings().AutoSwapInterval());
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 1'000'000);
  for (int32_t count = 0; count < 50; ++count)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {frame.PresentNanoseconds + 6'000'000, true});
  }
  const uint32_t framesInView = pacer.FrameWindow().Frames;

  // Covered: two waits run out after the four periods they were given, the frames they held are not judged, and the pacer stops
  // waiting
  for (int32_t count = 0; count < 2; ++count)
  {
    const int64_t now = frame.PresentNanoseconds + 100'000;
    frame = Frame(pacer, now, {now + (4 * Period), false});
    EXPECT_EQ(frame.TimeoutNanoseconds, 4 * Period);
    EXPECT_GE(frame.StartNanoseconds, now + (4 * Period));
  }
  EXPECT_TRUE(pacer.PresentWaitsStopped());
  EXPECT_EQ(pacer.PresentWaitTimeouts(), 2u);
  EXPECT_EQ(pacer.FrameWindow().Frames, framesInView);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);

  // From then on a frame every vertical blank, and once in sixteen frames the plan asks after an older present with no time to
  // wait. An answer that says shown once is a frame of the covered window shown in passing
  int32_t asks = 0;
  for (int32_t count = 0; count < 200; ++count)
  {
    const int64_t before = BlankOf(frame);
    const int64_t now = frame.PresentNanoseconds + 100'000;
    frame = Frame(pacer, now, {now, count == 31});
    asks += frame.AskedForId != 0u ? 1 : 0;
    ASSERT_EQ(frame.TimeoutNanoseconds, 0) << count;
    ASSERT_EQ(BlankOf(frame), before + 1) << count;
    ASSERT_TRUE(pacer.PresentWaitsStopped()) << count;
  }
  EXPECT_EQ(asks, 12);
  EXPECT_EQ(pacer.PresentWaitTimeouts(), 2u + 11u);
  EXPECT_EQ(pacer.SwapInterval(), 1u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);

  // In view again: two answers in a row say shown, and the next frame waits as before
  int32_t frames = 0;
  while (pacer.PresentWaitsStopped() && frames < 100)
  {
    const int64_t now = frame.PresentNanoseconds + 100'000;
    frame = Frame(pacer, now, {now, true});
    ++frames;
  }
  EXPECT_LE(frames, 32);
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {frame.PresentNanoseconds + 6'000'000, true});
  EXPECT_EQ(frame.TimeoutNanoseconds, 4 * Period);
  EXPECT_EQ(frame.AskedForId, frame.Schedule.FrameId - 1u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);

  // A new swap chain's presents are waited for again at once
  for (int32_t count = 0; count < 2; ++count)
  {
    const int64_t now = frame.PresentNanoseconds + 100'000;
    frame = Frame(pacer, now, {now + (4 * Period), false});
  }
  ASSERT_TRUE(pacer.PresentWaitsStopped());
  pacer.ForgetPresents();
  EXPECT_FALSE(pacer.PresentWaitsStopped());
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000);
  EXPECT_EQ(frame.AskedForId, 0u);
}

TEST(VBlankWaitForPresentPacer, APresentTheSystemDidNotTakeIsNotWaitedForNorAnyBeforeIt)
{
  PC::VBlankWaitForPresentPacer pacer(Settings(PC::PacerAim::LowLatency));
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 1'000'000);
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {}, Work, false);
  EXPECT_EQ(frame.AskedForId, 1u);
  // Frame 2's present was not taken: nothing to wait for, and after the next one it is that one
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000);
  EXPECT_EQ(frame.AskedForId, 0u);
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000);
  EXPECT_EQ(frame.AskedForId, 3u);

  // After a reset no present from before is waited for
  pacer.Reset();
  frame = Frame(pacer, frame.PresentNanoseconds + (5 * Period));
  EXPECT_EQ(frame.AskedForId, 0u);
  EXPECT_EQ(frame.Schedule.AnimationStep, Span(Period));
}

TEST(VBlankWaitForPresentPacer, WhatAWaitSaysAcrossAPauseOrOfAFrameThatIsNotThePacersIsNotUsed)
{
  PC::VBlankWaitForPresentPacer pacer(Settings(PC::PacerAim::LowLatency));
  // Before there is a frame a wait says nothing
  PC::PresentWaitReport report;
  report.FrameId = 1;
  report.BeginTime = At(Blank(0));
  report.EndTime = At(Blank(0) + (3 * Period));
  pacer.AddPresentWait(report);
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 1'000'000);
  frame = Frame(pacer, frame.PresentNanoseconds + 100'000, {Blank(1) + 2'000'000, true});
  ASSERT_EQ(BlankOf(frame), 2);

  // Of a frame the pacer has not made, and of no frame
  report.FrameId = 9;
  report.BeginTime = At(Blank(1) + 6'000'000);
  report.EndTime = At(Blank(4) + 2'000'000);
  pacer.AddPresentWait(report);
  report.FrameId = 0;
  pacer.AddPresentWait(report);
  // A wait that ended longer after the last frame than the pacer measures across (a pause)
  report.FrameId = 2;
  report.EndTime = At(Blank(600) + 2'000'000);
  pacer.AddPresentWait(report);
  static_cast<void>(pacer.BeginFrame(At(Blank(2) + Place - Margin - Work)));
  EXPECT_EQ(pacer.ShownLaterByWaits(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
}

TEST(VBlankWaitForPresentPacer, TheRestIsThePacerOfVerticalBlankTimes)
{
  PC::VBlankWaitForPresentPacer pacer(Settings(PC::PacerAim::LowLatency));
  // Nothing is open: no present plan and no CPU busy time
  EXPECT_EQ(pacer.EndFrame(At(Start)).FrameId, 0u);
  EXPECT_EQ(pacer.CpuBusyAt(At(Start)), FP::NanosecondTimeSpan32());

  // Until a reading the first frame's start is taken as a vertical blank
  PC::FrameSchedule schedule = pacer.BeginFrame(At(Start));
  EXPECT_EQ(schedule.IntendedDisplayTime, At(Start + Period));
  EXPECT_EQ(pacer.CpuBusyAt(At(Start + 2'000'000)), FP::NanosecondTimeSpan32(2'000'000u));
  const PC::PresentPlan present = pacer.EndFrame(At(Start + Work));
  EXPECT_EQ(present.CpuBusy, FP::NanosecondTimeSpan32(static_cast<uint32_t>(Work)));
  PC::PresentReport report;
  report.FrameId = present.FrameId;
  report.CallTime = At(Start + Work);
  report.ReturnTime = At(Start + Work + 700'000);
  pacer.AddPresent(report);
  EXPECT_EQ(pacer.LastPresentBlocked(), FP::NanosecondTimeDuration::FromNanoseconds(700'000));

  // A reading puts the vertical blanks where the display has them, an older one is not taken, and one that is off is counted
  // and not taken by itself; eight in a row on a grid of their own are, the same one twice counting once
  AddBlank(pacer, Blank(1) + 300'000);
  AddBlank(pacer, Blank(0));
  AddBlank(pacer, Blank(2) + 300'000 + 4'000'000);
  EXPECT_EQ(pacer.VBlankJumps(), 1u);
  AddBlank(pacer, Blank(3) + 300'000);
  EXPECT_EQ(pacer.VBlankJumps(), 1u);
  AddBlank(pacer, Blank(4) + 4'300'000);
  AddBlank(pacer, Blank(4) + 4'300'000);
  for (int64_t number = 5; number < 12; ++number)
  {
    AddBlank(pacer, Blank(number) + 4'300'000);
  }
  EXPECT_EQ(pacer.VBlankJumps(), 10u);
  AddBlank(pacer, Blank(12) + 4'300'000);
  EXPECT_EQ(pacer.VBlankJumps(), 10u);
  for (int64_t number = 13; number < 21; ++number)
  {
    AddBlank(pacer, Blank(number) + 300'000);
  }
  EXPECT_EQ(pacer.VBlankJumps(), 18u);

  // GPU work reports: a frame is ready when the GPU is done with it
  pacer.AddGpuWork(PC::GpuWorkReport::OfDuration(1, FP::NanosecondTimeDuration::FromNanoseconds(2'000'000)));
  EXPECT_EQ(pacer.GpuTime(), FP::NanosecondTimeDuration::FromNanoseconds(2'000'000));

  // A frame that starts too late for the vertical blank its swap interval gives is for the first it can make, and is late: its
  // animation step has the refreshes. A frame without an end is not judged by its work
  schedule = pacer.BeginFrame(At(Blank(4) + 300'000));
  EXPECT_EQ(schedule.IntendedDisplayTime, At(Blank(5) + 300'000));
  EXPECT_EQ(schedule.AnimationStep, Span(4 * Period));
  schedule = pacer.BeginFrame(At(Blank(5) + 300'000));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  schedule = pacer.BeginFrame(At(Blank(6) + 300'000));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);

  // Another refresh period drops the readings and the frames start again on it; the animation time goes on
  const FP::NanosecondTimeSpan animationTime = schedule.AnimationTime;
  pacer.SetRefreshPeriod(g_hz100);
  EXPECT_TRUE(pacer.HasVBlankReading());
  pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(50));
  EXPECT_FALSE(pacer.HasVBlankReading());
  schedule = pacer.BeginFrame(At(Blank(8)));
  EXPECT_EQ(schedule.AnimationTime, animationTime + Span(2 * Period));
  EXPECT_EQ(schedule.TargetFrameTime, FP::NanosecondTimeSpan32(static_cast<uint32_t>(2 * Period)));
  // The same settings change nothing; others on the same period keep the readings
  AddBlank(pacer, Blank(30));
  pacer.SetSettings(pacer.Settings());
  PC::PacerSettings other = pacer.Settings();
  other.SetAim(PC::PacerAim::Smoothness);
  pacer.SetSettings(other);
  EXPECT_TRUE(pacer.HasVBlankReading());
  other.SetRefresh(g_hz100);
  pacer.SetSettings(other);
  EXPECT_FALSE(pacer.HasVBlankReading());
}
