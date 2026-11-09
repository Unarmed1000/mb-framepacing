// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// The pacer module's comparisons: operator== and operator!= are written by hand (C++17 has no defaulted ones), so every field is
// checked to take part.
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/FrameRateStep.hpp>
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/RefreshTime.hpp>
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerRating.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <mb/framepacing/pacer/display/DisplayErrorState.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/rule/SlowDownRule.hpp>
#include <mb/framepacing/testing/EqualityCheck.hpp>
#include <gtest/gtest.h>
#include <cstdint>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;
namespace EC = MB::FramePacing::Testing::EqualityCheck;

namespace
{
  FP::NanosecondTimeSpan Milliseconds(const int64_t milliseconds)
  {
    return FP::NanosecondTimeSpan::FromMilliseconds(milliseconds);
  }

  FP::NanosecondTimeDuration Duration(const int64_t nanoseconds)
  {
    return FP::NanosecondTimeDuration::FromNanoseconds(nanoseconds);
  }
}

TEST(Comparison, CapabilitiesAndARatingCompareEveryField)
{
  EXPECT_EQ(EC::FirstChangeNotSeen(
              PC::PacerCapabilities(PC::PacerCapability::PresentSwapInterval, 2),
              [](PC::PacerCapabilities& changed) { changed = PC::PacerCapabilities(PC::PacerCapability::PresentAtTime); },
              [](PC::PacerCapabilities& changed) { changed = PC::PacerCapabilities(PC::PacerCapability::PresentSwapInterval, 3); }),
            -1);
  EXPECT_EQ(EC::FirstChangeNotSeen(
              PC::PacerRating{}, [](PC::PacerRating& changed) { changed.Tier = PC::PacerTier::AtTimeTimerPeriodOnly; },
              [](PC::PacerRating& changed) { changed.RaisesTier = PC::PacerCapability::NoCapabilities; },
              [](PC::PacerRating& changed) { changed.DisplaySideHolds = true; }, [](PC::PacerRating& changed) { changed.ReportsDisplayTimes = true; },
              [](PC::PacerRating& changed) { changed.SkipsOverdue = true; }, [](PC::PacerRating& changed) { changed.HoldsByFrameCallback = true; }),
            -1);
}

TEST(Comparison, TheDisplayErrorsCompareEveryField)
{
  using State = PC::DisplayErrorState;
  EXPECT_EQ(EC::FirstChangeNotSeen(
              State{}, [](State& changed) { changed.Reports = 1; }, [](State& changed) { changed.Refused = 1; }, [](State& changed)
              { changed.NotShown = 1; }, [](State& changed) { changed.JudgedFrames = 1; }, [](State& changed) { changed.ErrorFrames = 1; },
              [](State& changed) { changed.OffTargetFrames = 1; }, [](State& changed) { changed.LateFrames = 1; },
              [](State& changed) { changed.StartToDisplayFrames = 1; }, [](State& changed) { changed.StartToDisplayTotal = Duration(1); },
              [](State& changed) { changed.StartToDisplayLongest = Duration(1); }, [](State& changed) { changed.RecentJudgedFrames = 1; },
              [](State& changed) { changed.RecentErrorFrames = 1; }, [](State& changed) { changed.RecentOffTargetFrames = 1; },
              [](State& changed) { changed.RecentLateFrames = 1; }, [](State& changed) { changed.RecentStartToDisplayFrames = 1; }, [](State& changed)
              { changed.RecentStartToDisplayTotal = Duration(1); }, [](State& changed) { changed.RecentStartToDisplayLongest = Duration(1); }),
            -1);
}

TEST(Comparison, AFrameRateStepAndAFrameWindowCompareEveryField)
{
  EXPECT_EQ(EC::FirstChangeNotSeen(
              PC::FrameRateStep{}, [](PC::FrameRateStep& changed) { changed.SwapInterval = 2; }, [](PC::FrameRateStep& changed)
              { changed.FrameTime = Milliseconds(1); }, [](PC::FrameRateStep& changed) { changed.RateMillihertz = 1; }),
            -1);
  EXPECT_EQ(EC::FirstChangeNotSeen(
              PC::FrameWindowState{}, [](PC::FrameWindowState& changed) { changed.Frames = 1; }, [](PC::FrameWindowState& changed)
              { changed.LateFrames = 1; }, [](PC::FrameWindowState& changed) { changed.AverageWork = Milliseconds(1); },
              [](PC::FrameWindowState& changed) { changed.Span = Milliseconds(1); }, [](PC::FrameWindowState& changed) { changed.Full = true; },
              [](PC::FrameWindowState& changed) { changed.StartsAhead = Milliseconds(1); }),
            -1);
}

TEST(Comparison, ARefreshTimeComparesItsWholeNanosecondsAndItsFraction)
{
  const PC::RefreshPeriod period = PC::RefreshPeriod::FromRate(60);
  // A 60 Hz refresh is 16'666'666 ns and two thirds: the time after one has the whole nanoseconds of the second, and a fraction
  const PC::RefreshTime afterOne = PC::RefreshTime().After(1, period);
  ASSERT_EQ(afterOne.ToNanosecondTimeSpan(), FP::NanosecondTimeSpan(16'666'667));
  EXPECT_EQ(EC::FirstChangeNotSeen(
              afterOne, [](PC::RefreshTime& changed) { changed = PC::RefreshTime(FP::NanosecondTimeSpan(1)); },
              [](PC::RefreshTime& changed) { changed = PC::RefreshTime(FP::NanosecondTimeSpan(16'666'666)); }),
            -1);
}

TEST(Comparison, SettingsCompareEveryField)
{
  const PC::PacerSettings settings(PC::RefreshPeriod::FromRate(60));
  using Settings = PC::PacerSettings;
  EXPECT_EQ(EC::FirstChangeNotSeen(
              settings, [](Settings& changed) { changed.SetRefresh(PC::RefreshPeriod::FromRate(120)); }, [](Settings& changed)
              { changed.SetPreferredFrameTime(Milliseconds(33)); }, [](Settings& changed) { changed.SetPreferredSwapInterval(2); },
              [](Settings& changed) { changed.SetAutoSwapInterval(false); }, [](Settings& changed)
              { changed.SetSlowDown(PC::SlowDownRule::FullWindow); }, [](Settings& changed) { changed.SetFrameWindowLength(Milliseconds(3000)); },
              [](Settings& changed) { changed.SetSlowDownLatePercent(20); }, [](Settings& changed) { changed.SetFrameMargin(Milliseconds(2)); },
              // The default margin, set: only whether it was set differs
              [](Settings& changed) { changed.SetFrameMargin(Milliseconds(1)); }, [](Settings& changed)
              { changed.SetSlowestFrameTime(Milliseconds(60)); }, [](Settings& changed) { changed.SetAim(PC::PacerAim::LowLatency); },
              [](Settings& changed) { changed.SetWaitingPresents(1); }, [](Settings& changed) { changed.SetPresentWaitSwapIntervals(5); },
              [](Settings& changed) { changed.SetMinWaitTimeout(Duration(60'000'000)); }, [](Settings& changed) { changed.SetMaxFramesInFlight(2); },
              [](Settings& changed) { changed.SetStartupPauseRefreshes(5); },
              [](Settings& changed) { changed.SetStartupPauseDelay(Milliseconds(600)); }, [](Settings& changed) { changed.SetReadyPlacePercent(60); },
              [](Settings& changed) { changed.SetSwapChainImages(3); }, [](Settings& changed) { changed.SetSystemHoldsLoop(true); }),
            -1);
}
