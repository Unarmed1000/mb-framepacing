// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include "FrameLoopSimulation.hpp"
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/frame/SystemWaitKind.hpp>
#include <mb/framepacing/pacer/frame/SystemWaitReport.hpp>
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/tier/TimerPeriodOnlyPacer.hpp>
#include <mb/framepacing/pacer/tier/TimerWaitForPresentPacer.hpp>
#include <mb/framepacing/pacer/tier/VBlankPeriodOnlyPacer.hpp>
#include <mb/framepacing/pacer/tier/VBlankWaitForPresentPacer.hpp>
#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <sstream>
#include "DisplayModel.hpp"
#include "SplitMix64.hpp"

namespace MB::FramePacing::Pacer::Simulation
{
  namespace
  {
    //! The vertical blank nearest to a time, counted from one the loop knows
    int64_t NearestBlank(const int64_t blankNanoseconds, const int64_t periodNanoseconds, const int64_t nanoseconds) noexcept
    {
      const int64_t fromBlank = nanoseconds - blankNanoseconds;
      const int64_t refreshes = (fromBlank >= 0 ? (fromBlank + (periodNanoseconds / 2)) : (fromBlank - (periodNanoseconds / 2))) / periodNanoseconds;
      return blankNanoseconds + (refreshes * periodNanoseconds);
    }

    //! A wait on a timer: it wakes a little after its time, and a time that has passed is no wait
    int64_t WaitUntil(const int64_t now, const int64_t target, SplitMix64& random, const NanosecondRange late) noexcept
    {
      return now < target ? target + random.Draw(late.MinNanoseconds, late.MaxNanoseconds) : now;
    }

    //! A frame's work on the CPU: drawn, and longer for the frames that run long
    int64_t CpuWork(const LoopSettings& settings, const int32_t index, SplitMix64& random)
    {
      const bool isLong = std::find(settings.LongFrames.begin(), settings.LongFrames.end(), index) != settings.LongFrames.end();
      return random.Draw(settings.CpuWork.MinNanoseconds, settings.CpuWork.MaxNanoseconds) + (isLong ? settings.LongFrameCpuNanoseconds : 0);
    }

    //! The display's own refresh period: the one the loop was given, and DisplayPeriodPpm longer
    RefreshPeriod DisplayPeriod(const LoopSettings& settings) noexcept
    {
      const int64_t nominal = (int64_t{1'000'000'000} * settings.RateDenominator) / settings.RateNumerator;
      return settings.DisplayPeriodPpm == 0
               ? RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator)
               : RefreshPeriod::FromNanosecondTimeSpan(NanosecondTimeSpan(nominal + ((nominal * settings.DisplayPeriodPpm) / 1'000'000)));
    }

    //! What the window system says of the display at now: its last vertical blank, as exact as the loop's source is
    VBlankReading ReadVBlank(const DisplayModel& display, const int64_t now, SplitMix64& random, const LoopSettings& settings) noexcept
    {
      const NanosecondRange error = settings.VBlankReadingError;
      const int64_t off =
        error.MinNanoseconds == error.MaxNanoseconds ? error.MinNanoseconds : random.Draw(error.MinNanoseconds, error.MaxNanoseconds);
      VBlankReading reading;
      reading.VBlankTime = NanosecondTickCount(display.BlankNanoseconds(display.BlankAtOrBefore(now)) + off);
      reading.ReadTime = NanosecondTickCount(now);
      return reading;
    }

    //! The GPU's work on the frames it is done with at now, given to a tier pacer in the frames' order
    template <typename TPacer>
    void ReportGpuWork(TPacer& rPacer, const std::vector<LoopFrame>& frames, std::size_t& rNext, const int64_t now) noexcept
    {
      for (; rNext < frames.size() && frames[rNext].GpuEndNanoseconds <= now; ++rNext)
      {
        const LoopFrame& frame = frames[rNext];
        rPacer.AddGpuWork(
          GpuWorkReport::Times(frame.FrameId, NanosecondTickCount(frame.GpuBeginNanoseconds), NanosecondTickCount(frame.GpuEndNanoseconds)));
      }
    }

    //! The frame log is the first integration's, which counts in ticks of 100 ns
    constexpr int64_t NanosecondsPerTick = NanosecondTimeSpan::NanosecondsPerTick;

    //! A time of the loop in the log's ticks: the nearest one
    constexpr int64_t Ticks(const int64_t nanoseconds) noexcept
    {
      return (nanoseconds + (NanosecondsPerTick / 2)) / NanosecondsPerTick;
    }

    //! A moment as a cell, in ticks: empty for one the frame did not have
    void Moment(std::ostringstream& out, const int64_t nanoseconds)
    {
      out << ',';
      if (nanoseconds != 0)
      {
        out << Ticks(nanoseconds);
      }
    }
  }

