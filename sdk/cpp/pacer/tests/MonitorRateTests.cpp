// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer on the refresh rates monitors have, from 50 to 540 Hz (59.94 and 119.88 Hz as the display modes state them): a frame loop
// paced by vsync, with frames on time, a display a little off its nominal rate, target frame rates, a load that comes and goes (with
// the default frame margin, which follows the refresh period, and with one set to 1 ms), and a loop the GPU limits.
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <array>
#include <cstdint>
#include <cstdlib>
#include <string>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  struct MonitorRate
  {
    uint32_t Numerator{0};
    uint32_t Denominator{1};

    [[nodiscard]] PC::RefreshPeriod Period() const noexcept
    {
      return PC::RefreshPeriod::FromRate(Numerator, Denominator);
    }

    [[nodiscard]] std::string Name() const
    {
      return std::to_string(Numerator) + (Denominator == 1 ? "" : "/" + std::to_string(Denominator)) + " Hz";
    }
  };

  constexpr std::array<MonitorRate, 23> MonitorRates{{{50},  {60'000, 1'001}, {60},  {72},  {75},  {85},  {90},  {100}, {120'000, 1'001},
                                                      {120}, {144},           {160}, {165}, {170}, {180}, {200}, {240}, {280},
                                                      {300}, {360},           {480}, {500}, {540}}};

  //! The frame rates applications ask for
  constexpr std::array<uint32_t, 11> TargetFrameRates{24, 25, 30, 48, 50, 60, 72, 90, 100, 120, 144};

  constexpr int64_t StartTicks = 100 * FP::TimeSpan::TicksPerSecond;

  //! A frame loop paced by vsync on a display: a frame is shown on the first refresh after it is done, no earlier than its swap
  //! interval after the previous frame, and the next frame starts when it is shown (plus the time the thread takes to wake up)
  class VsyncLoop
  {
    PC::FramePacer& m_pacer;
    PC::RefreshPeriod m_display;
    int64_t m_shownOnRefresh{0};

  public:
    VsyncLoop(PC::FramePacer& pacer, const PC::RefreshPeriod display) noexcept
      : m_pacer(pacer)
      , m_display(display)
    {
    }

    PC::FrameSchedule Frame(const FP::TimeSpan work, const FP::TimeSpan wakeUp = {})
    {
      const int64_t start = m_display.TimeFor(m_shownOnRefresh).Ticks() + wakeUp.Ticks();
      const PC::FrameSchedule schedule = m_pacer.BeginFrame(FP::TickCount64(StartTicks + start));
      static_cast<void>(m_pacer.EndFrame(FP::TickCount64(StartTicks + start + work.Ticks())));
      m_shownOnRefresh = std::max(m_shownOnRefresh + int64_t{schedule.SwapInterval}, m_display.RefreshesToFit(FP::TimeSpan(start + work.Ticks())));
      return schedule;
    }

    //! A frame the GPU finishes after the CPU is done with it: EndFrame comes when the CPU is done, with the GPU's time in the work
    //! the rule counts or without it
    PC::FrameSchedule GpuFrame(const FP::TimeSpan cpu, const FP::TimeSpan gpu, const bool gpuInWork)
    {
      const int64_t start = m_display.TimeFor(m_shownOnRefresh).Ticks();
      const PC::FrameSchedule schedule = m_pacer.BeginFrame(FP::TickCount64(StartTicks + start));
      const FP::TimeSpan work = gpuInWork ? FP::TimeSpan(cpu.Ticks() + gpu.Ticks()) : FP::TimeSpan();
      static_cast<void>(m_pacer.EndFrame(FP::TickCount64(StartTicks + start + cpu.Ticks()), work));
      const int64_t done = start + cpu.Ticks() + gpu.Ticks();
      m_shownOnRefresh = std::max(m_shownOnRefresh + int64_t{schedule.SwapInterval}, m_display.RefreshesToFit(FP::TimeSpan(done)));
      return schedule;
    }
  };

  //! A share of a refresh period, in thousandths
  FP::TimeSpan Share(const PC::RefreshPeriod period, const int64_t thousandths) noexcept
  {
    return FP::TimeSpan(period.ToTimeSpan().Ticks() * thousandths / 1'000);
  }

  //! The time a thread takes to wake up after vsync, different every frame: up to a fifth of a refresh
  FP::TimeSpan WakeUp(const PC::RefreshPeriod period, const int64_t frame) noexcept
  {
    return Share(period, (frame * 7'919) % 200);
  }
}

TEST(MonitorRates, FramesOnTimeAreOneRefreshEachAndTheAnimationDoesNotDrift)
{
  for (const MonitorRate rate : MonitorRates)
  {
    SCOPED_TRACE(rate.Name());
    const PC::RefreshPeriod period = rate.Period();
    PC::FramePacer pacer{PC::PacerSettings(period)};
    VsyncLoop loop(pacer, period);
    const int64_t frames = 20'000;
    int64_t wrongFrames = 0;
    PC::FrameSchedule schedule;
    for (int64_t frame = 0; frame < frames; ++frame)
    {
      schedule = loop.Frame(Share(period, 300), WakeUp(period, frame));
      const int64_t aimedAfter = schedule.IntendedDisplayTime.Ticks() - (StartTicks + period.TimeFor(frame).Ticks() + WakeUp(period, frame).Ticks());
      const bool right = schedule.SwapInterval == 1u && schedule.Change == PC::SwapIntervalChange::Unchanged &&
                         schedule.AnimationTime == period.TimeFor(frame) && std::abs(aimedAfter - period.ToTimeSpan().Ticks()) <= 1 &&
                         schedule.TargetFrameTime == FP::TimeSpan32::FromTimeSpan(period.ToTimeSpan());
      wrongFrames += right ? 0 : 1;
    }
    EXPECT_EQ(wrongFrames, 0);
    EXPECT_EQ(schedule.AnimationTime, period.TimeFor(frames - 1));
    EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
    EXPECT_EQ(pacer.SwapInterval(), 1u);
  }
}

TEST(MonitorRates, ADisplayATenthOfAPercentOffItsRateNeverHitches)
{
  for (const MonitorRate rate : MonitorRates)
  {
    for (const bool faster : {false, true})
    {
      SCOPED_TRACE(rate.Name() + (faster ? ", the display faster" : ", the display slower"));
      const PC::RefreshPeriod period = rate.Period();
      // The display's own clock, a thousandth off the rate the pacer is told
      const PC::RefreshPeriod display = faster ? PC::RefreshPeriod::FromRate(rate.Numerator * 1'001u, rate.Denominator * 1'000u)
                                               : PC::RefreshPeriod::FromRate(rate.Numerator * 1'000u, rate.Denominator * 1'001u);
      PC::FramePacer pacer{PC::PacerSettings(period)};
      VsyncLoop loop(pacer, display);
      int64_t wrongFrames = 0;
      FP::TimeSpan previous;
      for (int64_t frame = 0; frame < 20'000; ++frame)
      {
        const PC::FrameSchedule schedule = loop.Frame(Share(period, 300), WakeUp(period, frame));
        // Every step is one refresh of the rate the pacer was told: the display's drift never adds up to a double step
        const bool right = schedule.SwapInterval == 1u && schedule.Change == PC::SwapIntervalChange::Unchanged &&
                           (frame == 0 || std::abs((schedule.AnimationTime.Ticks() - previous.Ticks()) - period.ToTimeSpan().Ticks()) <= 1);
        wrongFrames += right ? 0 : 1;
        previous = schedule.AnimationTime;
      }
      EXPECT_EQ(wrongFrames, 0);
      EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
    }
  }
}

TEST(MonitorRates, ATargetFrameRateIsTheSmallestSwapIntervalThatReachesIt)
{
  for (const MonitorRate rate : MonitorRates)
  {
    for (const uint32_t target : TargetFrameRates)
    {
      SCOPED_TRACE(rate.Name() + ", " + std::to_string(target) + " fps wanted");
      const PC::RefreshPeriod period = rate.Period();
      PC::PacerSettings settings(period);
      settings.SetPreferredFrameRate(target);
      settings.SetAutoSwapInterval(false);
      const uint32_t swapInterval = settings.PreferredSwapIntervalAt(period);

      // The rule the tools judge a target frame rate by: the frame time in whole refreshes, rounded up, with a twentieth of a
      // refresh of slack, at least one. A tick either way is the rounding of the times
      const int64_t wanted = (FP::TimeSpan::TicksPerSecond / target) - (period.ToTimeSpan().Ticks() / 20);
      EXPECT_GE(swapInterval, 1u);
      EXPECT_GE(period.TimeFor(swapInterval).Ticks() + 1, wanted) << "it reaches the frame time";
      if (swapInterval > 1u)
      {
        EXPECT_LT(period.TimeFor(swapInterval - 1u).Ticks() - 1, wanted) << "and one refresh less would not";
      }

      // The pacer holds it: every frame for that many refreshes, none late, the marker's frame times that swap interval
      PC::FramePacer pacer(settings);
      VsyncLoop loop(pacer, period);
      int64_t wrongFrames = 0;
      PC::FrameSchedule schedule;
      const int64_t frames = 500;
      for (int64_t frame = 0; frame < frames; ++frame)
      {
        schedule = loop.Frame(Share(period, 300), WakeUp(period, frame));
        const bool right = schedule.SwapInterval == swapInterval && schedule.AnimationTime == period.TimeFor(frame * swapInterval) &&
                           schedule.TargetFrameTime == FP::TimeSpan32::FromTimeSpan(period.TimeFor(swapInterval)) &&
                           schedule.PreferredFrameTime == schedule.TargetFrameTime;
        wrongFrames += right ? 0 : 1;
      }
      EXPECT_EQ(wrongFrames, 0);
      EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
    }
  }
}

TEST(MonitorRates, AHeavyLoadSlowsDownAndALightOneComesBack)
{
  for (const MonitorRate rate : MonitorRates)
  {
    for (const bool marginOfAMillisecond : {false, true})
    {
      SCOPED_TRACE(rate.Name() + (marginOfAMillisecond ? ", a frame margin set to 1 ms" : ", the default frame margin"));
      const PC::RefreshPeriod period = rate.Period();
      PC::PacerSettings settings(period);
      if (marginOfAMillisecond)
      {
        settings.SetFrameMargin(PC::PacerSettings::DefaultFrameMargin);
      }
      PC::FramePacer pacer(settings);
      VsyncLoop loop(pacer, period);
      const int64_t framesPerWindow = period.RefreshesToFit(settings.FrameWindowLength());

      // Frames that need a refresh and a half: every one is late, and the pacer goes to every second refresh, where they fit
      const FP::TimeSpan heavy = Share(period, 1'400);
      for (int64_t frame = 0; frame < 2 * framesPerWindow; ++frame)
      {
        static_cast<void>(loop.Frame(heavy));
      }
      EXPECT_EQ(pacer.SwapInterval(), 2u);
      EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);

      // The load goes: after a frame window without a late frame the pacer is back on every refresh, when the frames' work and twice
      // the margin fit a refresh. The default margin is at most an eighth of a refresh, so it does on every display. A margin set
      // to 1 ms stays 1 ms: twice that and this work of a tenth of a refresh fit a refresh up to 450 Hz, so from 480 Hz on the
      // pacer stays at half rate
      const FP::TimeSpan light = Share(period, 100);
      for (int64_t frame = 0; frame < 3 * framesPerWindow; ++frame)
      {
        static_cast<void>(loop.Frame(light));
      }
      const bool room = light.Ticks() + (2 * settings.FrameMargin().Ticks()) < period.ToTimeSpan().Ticks();
      EXPECT_EQ(pacer.SwapInterval(), room ? 1u : 2u);
      if (marginOfAMillisecond)
      {
        EXPECT_EQ(room, period.ToTimeSpan() > FP::TimeSpan(22'200)) << "a margin of 1 ms: displays up to 450 Hz";
      }
      else
      {
        EXPECT_TRUE(room);
      }
    }
  }
}

TEST(MonitorRates, TheDefaultFrameMarginFollowsAChangeOfTheRefreshPeriod)
{
  // A pacer made for a 60 Hz display that moves to a 500 Hz one: the default margin is the new display's (0.25 ms), not the 1 ms of
  // the display it was made for, so it comes back to every refresh there too
  const PC::RefreshPeriod fast = PC::RefreshPeriod::FromRate(500);
  PC::FramePacer pacer{PC::PacerSettings(PC::RefreshPeriod::FromRate(60))};
  pacer.SetRefreshPeriod(fast);
  VsyncLoop loop(pacer, fast);
  const int64_t framesPerWindow = fast.RefreshesToFit(pacer.Settings().FrameWindowLength());
  for (int64_t frame = 0; frame < 2 * framesPerWindow; ++frame)
  {
    static_cast<void>(loop.Frame(Share(fast, 1'400)));
  }
  EXPECT_EQ(pacer.SwapInterval(), 2u);
  for (int64_t frame = 0; frame < 3 * framesPerWindow; ++frame)
  {
    static_cast<void>(loop.Frame(Share(fast, 100)));
  }
  EXPECT_EQ(pacer.SwapInterval(), 1u);
}

TEST(MonitorRates, AGpuBoundLoopNeedsTheGpusTimeInItsWork)
{
  for (const MonitorRate rate : MonitorRates)
  {
    for (const bool gpuInWork : {true, false})
    {
      SCOPED_TRACE(rate.Name() + (gpuInWork ? ", the GPU's time in the work" : ", the CPU's time only"));
      const PC::RefreshPeriod period = rate.Period();
      PC::FramePacer pacer{PC::PacerSettings(period)};
      VsyncLoop loop(pacer, period);
      const int64_t framesPerWindow = period.RefreshesToFit(pacer.Settings().FrameWindowLength());
      // A tenth of a refresh on the CPU and one and three tenths on the GPU: every frame at full rate is late
      int64_t slower = 0;
      int64_t faster = 0;
      for (int64_t frame = 0; frame < 6 * framesPerWindow; ++frame)
      {
        const PC::FrameSchedule schedule = loop.GpuFrame(Share(period, 100), Share(period, 1'300), gpuInWork);
        slower += schedule.Change == PC::SwapIntervalChange::Slower ? 1 : 0;
        faster += schedule.Change == PC::SwapIntervalChange::Faster ? 1 : 0;
      }
      if (gpuInWork)
      {
        // The rule knows what a frame needs: it slows down once and stays
        EXPECT_EQ(slower, 1);
        EXPECT_EQ(faster, 0);
        EXPECT_EQ(pacer.SwapInterval(), 2u);
      }
      else
      {
        // The late frames still slow it down, but the work it sees fits a refresh: after a frame window without a late frame it speeds
        // up again, is late again, and so on
        EXPECT_GE(slower, 2);
        EXPECT_GE(faster, 1);
      }
    }
  }
}
