// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The wait for a present as a part of its own (sdk/doc/pacer-design.md, "How a pacer is put together"): which
// present a frame start plan asks for, how long the wait may take, and what a wait that ran out means. The pacers that have
// the wait are tested with it in their own files; these tests are of the part alone.

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/hold/PresentWaitRule.hpp>
#include <gtest/gtest.h>
#include <cstdint>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  // 100 Hz: a refresh period of exactly 10,000,000 ns
  constexpr int64_t Period = 10'000'000;
  constexpr int64_t Start = 1'000'000'000;

  const PC::RefreshPeriod g_hz100 = PC::RefreshPeriod::FromRate(100);

  PC::PacerSettings Settings(const uint32_t waitingPresents)
  {
    PC::PacerSettings settings(g_hz100);
    settings.SetWaitingPresents(waitingPresents);
    return settings;
  }

  //! A present the system took, or one it refused
  void Present(PC::PresentWaitRule& rRule, const uint64_t frameId, const bool accepted = true)
  {
    PC::PresentReport report;
    report.FrameId = frameId;
    report.CallTime = FP::NanosecondTickCount(Start);
    report.ReturnTime = FP::NanosecondTickCount(Start + 60'000);
    report.Accepted = accepted;
    rRule.AddPresent(report);
  }

  //! A wait for a present that took so long and ended with the present shown or not
  PC::PresentWaitReport Wait(const uint64_t frameId, const int64_t blockedNanoseconds, const bool shown)
  {
    PC::PresentWaitReport report;
    report.FrameId = frameId;
    report.BeginTime = FP::NanosecondTickCount(Start);
    report.EndTime = FP::NanosecondTickCount(Start + blockedNanoseconds);
    report.Shown = shown;
    return report;
  }

  //! The frame id the plan asks for: 0 for none
  uint64_t AskedFor(const PC::PresentWaitRule& rule, const PC::PacerSettings& settings, const uint32_t swapInterval = 1)
  {
    return rule.Plan(settings, g_hz100, swapInterval).WaitForPresentFrameId;
  }
}

TEST(PresentWaitRule, ThePlanAsksForThePresentSoManyBackThatThePresentsThatMayWaitAreInBetween)
{
  const PC::PacerSettings two = Settings(2);
  PC::PresentWaitRule rule;
  // Nothing was presented: nothing to wait for, and the plan has no start time either
  EXPECT_EQ(AskedFor(rule, two), 0u);
  EXPECT_FALSE(rule.Plan(two, g_hz100, 1).WaitsForStartTime());
  // Two presents may wait: before frame 2 none is waited for, before frame 3 the first is
  Present(rule, 1);
  EXPECT_EQ(AskedFor(rule, two), 0u);
  Present(rule, 2);
  EXPECT_EQ(AskedFor(rule, two), 1u);
  // The longest the wait may take is four of the frame's own swap intervals
  EXPECT_EQ(rule.Plan(two, g_hz100, 1).WaitForPresentTimeout.Nanoseconds(), 4 * Period);
  EXPECT_EQ(rule.Plan(two, g_hz100, 3).WaitForPresentTimeout.Nanoseconds(), 12 * Period);
  PC::PacerSettings longer = two;
  longer.SetPresentWaitSwapIntervals(6);
  EXPECT_EQ(rule.Plan(longer, g_hz100, 2).WaitForPresentTimeout.Nanoseconds(), 12 * Period);
  // One may wait: the present just made is waited for. Three: the one three back
  EXPECT_EQ(AskedFor(rule, Settings(1)), 2u);
  EXPECT_EQ(AskedFor(rule, Settings(3)), 0u);
  Present(rule, 3);
  EXPECT_EQ(AskedFor(rule, Settings(3)), 1u);

  // A wait that was made: its present was shown, and its end says something of the display. A frame has one wait: the plan
  // asks for no second one before the frame starts, and not for the same present again after it
  EXPECT_TRUE(rule.AddPresentWait(Wait(2, 2'000'000, true), g_hz100));
  EXPECT_EQ(AskedFor(rule, two), 0u);
  rule.BeginFrame();
  EXPECT_EQ(AskedFor(rule, two), 0u);
  Present(rule, 4);
  EXPECT_EQ(AskedFor(rule, two), 3u);
  EXPECT_EQ(rule.Timeouts(), 0u);
  EXPECT_FALSE(rule.WaitRanOut());
  EXPECT_FALSE(rule.Disturbed());
}