  std::vector<LoopFrame> SimulateLoop(const LoopSettings& settings)
  {
    const RefreshPeriod period = RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator);
    const int64_t periodNanoseconds = period.ToNanosecondTimeSpan().Nanoseconds();
    PacerSettings pacerSettings(period);
    pacerSettings.SetAutoSwapInterval(settings.AutoSwapInterval);
    FramePacer pacer(pacerSettings);
    DisplayModel display(period, settings.Display);
    SplitMix64 random(settings.Seed);

    std::vector<LoopFrame> frames;
    frames.reserve(static_cast<std::size_t>(std::max(settings.Frames, 0)));
    int64_t now = settings.Display.FirstBlankNanoseconds + settings.LoopNanoseconds;
    // The loop's own state: the time the next frame may start, and the times the last present and frame start were held to
    int64_t nextFrameStartNanoseconds = 0;
    int64_t presentDueNanoseconds = 0;
    int64_t frameStartDueNanoseconds = 0;
    int64_t previousPresentNanoseconds = 0;
    int64_t previousGpuEndNanoseconds = 0;
    int64_t previousGpuWorkNanoseconds = 0;
    for (int32_t index = 0; index < settings.Frames; ++index)
    {
      LoopFrame frame;
      // The first place to wait: before the frame takes anything
      if (nextFrameStartNanoseconds != 0)
      {
        frame.WaitBeginNanoseconds = now;
        frame.WaitTargetNanoseconds = nextFrameStartNanoseconds;
        now = WaitUntil(now, nextFrameStartNanoseconds, random, settings.TimerLate);
        nextFrameStartNanoseconds = 0;
      }
      if (settings.WaitsForPreviousGpuWork)
      {
        now = std::max(now, previousGpuEndNanoseconds);
      }
      now = display.AcquireNanoseconds(now);

      frame.StartNanoseconds = now;
      frame.PendingAtStart = display.Pending(now);
      const int64_t blankNanoseconds = display.BlankNanoseconds(display.BlankAtOrBefore(now));
      const FrameSchedule schedule = pacer.BeginFrame(NanosecondTickCount(now));
      const FrameWindowState window = pacer.FrameWindow();
      frame.FrameId = schedule.FrameId;
      frame.SwapInterval = schedule.SwapInterval;
      frame.AnimationNanoseconds = schedule.AnimationTime.Nanoseconds();
      frame.AnimationStepNanoseconds = schedule.AnimationStep.Nanoseconds();
      frame.IntendedDisplayNanoseconds = schedule.IntendedDisplayTime.Nanoseconds();
      frame.NextFrameStartNanoseconds = schedule.NextFrameStartTime.Nanoseconds();
      frame.TargetFrameTimeNanoseconds = schedule.TargetFrameTime.Nanoseconds();
      frame.WindowFrames = window.Frames;
      frame.WindowLateFrames = window.LateFrames;

      // The work: the CPU's, then the GPU's once it is free. The pacer is given the GPU's time of the frame before, as the
      // sample only has a frame's GPU time later
      frame.WorkCpuNanoseconds = CpuWork(settings, index, random);
      frame.WorkGpuNanoseconds = previousGpuWorkNanoseconds;
      now += frame.WorkCpuNanoseconds;
      frame.WorkEndNanoseconds = now;
      static_cast<void>(pacer.EndFrame(NanosecondTickCount(now), NanosecondTimeSpan(frame.WorkCpuNanoseconds + frame.WorkGpuNanoseconds)));
      previousGpuWorkNanoseconds = random.Draw(settings.GpuWork.MinNanoseconds, settings.GpuWork.MaxNanoseconds);
      frame.GpuBeginNanoseconds = std::max(now, previousGpuEndNanoseconds);
      frame.GpuEndNanoseconds = frame.GpuBeginNanoseconds + previousGpuWorkNanoseconds;
      previousGpuEndNanoseconds = frame.GpuEndNanoseconds;

      // The second place to wait: before the present. The sample's calculations, which the present itself does not do
      const int64_t lastPresentDueNanoseconds = presentDueNanoseconds;
      const int64_t lastFrameStartDueNanoseconds = frameStartDueNanoseconds;
      presentDueNanoseconds = 0;
      frameStartDueNanoseconds = 0;
      const int64_t frameTimeNanoseconds = frame.NextFrameStartNanoseconds - frame.StartNanoseconds;
      const int64_t phaseNanoseconds = (periodNanoseconds * settings.VBlankPhasePercent) / 100;
      if (schedule.SwapInterval <= 1)
      {
        // The present holds the frame for its one refresh; the loop waits for the pacer's time, counted from the time it held the
        // frame before to
        if (settings.Profile == LoopProfile::RenderLate)
        {
          const bool isOnCount =
            lastFrameStartDueNanoseconds != 0 && (frame.StartNanoseconds - lastFrameStartDueNanoseconds) <= (frameTimeNanoseconds / 2);
          int64_t dueNanoseconds = (isOnCount ? lastFrameStartDueNanoseconds : frame.StartNanoseconds) + frameTimeNanoseconds;
          if (settings.HasVBlankTimes)
          {
            dueNanoseconds = NearestBlank(blankNanoseconds, periodNanoseconds, dueNanoseconds);
          }
          nextFrameStartNanoseconds = dueNanoseconds;
          frameStartDueNanoseconds = dueNanoseconds;
        }
        else
        {
          const bool isOnCount =
            lastPresentDueNanoseconds != 0 && (previousPresentNanoseconds - lastPresentDueNanoseconds) <= (frameTimeNanoseconds / 2);
          int64_t dueNanoseconds = (isOnCount ? lastPresentDueNanoseconds : frame.StartNanoseconds) + frameTimeNanoseconds;
          if (settings.HasVBlankTimes)
          {
            dueNanoseconds = NearestBlank(blankNanoseconds, periodNanoseconds, dueNanoseconds - phaseNanoseconds) + phaseNanoseconds;
          }
          frame.PresentWaitBeginNanoseconds = now;
          frame.PresentWaitTargetNanoseconds = dueNanoseconds;
          now = WaitUntil(now, dueNanoseconds, random, settings.TimerLate);
          presentDueNanoseconds = dueNanoseconds;
        }
      }
      else if (settings.HasVBlankTimes)
      {
        // A longer swap interval than the present holds: presented in the refresh before the vertical blank the frame is aimed
        // at, and the next frame starts at that blank
        const int64_t targetNanoseconds =
          NearestBlank(blankNanoseconds, periodNanoseconds, frame.StartNanoseconds + (periodNanoseconds * schedule.SwapInterval));
        frame.PresentWaitBeginNanoseconds = now;
        frame.PresentWaitTargetNanoseconds = (targetNanoseconds - periodNanoseconds) + phaseNanoseconds;
        now = WaitUntil(now, frame.PresentWaitTargetNanoseconds, random, settings.TimerLate);
        nextFrameStartNanoseconds = targetNanoseconds;
      }
      else
      {
        // The same with a timer only: as late as the present still reaches the refresh, which is a guess
        const int64_t marginNanoseconds = std::min(NanosecondTimeSpan::NanosecondsPerMillisecond, periodNanoseconds / 8);
        frame.PresentWaitBeginNanoseconds = now;
        frame.PresentWaitTargetNanoseconds = (frame.NextFrameStartNanoseconds - periodNanoseconds) + marginNanoseconds;
        now = WaitUntil(now, frame.PresentWaitTargetNanoseconds, random, settings.TimerLate);
        nextFrameStartNanoseconds = frame.NextFrameStartNanoseconds;
      }

      frame.PresentNanoseconds = now;
      previousPresentNanoseconds = now;
      frame.ShownNanoseconds = display.Present(now, frame.GpuEndNanoseconds);
      frames.push_back(frame);
      now += settings.LoopNanoseconds;
    }
    return frames;
  }

