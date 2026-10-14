// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The slip of the display behind the frames' plan, from reported display times (DisplaySlipRule): the least lag is no
// slip, a lag whole refreshes more is, the frames on their way when the plan moved are not counted again, and neither are the
// frames planned after a move that did not take the wait away.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/display/DisplayReport.hpp>
#include <mb/framepacing/pacer/hold/DisplaySlipRule.hpp>
#include <gtest/gtest.h>
#include <cstdint>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  // 100 Hz: a refresh period of exactly 10,000,000 ns, and the display's own delay behind the plan
  constexpr int64_t Period = 10'000'000;
  constexpr int64_t Start = 1'000'000'000;
  constexpr int64_t Delay = 3'000'000;

  const PC::RefreshPeriod g_hz100 = PC::RefreshPeriod::FromRate(100);

  //! Frame id's planned display time: a refresh apart from the first
  FP::NanosecondTickCount PlannedOf(const uint64_t frameId)
  {
    return FP::NanosecondTickCount(Start + (static_cast<int64_t>(frameId) * Period));
  }

  PC::DisplayReport Shown(const uint64_t frameId, const int64_t displayNanoseconds)
  {
    PC::DisplayReport report;
    report.FrameId = frameId;
    report.DisplayTime = FP::NanosecondTickCount(displayNanoseconds);
    return report;
  }

  //! Frame id shown at its planned time, the display's delay and a lag after it
  PC::DisplayReport ShownLate(const uint64_t frameId, const int64_t lagNanoseconds)
  {
    return Shown(frameId, PlannedOf(frameId).Nanoseconds() + Delay + lagNanoseconds);
  }

  //! Frame id, planned for the refresh of frame plannedAs, shown there with the display's delay and a lag after it
  PC::DisplayReport ShownAs(const uint64_t frameId, const uint64_t plannedAs, const int64_t lagNanoseconds)
  {
    return Shown(frameId, PlannedOf(plannedAs).Nanoseconds() + Delay + lagNanoseconds);
  }

  void Begin(PC::DisplaySlipRule& rRule, const uint64_t firstId, const uint64_t lastId)
  {
    for (uint64_t frameId = firstId; frameId <= lastId; ++frameId)
    {
      rRule.AddFrame(frameId, PlannedOf(frameId));
    }
  }
}

TEST(DisplaySlipRule, TheLeastLagIsTheDisplaysOwnDelayAndNoSlip)
{
  PC::DisplaySlipRule rule;
  Begin(rule, 1, 10);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(1, 0), g_hz100), 0);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(2, 0), g_hz100), 0);
  // Less than half a refresh more is no slip, nor is a lag below the least (which becomes the least)
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(3, (Period / 2) - 1), g_hz100), 0);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(4, -(Period / 3)), g_hz100), 0);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(5, (Period / 2) - (Period / 3) - 1), g_hz100), 0);
  EXPECT_EQ(rule.Moved(), 0);
}

TEST(DisplaySlipRule, AFrameShownARefreshLateMovesThePlanOnceAndTheFramesOnTheirWayAreNotCountedAgain)
{
  PC::DisplaySlipRule rule;
  Begin(rule, 1, 8);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(1, 0), g_hz100), 0);
  // Frame 2 is shown a refresh late, and the ones after it with it: the plan moves a refresh
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(2, Period), g_hz100), 1);
  EXPECT_EQ(rule.Moved(), 1);
  // Frames 3 to 8 began before the move and are shown a refresh late as well: no slip of their own
  for (uint64_t frameId = 3; frameId <= 8; ++frameId)
  {
    EXPECT_EQ(rule.AddDisplayReport(ShownLate(frameId, Period), g_hz100), 0) << frameId;
  }
  // The frames after the move are planned a refresh later, and shown where they are planned
  for (uint64_t frameId = 9; frameId <= 12; ++frameId)
  {
    rule.AddFrame(frameId, PlannedOf(frameId + 1));
  }
  for (uint64_t frameId = 9; frameId <= 12; ++frameId)
  {
    EXPECT_EQ(rule.AddDisplayReport(ShownAs(frameId, frameId + 1, 0), g_hz100), 0) << frameId;
  }
  // Two refreshes more are two refreshes
  EXPECT_EQ(rule.AddDisplayReport(ShownAs(12, 13, 2 * Period), g_hz100), 2);
  EXPECT_EQ(rule.Moved(), 3);
}