TEST(PresentWaitRule, AWaitHeldTheLoopWhenItTookAnEighthOfARefreshPeriod)
{
  EXPECT_TRUE(PC::PresentWaitRule::HeldTheLoop(Wait(1, Period / 8, true), g_hz100));
  EXPECT_FALSE(PC::PresentWaitRule::HeldTheLoop(Wait(1, (Period / 8) - 1, true), g_hz100));
  // A wait that ended before it began held nothing
  EXPECT_FALSE(PC::PresentWaitRule::HeldTheLoop(Wait(1, -Period, true), g_hz100));
  static_assert(PC::PresentWaitRule::HeldDivisor == 8);
}

TEST(PresentWaitRule, WaitsThatRunOutStopTheWaitsAndAnswersThatSayShownStartThemAgain)
{
  const PC::PacerSettings settings = Settings(1);
  PC::PresentWaitRule rule;
  uint64_t frame = 0;
  // A frame of the loop: its present, and the wait before the next one if the plan asks for it
  const auto next = [&rule, &settings, &frame](const bool shown, const int64_t blockedNanoseconds)
  {
    Present(rule, ++frame);
    const PC::FrameStartPlan plan = rule.Plan(settings, g_hz100, 1);
    if (plan.WaitsForPresent())
    {
      static_cast<void>(rule.AddPresentWait(Wait(plan.WaitForPresentFrameId, blockedNanoseconds, shown), g_hz100));
    }
    return plan;
  };

  // One wait that runs out after holding the loop: counted, and the frame it held starts late by the pacer's doing
  PC::FrameStartPlan plan = next(false, 4 * Period);
  EXPECT_EQ(plan.WaitForPresentFrameId, 1u);
  EXPECT_EQ(rule.Timeouts(), 1u);
  EXPECT_TRUE(rule.WaitRanOut());
  EXPECT_TRUE(rule.Disturbed());
  EXPECT_FALSE(rule.Stopped());
  rule.BeginFrame();
  EXPECT_FALSE(rule.WaitRanOut());
  EXPECT_FALSE(rule.Disturbed());
  // One that was shown in between: the count of waits in a row starts again
  static_cast<void>(next(true, 1'000));
  rule.BeginFrame();
  static_cast<void>(next(false, 4 * Period));
  rule.BeginFrame();
  EXPECT_FALSE(rule.Stopped());
  // Two in a row: the waits stop, which is a window that is not shown for as long as it lasts
  static_cast<void>(next(false, 4 * Period));
  EXPECT_TRUE(rule.Stopped());
  EXPECT_EQ(rule.Timeouts(), 3u);
  rule.BeginFrame();
  EXPECT_TRUE(rule.Disturbed());
  static_assert(PC::PresentWaitRule::WaitsRunOutToStop == 2u);

  // While stopped the plan asks once in sixteen frames, with no time to wait, after a present four frames older than a
  // wait would be for, and none from before the first wait that ran out
  uint32_t asks = 0;
  uint32_t framesToTheFirstAsk = 0;
  for (uint32_t count = 1; count <= 40; ++count)
  {
    plan = next(false, 1'000);
    if (plan.WaitsForPresent())
    {
      ++asks;
      framesToTheFirstAsk = framesToTheFirstAsk == 0 ? count : framesToTheFirstAsk;
      EXPECT_EQ(plan.WaitForPresentTimeout.Nanoseconds(), 0);
      EXPECT_EQ(plan.WaitForPresentFrameId, frame - 4u);
    }
    rule.BeginFrame();
    ASSERT_TRUE(rule.Stopped()) << count;
  }
  EXPECT_EQ(framesToTheFirstAsk, PC::PresentWaitRule::FramesBetweenAsks);
  EXPECT_EQ(asks, 2u);
  // Every answer that did not say shown is counted, and an answer that did not hold the loop holds no frame
  EXPECT_EQ(rule.Timeouts(), 5u);
  EXPECT_FALSE(rule.WaitRanOut());

  // An answer that says shown, then one that does not: the stop goes on. Two in a row end it, and the answer that ends it
  // is no wait that says something of the display
  const auto ask = [&rule, &next](const bool shown)
  {
    for (uint32_t count = 0; count < 64; ++count)
    {
      const PC::FrameStartPlan asked = next(shown, 1'000);
      rule.BeginFrame();
      if (asked.WaitsForPresent())
      {
        return;
      }
    }
    FAIL() << "the plan never asked";
  };
  ask(true);
  EXPECT_TRUE(rule.Stopped());
  ask(false);
  EXPECT_TRUE(rule.Stopped());
  ask(true);
  EXPECT_TRUE(rule.Stopped());
  ask(true);
  EXPECT_FALSE(rule.Stopped());
  static_assert(PC::PresentWaitRule::AsksShownToWait == 2u);
  // The waits are made again
  plan = next(true, 1'000);
  EXPECT_EQ(plan.WaitForPresentFrameId, frame);
  EXPECT_EQ(plan.WaitForPresentTimeout.Nanoseconds(), 4 * Period);
}

TEST(PresentWaitRule, AnAnswerWhileStoppedThatHeldTheLoopHoldsTheFrameAsAWaitDoes)
{
  PC::PresentWaitRule rule;
  for (uint64_t frame = 1; frame <= 2; ++frame)
  {
    Present(rule, frame);
    EXPECT_FALSE(rule.AddPresentWait(Wait(frame, 4 * Period, false), g_hz100));
    rule.BeginFrame();
  }
  ASSERT_TRUE(rule.Stopped());
  // The system took 9 ms to answer: the frame after it is late by the pacer's asking
  EXPECT_FALSE(rule.AddPresentWait(Wait(1, 9'000'000, true), g_hz100));
  EXPECT_TRUE(rule.WaitRanOut());
  rule.BeginFrame();
  EXPECT_FALSE(rule.WaitRanOut());
}

TEST(PresentWaitRule, APresentThatWasRefusedAndASwapChainMadeAnewAreNotWaitedFor)
{
  const PC::PacerSettings settings = Settings(1);
  PC::PresentWaitRule rule;
  Present(rule, 1);
  EXPECT_EQ(AskedFor(rule, settings), 1u);
  // The system refused the present: it is never shown, and neither are the ones before it
  Present(rule, 2, false);
  EXPECT_EQ(AskedFor(rule, settings), 0u);
  // The frame is presented again, and taken: that present is waited for
  Present(rule, 2);
  EXPECT_EQ(AskedFor(rule, settings), 2u);
  // A new swap chain: the presents made so far are gone, and the waits that ran out with them
  EXPECT_FALSE(rule.AddPresentWait(Wait(2, 4 * Period, false), g_hz100));
  rule.BeginFrame();
  Present(rule, 3);
  EXPECT_FALSE(rule.AddPresentWait(Wait(3, 4 * Period, false), g_hz100));
  ASSERT_TRUE(rule.Stopped());
  rule.ForgetPresents();
  EXPECT_FALSE(rule.Stopped());
  EXPECT_TRUE(rule.WaitRanOut());
  rule.BeginFrame();
  EXPECT_EQ(AskedFor(rule, settings), 0u);
  Present(rule, 4);
  EXPECT_EQ(AskedFor(rule, settings), 4u);

  // A reset also drops what the waits before the next frame said
  EXPECT_FALSE(rule.AddPresentWait(Wait(4, 4 * Period, false), g_hz100));
  EXPECT_TRUE(rule.WaitRanOut());
  EXPECT_TRUE(rule.Disturbed());
  rule.Reset();
  EXPECT_FALSE(rule.WaitRanOut());
  EXPECT_FALSE(rule.Disturbed());
  EXPECT_FALSE(rule.Stopped());
  EXPECT_EQ(rule.Timeouts(), 3u);
}