  std::vector<LoopFrame> SimulateTimerPeriodOnlyLoop(const LoopSettings& settings)
  {
    const RefreshPeriod period = RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator);
    PacerSettings pacerSettings(period);
    pacerSettings.SetAutoSwapInterval(settings.AutoSwapInterval);
    pacerSettings.SetAim(settings.Aim);
    pacerSettings.SetPreferredSwapInterval(settings.PreferredSwapInterval);
    pacerSettings.SetWaitingPresents(settings.WaitingPresents);
    pacerSettings.SetMaxFramesInFlight(settings.MaxFramesInFlight);
    pacerSettings.SetStartupPauseRefreshes(settings.StartupPauseRefreshes);
    pacerSettings.SetSystemHoldsLoop(settings.SystemHoldsLoop);
    if (settings.SystemHoldsLoop && settings.Display.Images > 0)
    {
      pacerSettings.SetSwapChainImages(static_cast<uint32_t>(settings.Display.Images));
    }
    TimerPeriodOnlyPacer pacer(pacerSettings);
    DisplayModel display(DisplayPeriod(settings), settings.Display);
    SplitMix64 random(settings.Seed);

    std::vector<LoopFrame> frames;
    frames.reserve(static_cast<std::size_t>(std::max(settings.Frames, 0)));
    int64_t now = settings.Display.FirstBlankNanoseconds + settings.LoopNanoseconds;
    int64_t previousGpuEndNanoseconds = 0;
    std::size_t nextGpuReport = 0;
    PresentReport report;
    for (int32_t index = 0; index < settings.Frames; ++index)
    {
      LoopFrame frame;
      if (index > 0)
      {
        pacer.AddPresent(report);
      }
      if (settings.ReportsGpuWork)
      {
        ReportGpuWork(pacer, frames, nextGpuReport, now);
      }
      // Before the frame takes anything: the wait the pacer gives
      const FrameStartPlan startPlan = pacer.PlanFrame(NanosecondTickCount(now));
      if (startPlan.WaitsForStartTime())
      {
        frame.WaitBeginNanoseconds = now;
        frame.WaitTargetNanoseconds = startPlan.StartTime.Nanoseconds();
        now = WaitUntil(now, frame.WaitTargetNanoseconds, random, settings.TimerLate);
      }
      // The application's own waits, which the pacer did not ask for: it is told of them where the loop says so
      const int64_t frameSlotWaitBegin = now;
      if (settings.WaitsForPreviousGpuWork)
      {
        now = std::max(now, previousGpuEndNanoseconds);
      }
      const int64_t acquireBegin = now;
      now = display.AcquireNanoseconds(now);
      if (settings.SystemHoldsLoop)
      {
        pacer.AddSystemWait({SystemWaitKind::FrameSlot, NanosecondTickCount(frameSlotWaitBegin), NanosecondTickCount(acquireBegin)});
        pacer.AddSystemWait({SystemWaitKind::Acquire, NanosecondTickCount(acquireBegin), NanosecondTickCount(now)});
      }

      frame.StartNanoseconds = now;
      frame.PendingAtStart = display.Pending(now);
      const FrameSchedule schedule = pacer.BeginFrame(NanosecondTickCount(now));
      const FrameWindowState window = pacer.FrameWindow();
      frame.FrameId = schedule.FrameId;
      frame.SwapInterval = schedule.SwapInterval;
      frame.AnimationNanoseconds = schedule.AnimationTime.Nanoseconds();
      frame.AnimationStepNanoseconds = schedule.AnimationStep.Nanoseconds();
      frame.IntendedDisplayNanoseconds = schedule.IntendedDisplayTime.Nanoseconds();
      frame.NextFrameStartNanoseconds = schedule.NextFrameStartTime.Nanoseconds();
      frame.TargetFrameTimeNanoseconds = schedule.TargetFrameTime.Nanoseconds();
      frame.WindowFrames = window.Frames;
      frame.WindowLateFrames = window.LateFrames;

      frame.WorkCpuNanoseconds = CpuWork(settings, index, random);
      frame.WorkGpuNanoseconds = pacer.GpuTime().Nanoseconds();
      now += frame.WorkCpuNanoseconds;
      frame.WorkEndNanoseconds = now;
      const PresentPlan presentPlan = pacer.EndFrame(NanosecondTickCount(now));
      const int64_t gpuWorkNanoseconds = random.Draw(settings.GpuWork.MinNanoseconds, settings.GpuWork.MaxNanoseconds);
      frame.GpuBeginNanoseconds = std::max(now, previousGpuEndNanoseconds);
      frame.GpuEndNanoseconds = frame.GpuBeginNanoseconds + gpuWorkNanoseconds;
      previousGpuEndNanoseconds = frame.GpuEndNanoseconds;

      // Before the present: the wait the pacer gives
      if (presentPlan.WaitsForPresentTime())
      {
        frame.PresentWaitBeginNanoseconds = now;
        frame.PresentWaitTargetNanoseconds = presentPlan.PresentTime.Nanoseconds();
        now = WaitUntil(now, frame.PresentWaitTargetNanoseconds, random, settings.TimerLate);
      }
      frame.PresentNanoseconds = now;
      frame.ShownNanoseconds = display.Present(now, frame.GpuEndNanoseconds);
      report.FrameId = presentPlan.FrameId;
      report.CallTime = NanosecondTickCount(now);
      report.ReturnTime = NanosecondTickCount(now);
      frames.push_back(frame);
      now += settings.LoopNanoseconds;
    }
    return frames;
  }

  std::vector<LoopFrame> SimulateTimerWaitForPresentLoop(const LoopSettings& settings)
  {
    const RefreshPeriod period = RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator);
    PacerSettings pacerSettings(period);
    pacerSettings.SetAutoSwapInterval(settings.AutoSwapInterval);
    pacerSettings.SetAim(settings.Aim);
    pacerSettings.SetPreferredSwapInterval(settings.PreferredSwapInterval);
    pacerSettings.SetWaitingPresents(settings.WaitingPresents);
    pacerSettings.SetMaxFramesInFlight(settings.MaxFramesInFlight);
    TimerWaitForPresentPacer pacer(pacerSettings);
    DisplayModel display(DisplayPeriod(settings), settings.Display);
    SplitMix64 random(settings.Seed);

    std::vector<LoopFrame> frames;
    frames.reserve(static_cast<std::size_t>(std::max(settings.Frames, 0)));
    int64_t now = settings.Display.FirstBlankNanoseconds + settings.LoopNanoseconds;
    int64_t previousGpuEndNanoseconds = 0;
    std::size_t nextGpuReport = 0;
    PresentReport report;
    for (int32_t index = 0; index < settings.Frames; ++index)
    {
      LoopFrame frame;
      if (index > 0)
      {
        pacer.AddPresent(report);
      }
      if (settings.ReportsGpuWork)
      {
        ReportGpuWork(pacer, frames, nextGpuReport, now);
      }
      // Before the frame takes anything: the waits the pacer gives, the present first
      FrameStartPlan startPlan = pacer.PlanFrame(NanosecondTickCount(now));
      frame.WaitBeginNanoseconds = now;
      if (startPlan.WaitsForPresent())
      {
        // The wait returns a little after the display took the frame, at once when that has passed, or when its time runs out
        const int64_t shownNanoseconds = frames[static_cast<std::size_t>(startPlan.WaitForPresentFrameId) - 1].ShownNanoseconds;
        const int64_t returnNanoseconds =
          shownNanoseconds + random.Draw(settings.PresentWaitReturn.MinNanoseconds, settings.PresentWaitReturn.MaxNanoseconds);
        const int64_t timeoutNanoseconds = now + startPlan.WaitForPresentTimeout.Nanoseconds();
        PresentWaitReport waitReport;
        waitReport.FrameId = startPlan.WaitForPresentFrameId;
        waitReport.BeginTime = NanosecondTickCount(now);
        waitReport.Shown = returnNanoseconds <= timeoutNanoseconds;
        now = std::max(now, std::min(returnNanoseconds, timeoutNanoseconds));
        waitReport.EndTime = NanosecondTickCount(now);
        pacer.AddPresentWait(waitReport);
        // Planned again: the wait may have taken long, and the grid may have moved
        startPlan = pacer.PlanFrame(NanosecondTickCount(now));
      }
      if (startPlan.WaitsForStartTime())
      {
        frame.WaitTargetNanoseconds = startPlan.StartTime.Nanoseconds();
        now = WaitUntil(now, frame.WaitTargetNanoseconds, random, settings.TimerLate);
      }
      if (settings.WaitsForPreviousGpuWork)
      {
        now = std::max(now, previousGpuEndNanoseconds);
      }
      now = display.AcquireNanoseconds(now);

      frame.StartNanoseconds = now;
      frame.PendingAtStart = display.Pending(now);
      const FrameSchedule schedule = pacer.BeginFrame(NanosecondTickCount(now));
      const FrameWindowState window = pacer.FrameWindow();
      frame.FrameId = schedule.FrameId;
      frame.SwapInterval = schedule.SwapInterval;
      frame.AnimationNanoseconds = schedule.AnimationTime.Nanoseconds();
      frame.AnimationStepNanoseconds = schedule.AnimationStep.Nanoseconds();
      frame.IntendedDisplayNanoseconds = schedule.IntendedDisplayTime.Nanoseconds();
      frame.NextFrameStartNanoseconds = schedule.NextFrameStartTime.Nanoseconds();
      frame.TargetFrameTimeNanoseconds = schedule.TargetFrameTime.Nanoseconds();
      frame.WindowFrames = window.Frames;
      frame.WindowLateFrames = window.LateFrames;

      frame.WorkCpuNanoseconds = CpuWork(settings, index, random);
      frame.WorkGpuNanoseconds = pacer.GpuTime().Nanoseconds();
      now += frame.WorkCpuNanoseconds;
      frame.WorkEndNanoseconds = now;
      const PresentPlan presentPlan = pacer.EndFrame(NanosecondTickCount(now));
      const int64_t gpuWorkNanoseconds = random.Draw(settings.GpuWork.MinNanoseconds, settings.GpuWork.MaxNanoseconds);
      frame.GpuBeginNanoseconds = std::max(now, previousGpuEndNanoseconds);
      frame.GpuEndNanoseconds = frame.GpuBeginNanoseconds + gpuWorkNanoseconds;
      previousGpuEndNanoseconds = frame.GpuEndNanoseconds;

      if (presentPlan.WaitsForPresentTime())
      {
        frame.PresentWaitBeginNanoseconds = now;
        frame.PresentWaitTargetNanoseconds = presentPlan.PresentTime.Nanoseconds();
        now = WaitUntil(now, frame.PresentWaitTargetNanoseconds, random, settings.TimerLate);
      }
      frame.PresentNanoseconds = now;
      frame.ShownNanoseconds = display.Present(now, frame.GpuEndNanoseconds);
      report.FrameId = presentPlan.FrameId;
      report.CallTime = NanosecondTickCount(now);
      report.ReturnTime = NanosecondTickCount(now);
      frames.push_back(frame);
      now += settings.LoopNanoseconds;
    }
    return frames;
  }

  std::vector<LoopFrame> SimulateVBlankPeriodOnlyLoop(const LoopSettings& settings)
  {
    const RefreshPeriod period = RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator);
    PacerSettings pacerSettings(period);
    pacerSettings.SetAutoSwapInterval(settings.AutoSwapInterval);
    pacerSettings.SetAim(settings.Aim);
    pacerSettings.SetPreferredSwapInterval(settings.PreferredSwapInterval);
    pacerSettings.SetWaitingPresents(settings.WaitingPresents);
    pacerSettings.SetMaxFramesInFlight(settings.MaxFramesInFlight);
    pacerSettings.SetStartupPauseRefreshes(settings.StartupPauseRefreshes);
    pacerSettings.SetReadyPlacePercent(settings.ReadyPlacePercent);
    VBlankPeriodOnlyPacer pacer(pacerSettings);
    DisplayModel display(DisplayPeriod(settings), settings.Display);
    SplitMix64 random(settings.Seed);

    std::vector<LoopFrame> frames;
    frames.reserve(static_cast<std::size_t>(std::max(settings.Frames, 0)));
    int64_t now = settings.Display.FirstBlankNanoseconds + settings.LoopNanoseconds;
    int64_t previousGpuEndNanoseconds = 0;
    std::size_t nextGpuReport = 0;
    PresentReport report;
    for (int32_t index = 0; index < settings.Frames; ++index)
    {
      LoopFrame frame;
      if (index > 0)
      {
        pacer.AddPresent(report);
      }
      if (settings.ReportsGpuWork)
      {
        ReportGpuWork(pacer, frames, nextGpuReport, now);
      }
      // What the window system says of the display: its last vertical blank
      VBlankReading reading;
      reading = ReadVBlank(display, now, random, settings);
      pacer.AddVBlank(reading);

      // Before the frame takes anything: the wait the pacer gives
      const FrameStartPlan startPlan = pacer.PlanFrame(NanosecondTickCount(now));
      if (startPlan.WaitsForStartTime())
      {
        frame.WaitBeginNanoseconds = now;
        frame.WaitTargetNanoseconds = startPlan.StartTime.Nanoseconds();
        now = WaitUntil(now, frame.WaitTargetNanoseconds, random, settings.TimerLate);
      }
      // The application's own waits, which the pacer is not asked about
      if (settings.WaitsForPreviousGpuWork)
      {
        now = std::max(now, previousGpuEndNanoseconds);
      }
      now = display.AcquireNanoseconds(now);

      frame.StartNanoseconds = now;
      frame.PendingAtStart = display.Pending(now);
      const FrameSchedule schedule = pacer.BeginFrame(NanosecondTickCount(now));
      const FrameWindowState window = pacer.FrameWindow();
      frame.FrameId = schedule.FrameId;
      frame.SwapInterval = schedule.SwapInterval;
      frame.AnimationNanoseconds = schedule.AnimationTime.Nanoseconds();
      frame.AnimationStepNanoseconds = schedule.AnimationStep.Nanoseconds();
      frame.IntendedDisplayNanoseconds = schedule.IntendedDisplayTime.Nanoseconds();
      frame.NextFrameStartNanoseconds = schedule.NextFrameStartTime.Nanoseconds();
      frame.TargetFrameTimeNanoseconds = schedule.TargetFrameTime.Nanoseconds();
      frame.WindowFrames = window.Frames;
      frame.WindowLateFrames = window.LateFrames;

      frame.WorkCpuNanoseconds = CpuWork(settings, index, random);
      frame.WorkGpuNanoseconds = pacer.GpuTime().Nanoseconds();
      now += frame.WorkCpuNanoseconds;
      frame.WorkEndNanoseconds = now;
      const PresentPlan presentPlan = pacer.EndFrame(NanosecondTickCount(now));
      const int64_t gpuWorkNanoseconds = random.Draw(settings.GpuWork.MinNanoseconds, settings.GpuWork.MaxNanoseconds);
      frame.GpuBeginNanoseconds = std::max(now, previousGpuEndNanoseconds);
      frame.GpuEndNanoseconds = frame.GpuBeginNanoseconds + gpuWorkNanoseconds;
      previousGpuEndNanoseconds = frame.GpuEndNanoseconds;

      // Before the present: the wait the pacer gives
      if (presentPlan.WaitsForPresentTime())
      {
        frame.PresentWaitBeginNanoseconds = now;
        frame.PresentWaitTargetNanoseconds = presentPlan.PresentTime.Nanoseconds();
        now = WaitUntil(now, frame.PresentWaitTargetNanoseconds, random, settings.TimerLate);
      }
      frame.PresentNanoseconds = now;
      frame.ShownNanoseconds = display.Present(now, frame.GpuEndNanoseconds);
      report.FrameId = presentPlan.FrameId;
      report.CallTime = NanosecondTickCount(now);
      report.ReturnTime = NanosecondTickCount(now);
      frames.push_back(frame);
      now += settings.LoopNanoseconds;
    }
    return frames;
  }

  std::vector<LoopFrame> SimulateVBlankWaitForPresentLoop(const LoopSettings& settings)
  {
    const RefreshPeriod period = RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator);
    PacerSettings pacerSettings(period);
    pacerSettings.SetAutoSwapInterval(settings.AutoSwapInterval);
    pacerSettings.SetAim(settings.Aim);
    pacerSettings.SetPreferredSwapInterval(settings.PreferredSwapInterval);
    pacerSettings.SetWaitingPresents(settings.WaitingPresents);
    pacerSettings.SetMaxFramesInFlight(settings.MaxFramesInFlight);
    pacerSettings.SetStartupPauseRefreshes(settings.StartupPauseRefreshes);
    pacerSettings.SetReadyPlacePercent(settings.ReadyPlacePercent);
    VBlankWaitForPresentPacer pacer(pacerSettings);
    DisplayModel display(DisplayPeriod(settings), settings.Display);
    SplitMix64 random(settings.Seed);

    std::vector<LoopFrame> frames;
    frames.reserve(static_cast<std::size_t>(std::max(settings.Frames, 0)));
    int64_t now = settings.Display.FirstBlankNanoseconds + settings.LoopNanoseconds;
    int64_t previousGpuEndNanoseconds = 0;
    std::size_t nextGpuReport = 0;
    PresentReport report;
    for (int32_t index = 0; index < settings.Frames; ++index)
    {
      LoopFrame frame;
      if (index > 0)
      {
        pacer.AddPresent(report);
      }
      if (settings.ReportsGpuWork)
      {
        ReportGpuWork(pacer, frames, nextGpuReport, now);
      }
      // What the window system says of the display: its last vertical blank
      VBlankReading reading;
      reading = ReadVBlank(display, now, random, settings);
      pacer.AddVBlank(reading);

      // Before the frame takes anything: the waits the pacer gives, the present first
      FrameStartPlan startPlan = pacer.PlanFrame(NanosecondTickCount(now));
      frame.WaitBeginNanoseconds = now;
      if (startPlan.WaitsForPresent())
      {
        // The wait returns a little after the display took the frame, at once when that has passed, or when its time runs out
        const int64_t shownNanoseconds = frames[static_cast<std::size_t>(startPlan.WaitForPresentFrameId) - 1].ShownNanoseconds;
        const int64_t returnNanoseconds =
          shownNanoseconds + random.Draw(settings.PresentWaitReturn.MinNanoseconds, settings.PresentWaitReturn.MaxNanoseconds);
        const int64_t timeoutNanoseconds = now + startPlan.WaitForPresentTimeout.Nanoseconds();
        PresentWaitReport waitReport;
        waitReport.FrameId = startPlan.WaitForPresentFrameId;
        waitReport.BeginTime = NanosecondTickCount(now);
        waitReport.Shown = returnNanoseconds <= timeoutNanoseconds;
        now = std::max(now, std::min(returnNanoseconds, timeoutNanoseconds));
        waitReport.EndTime = NanosecondTickCount(now);
        pacer.AddPresentWait(waitReport);
        // The vertical blank the window system has by now, and the frame planned again
        reading = ReadVBlank(display, now, random, settings);
        pacer.AddVBlank(reading);
        startPlan = pacer.PlanFrame(NanosecondTickCount(now));
      }
      if (startPlan.WaitsForStartTime())
      {
        frame.WaitTargetNanoseconds = startPlan.StartTime.Nanoseconds();
        now = WaitUntil(now, frame.WaitTargetNanoseconds, random, settings.TimerLate);
      }
      // The application's own waits, which the pacer is not asked about
      if (settings.WaitsForPreviousGpuWork)
      {
        now = std::max(now, previousGpuEndNanoseconds);
      }
      now = display.AcquireNanoseconds(now);

      frame.StartNanoseconds = now;
      frame.PendingAtStart = display.Pending(now);
      const FrameSchedule schedule = pacer.BeginFrame(NanosecondTickCount(now));
      const FrameWindowState window = pacer.FrameWindow();
      frame.FrameId = schedule.FrameId;
      frame.SwapInterval = schedule.SwapInterval;
      frame.AnimationNanoseconds = schedule.AnimationTime.Nanoseconds();
      frame.AnimationStepNanoseconds = schedule.AnimationStep.Nanoseconds();
      frame.IntendedDisplayNanoseconds = schedule.IntendedDisplayTime.Nanoseconds();
      frame.NextFrameStartNanoseconds = schedule.NextFrameStartTime.Nanoseconds();
      frame.TargetFrameTimeNanoseconds = schedule.TargetFrameTime.Nanoseconds();
      frame.WindowFrames = window.Frames;
      frame.WindowLateFrames = window.LateFrames;

      frame.WorkCpuNanoseconds = CpuWork(settings, index, random);
      frame.WorkGpuNanoseconds = pacer.GpuTime().Nanoseconds();
      now += frame.WorkCpuNanoseconds;
      frame.WorkEndNanoseconds = now;
      const PresentPlan presentPlan = pacer.EndFrame(NanosecondTickCount(now));
      const int64_t gpuWorkNanoseconds = random.Draw(settings.GpuWork.MinNanoseconds, settings.GpuWork.MaxNanoseconds);
      frame.GpuBeginNanoseconds = std::max(now, previousGpuEndNanoseconds);
      frame.GpuEndNanoseconds = frame.GpuBeginNanoseconds + gpuWorkNanoseconds;
      previousGpuEndNanoseconds = frame.GpuEndNanoseconds;

      // Before the present: the wait the pacer gives
      if (presentPlan.WaitsForPresentTime())
      {
        frame.PresentWaitBeginNanoseconds = now;
        frame.PresentWaitTargetNanoseconds = presentPlan.PresentTime.Nanoseconds();
        now = WaitUntil(now, frame.PresentWaitTargetNanoseconds, random, settings.TimerLate);
      }
      frame.PresentNanoseconds = now;
      frame.ShownNanoseconds = display.Present(now, frame.GpuEndNanoseconds);
      report.FrameId = presentPlan.FrameId;
      report.CallTime = NanosecondTickCount(now);
      report.ReturnTime = NanosecondTickCount(now);
      frames.push_back(frame);
      now += settings.LoopNanoseconds;
    }
    return frames;
  }

  std::string ToFrameLog(const std::vector<LoopFrame>& frames, const LoopSettings& settings)
  {
    const int64_t periodNanoseconds = RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToNanosecondTimeSpan().Nanoseconds();
    std::ostringstream out;
    out << "frameIndex,pacerFrameId,pacerOn,swapInterval,holdMethod,displayRefreshPeriodTicks,targetFrameTimeTicks,animationTimeTicks,"
           "animationStepTicks,"
           "workCpuTicks,workGpuTicks,pacerWindowFrames,pacerWindowLateFrames,pendingAtStart,frameWaitStartTicks,frameWaitTargetTicks,"
           "frameStartTicks,endFrameTicks,submitCallTicks,submitReturnTicks,gpuWorkBeginTicks,gpuWorkEndTicks,presentWaitBeginTicks,"
           "presentWaitTargetTicks,presentWaitEndTicks,presentCallTicks,presentReturnTicks,intendedDisplayTicks,nextFrameStartTicks,"
           "firstPixelOutTicks\n";
    for (std::size_t index = 0; index < frames.size(); ++index)
    {
      const LoopFrame& frame = frames[index];
      out << index << ',' << frame.FrameId << ",1," << frame.SwapInterval << ',' << (settings.HasVBlankTimes ? 1 : 0) << ','
          << Ticks(periodNanoseconds) << ',' << Ticks(frame.TargetFrameTimeNanoseconds) << ',' << Ticks(frame.AnimationNanoseconds) << ','
          << Ticks(frame.AnimationStepNanoseconds) << ',' << Ticks(frame.WorkCpuNanoseconds) << ',' << Ticks(frame.WorkGpuNanoseconds) << ','
          << frame.WindowFrames << ',' << frame.WindowLateFrames << ',' << frame.PendingAtStart;
      Moment(out, frame.WaitBeginNanoseconds);
      Moment(out, frame.WaitTargetNanoseconds);
      Moment(out, frame.StartNanoseconds);
      Moment(out, frame.WorkEndNanoseconds);
      Moment(out, frame.WorkEndNanoseconds);
      Moment(out, frame.WorkEndNanoseconds);
      Moment(out, frame.GpuBeginNanoseconds);
      Moment(out, frame.GpuEndNanoseconds);
      Moment(out, frame.PresentWaitBeginNanoseconds);
      Moment(out, frame.PresentWaitTargetNanoseconds);
      Moment(out, frame.PresentWaitBeginNanoseconds != 0 ? frame.PresentNanoseconds : 0);
      Moment(out, frame.PresentNanoseconds);
      Moment(out, frame.PresentNanoseconds);
      Moment(out, frame.IntendedDisplayNanoseconds);
      Moment(out, frame.NextFrameStartNanoseconds);
      Moment(out, frame.ShownNanoseconds);
      out << '\n';
    }
    return out.str();
  }
}