TEST(DisplaySlipRule, AMoveThatDidNotTakeTheWaitAwayIsNotMadeAgain)
{
  // A display with no frame waiting (the frames are late themselves): the frames planned after a move are shown as late against
  // the plan as before, and the plan is not moved for them again
  PC::DisplaySlipRule rule;
  Begin(rule, 1, 2);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(1, 0), g_hz100), 0);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(2, Period), g_hz100), 1);
  for (uint64_t frameId = 3; frameId <= 20; ++frameId)
  {
    rule.AddFrame(frameId, PlannedOf(frameId + 1));
    EXPECT_EQ(rule.AddDisplayReport(ShownAs(frameId, frameId + 1, Period), g_hz100), 0) << frameId;
  }
  EXPECT_EQ(rule.Moved(), 1);
}

TEST(DisplaySlipRule, WhatTheLoopFoundOutByItselfIsNoSlip)
{
  PC::DisplaySlipRule rule;
  Begin(rule, 1, 4);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(1, 0), g_hz100), 0);
  // Frame 2's present was late: the loop plans it a refresh later, and it is shown there
  rule.Replan(2, PlannedOf(3));
  EXPECT_EQ(rule.AddDisplayReport(ShownAs(2, 3, 0), g_hz100), 0);
  // A frame that is not kept is passed over
  rule.Replan(40, PlannedOf(41));
  rule.Replan(0, PlannedOf(41));
  // The loop moved its plan by itself, as for a start the display's side held: the frames on their way are shown late by it
  rule.AddMove(2);
  EXPECT_EQ(rule.Moved(), 2);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(3, 2 * Period), g_hz100), 0);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(4, 2 * Period), g_hz100), 0);
  // A replan after a move is on the plan as it is now
  rule.AddFrame(5, PlannedOf(7));
  rule.AddMove(1);
  rule.Replan(5, PlannedOf(9));
  EXPECT_EQ(rule.AddDisplayReport(ShownAs(5, 9, 0), g_hz100), 0);
  EXPECT_EQ(rule.Moved(), 3);
}

TEST(DisplaySlipRule, AReportOfAFrameNotShownOrNotKeptIsPassedOver)
{
  PC::DisplaySlipRule rule;
  // Before any frame
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(1, 5 * Period), g_hz100), 0);
  Begin(rule, 1, 70);
  // Frame 70 sets the least, then frames older than the kept ones, newer than the newest, and one not shown are passed over
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(70, 0), g_hz100), 0);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(6, 5 * Period), g_hz100), 0);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(71, 5 * Period), g_hz100), 0);
  PC::DisplayReport notShown = ShownLate(69, 5 * Period);
  notShown.Shown = false;
  EXPECT_EQ(rule.AddDisplayReport(notShown, g_hz100), 0);
  // The oldest kept frame is
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(70 - PC::DisplaySlipRule::Capacity + 1, Period), g_hz100), 1);
}

TEST(DisplaySlipRule, FramesThatStartAgainAreMeasuredAnew)
{
  PC::DisplaySlipRule rule;
  Begin(rule, 1, 3);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(1, 0), g_hz100), 0);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(2, Period), g_hz100), 1);
  // An id that is not the next one: the frames before are forgotten, and the least is measured anew
  Begin(rule, 10, 12);
  EXPECT_EQ(rule.Moved(), 0);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(3, 4 * Period), g_hz100), 0);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(10, 4 * Period), g_hz100), 0);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(11, 4 * Period), g_hz100), 0);
  // Id 0 is no frame: nothing is kept
  rule.AddFrame(0, PlannedOf(0));
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(0, 0), g_hz100), 0);
  // And Restart forgets them as well
  Begin(rule, 1, 2);
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(1, 0), g_hz100), 0);
  rule.Restart();
  EXPECT_EQ(rule.AddDisplayReport(ShownLate(2, Period), g_hz100), 0);
}
