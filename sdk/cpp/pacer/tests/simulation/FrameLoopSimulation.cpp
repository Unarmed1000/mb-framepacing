// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include "FrameLoopSimulation.hpp"
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/tier/TimerPeriodOnlyPacer.hpp>
#include <mb/framepacing/pacer/tier/TimerWaitForPresentPacer.hpp>
#include <mb/framepacing/pacer/tier/VBlankPeriodOnlyPacer.hpp>
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
    int64_t NearestBlank(const int64_t blankTicks, const int64_t periodTicks, const int64_t ticks) noexcept
    {
      const int64_t fromBlank = ticks - blankTicks;
      const int64_t refreshes = (fromBlank >= 0 ? (fromBlank + (periodTicks / 2)) : (fromBlank - (periodTicks / 2))) / periodTicks;
      return blankTicks + (refreshes * periodTicks);
    }

    //! A wait on a timer: it wakes a little after its time, and a time that has passed is no wait
    int64_t WaitUntil(const int64_t now, const int64_t target, SplitMix64& random, const TickRange late) noexcept
    {
      return now < target ? target + random.Draw(late.MinTicks, late.MaxTicks) : now;
    }

    //! A frame's work on the CPU: drawn, and longer for the frames that run long
    int64_t CpuWork(const LoopSettings& settings, const int32_t index, SplitMix64& random)
    {
      const bool isLong = std::find(settings.LongFrames.begin(), settings.LongFrames.end(), index) != settings.LongFrames.end();
      return random.Draw(settings.CpuWork.MinTicks, settings.CpuWork.MaxTicks) + (isLong ? settings.LongFrameCpuTicks : 0);
    }

    //! The display's own refresh period: the one the loop was given, and DisplayPeriodPpm longer
    RefreshPeriod DisplayPeriod(const LoopSettings& settings) noexcept
    {
      const int64_t nominal = (int64_t{1'000'000'000} * settings.RateDenominator) / settings.RateNumerator;
      return settings.DisplayPeriodPpm == 0 ? RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator)
                                            : RefreshPeriod::FromNanoseconds(nominal + ((nominal * settings.DisplayPeriodPpm) / 1'000'000));
    }

    //! The GPU's work on the frames it is done with at now, given to a tier pacer in the frames' order
    template <typename TPacer>
    void ReportGpuWork(TPacer& rPacer, const std::vector<LoopFrame>& frames, std::size_t& rNext, const int64_t now) noexcept
    {
      for (; rNext < frames.size() && frames[rNext].GpuEndTicks <= now; ++rNext)
      {
        const LoopFrame& frame = frames[rNext];
        rPacer.AddGpuWork(GpuWorkReport::Times(frame.FrameId, TickCount64(frame.GpuBeginTicks), TickCount64(frame.GpuEndTicks)));
      }
    }

    //! A moment as a cell: empty for one the frame did not have
    void Moment(std::ostringstream& out, const int64_t ticks)
    {
      out << ',';
      if (ticks != 0)
      {
        out << ticks;
      }
    }
  }

  std::vector<LoopFrame> SimulateLoop(const LoopSettings& settings)
  {
    const RefreshPeriod period = RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator);
    const int64_t periodTicks = period.ToTimeSpan().Ticks();
    PacerSettings pacerSettings(period);
    pacerSettings.SetAutoSwapInterval(settings.AutoSwapInterval);
    FramePacer pacer(pacerSettings);
    DisplayModel display(period, settings.Display);
    SplitMix64 random(settings.Seed);

    std::vector<LoopFrame> frames;
    frames.reserve(static_cast<std::size_t>(std::max(settings.Frames, 0)));
    int64_t now = settings.Display.FirstBlankTicks + settings.LoopTicks;
    // The loop's own state: the time the next frame may start, and the times the last present and frame start were held to
    int64_t nextFrameStartTicks = 0;
    int64_t presentDueTicks = 0;
    int64_t frameStartDueTicks = 0;
    int64_t previousPresentTicks = 0;
    int64_t previousGpuEndTicks = 0;
    int64_t previousGpuWorkTicks = 0;
    for (int32_t index = 0; index < settings.Frames; ++index)
    {
      LoopFrame frame;
      // The first place to wait: before the frame takes anything
      if (nextFrameStartTicks != 0)
      {
        frame.WaitBeginTicks = now;
        frame.WaitTargetTicks = nextFrameStartTicks;
        now = WaitUntil(now, nextFrameStartTicks, random, settings.TimerLate);
        nextFrameStartTicks = 0;
      }
      if (settings.WaitsForPreviousGpuWork)
      {
        now = std::max(now, previousGpuEndTicks);
      }
      now = display.AcquireTicks(now);

      frame.StartTicks = now;
      frame.PendingAtStart = display.Pending(now);
      const int64_t blankTicks = display.BlankTicks(display.BlankAtOrBefore(now));
      const FrameSchedule schedule = pacer.BeginFrame(TickCount64(now));
      const FrameWindowState window = pacer.FrameWindow();
      frame.FrameId = schedule.FrameId;
      frame.SwapInterval = schedule.SwapInterval;
      frame.AnimationTicks = schedule.AnimationTime.Ticks();
      frame.AnimationStepTicks = schedule.AnimationStep.Ticks();
      frame.IntendedDisplayTicks = schedule.IntendedDisplayTime.Ticks();
      frame.NextFrameStartTicks = schedule.NextFrameStartTime.Ticks();
      frame.TargetFrameTimeTicks = static_cast<int64_t>(schedule.TargetFrameTime.Ticks());
      frame.WindowFrames = window.Frames;
      frame.WindowLateFrames = window.LateFrames;

      // The work: the CPU's, then the GPU's once it is free. The pacer is given the GPU's time of the frame before, as the
      // sample only has a frame's GPU time later
      frame.WorkCpuTicks = CpuWork(settings, index, random);
      frame.WorkGpuTicks = previousGpuWorkTicks;
      now += frame.WorkCpuTicks;
      frame.WorkEndTicks = now;
      static_cast<void>(pacer.EndFrame(TickCount64(now), TimeSpan(frame.WorkCpuTicks + frame.WorkGpuTicks)));
      previousGpuWorkTicks = random.Draw(settings.GpuWork.MinTicks, settings.GpuWork.MaxTicks);
      frame.GpuBeginTicks = std::max(now, previousGpuEndTicks);
      frame.GpuEndTicks = frame.GpuBeginTicks + previousGpuWorkTicks;
      previousGpuEndTicks = frame.GpuEndTicks;

      // The second place to wait: before the present. The sample's calculations, which the present itself does not do
      const int64_t lastPresentDueTicks = presentDueTicks;
      const int64_t lastFrameStartDueTicks = frameStartDueTicks;
      presentDueTicks = 0;
      frameStartDueTicks = 0;
      const int64_t frameTimeTicks = frame.NextFrameStartTicks - frame.StartTicks;
      const int64_t phaseTicks = (periodTicks * settings.VBlankPhasePercent) / 100;
      if (schedule.SwapInterval <= 1)
      {
        // The present holds the frame for its one refresh; the loop waits for the pacer's time, counted from the time it held the
        // frame before to
        if (settings.Profile == LoopProfile::RenderLate)
        {
          const bool isOnCount = lastFrameStartDueTicks != 0 && (frame.StartTicks - lastFrameStartDueTicks) <= (frameTimeTicks / 2);
          int64_t dueTicks = (isOnCount ? lastFrameStartDueTicks : frame.StartTicks) + frameTimeTicks;
          if (settings.HasVBlankTimes)
          {
            dueTicks = NearestBlank(blankTicks, periodTicks, dueTicks);
          }
          nextFrameStartTicks = dueTicks;
          frameStartDueTicks = dueTicks;
        }
        else
        {
          const bool isOnCount = lastPresentDueTicks != 0 && (previousPresentTicks - lastPresentDueTicks) <= (frameTimeTicks / 2);
          int64_t dueTicks = (isOnCount ? lastPresentDueTicks : frame.StartTicks) + frameTimeTicks;
          if (settings.HasVBlankTimes)
          {
            dueTicks = NearestBlank(blankTicks, periodTicks, dueTicks - phaseTicks) + phaseTicks;
          }
          frame.PresentWaitBeginTicks = now;
          frame.PresentWaitTargetTicks = dueTicks;
          now = WaitUntil(now, dueTicks, random, settings.TimerLate);
          presentDueTicks = dueTicks;
        }
      }
      else if (settings.HasVBlankTimes)
      {
        // A longer swap interval than the present holds: presented in the refresh before the vertical blank the frame is aimed
        // at, and the next frame starts at that blank
        const int64_t targetTicks = NearestBlank(blankTicks, periodTicks, frame.StartTicks + (periodTicks * schedule.SwapInterval));
        frame.PresentWaitBeginTicks = now;
        frame.PresentWaitTargetTicks = (targetTicks - periodTicks) + phaseTicks;
        now = WaitUntil(now, frame.PresentWaitTargetTicks, random, settings.TimerLate);
        nextFrameStartTicks = targetTicks;
      }
      else
      {
        // The same with a timer only: as late as the present still reaches the refresh, which is a guess
        const int64_t marginTicks = std::min(TimeSpan::TicksPerMillisecond, periodTicks / 8);
        frame.PresentWaitBeginTicks = now;
        frame.PresentWaitTargetTicks = (frame.NextFrameStartTicks - periodTicks) + marginTicks;
        now = WaitUntil(now, frame.PresentWaitTargetTicks, random, settings.TimerLate);
        nextFrameStartTicks = frame.NextFrameStartTicks;
      }

      frame.PresentTicks = now;
      previousPresentTicks = now;
      frame.ShownTicks = display.Present(now, frame.GpuEndTicks);
      frames.push_back(frame);
      now += settings.LoopTicks;
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
    TimerPeriodOnlyPacer pacer(pacerSettings);
    DisplayModel display(DisplayPeriod(settings), settings.Display);
    SplitMix64 random(settings.Seed);

    std::vector<LoopFrame> frames;
    frames.reserve(static_cast<std::size_t>(std::max(settings.Frames, 0)));
    int64_t now = settings.Display.FirstBlankTicks + settings.LoopTicks;
    int64_t previousGpuEndTicks = 0;
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
      const FrameStartPlan startPlan = pacer.PlanFrame(TickCount64(now));
      if (startPlan.WaitsForStartTime())
      {
        frame.WaitBeginTicks = now;
        frame.WaitTargetTicks = startPlan.StartTime.Ticks();
        now = WaitUntil(now, frame.WaitTargetTicks, random, settings.TimerLate);
      }
      // The application's own waits, which the pacer is not asked about
      if (settings.WaitsForPreviousGpuWork)
      {
        now = std::max(now, previousGpuEndTicks);
      }
      now = display.AcquireTicks(now);

      frame.StartTicks = now;
      frame.PendingAtStart = display.Pending(now);
      const FrameSchedule schedule = pacer.BeginFrame(TickCount64(now));
      const FrameWindowState window = pacer.FrameWindow();
      frame.FrameId = schedule.FrameId;
      frame.SwapInterval = schedule.SwapInterval;
      frame.AnimationTicks = schedule.AnimationTime.Ticks();
      frame.AnimationStepTicks = schedule.AnimationStep.Ticks();
      frame.IntendedDisplayTicks = schedule.IntendedDisplayTime.Ticks();
      frame.NextFrameStartTicks = schedule.NextFrameStartTime.Ticks();
      frame.TargetFrameTimeTicks = static_cast<int64_t>(schedule.TargetFrameTime.Ticks());
      frame.WindowFrames = window.Frames;
      frame.WindowLateFrames = window.LateFrames;

      frame.WorkCpuTicks = CpuWork(settings, index, random);
      frame.WorkGpuTicks = pacer.GpuTime().Ticks();
      now += frame.WorkCpuTicks;
      frame.WorkEndTicks = now;
      const PresentPlan presentPlan = pacer.EndFrame(TickCount64(now));
      const int64_t gpuWorkTicks = random.Draw(settings.GpuWork.MinTicks, settings.GpuWork.MaxTicks);
      frame.GpuBeginTicks = std::max(now, previousGpuEndTicks);
      frame.GpuEndTicks = frame.GpuBeginTicks + gpuWorkTicks;
      previousGpuEndTicks = frame.GpuEndTicks;

      // Before the present: the wait the pacer gives
      if (presentPlan.WaitsForPresentTime())
      {
        frame.PresentWaitBeginTicks = now;
        frame.PresentWaitTargetTicks = presentPlan.PresentTime.Ticks();
        now = WaitUntil(now, frame.PresentWaitTargetTicks, random, settings.TimerLate);
      }
      frame.PresentTicks = now;
      frame.ShownTicks = display.Present(now, frame.GpuEndTicks);
      report.FrameId = presentPlan.FrameId;
      report.CallTime = TickCount64(now);
      report.ReturnTime = TickCount64(now);
      frames.push_back(frame);
      now += settings.LoopTicks;
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
    int64_t now = settings.Display.FirstBlankTicks + settings.LoopTicks;
    int64_t previousGpuEndTicks = 0;
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
      FrameStartPlan startPlan = pacer.PlanFrame(TickCount64(now));
      frame.WaitBeginTicks = now;
      if (startPlan.WaitsForPresent())
      {
        // The wait returns a little after the display took the frame, at once when that has passed, or when its time runs out
        const int64_t shownTicks = frames[static_cast<std::size_t>(startPlan.WaitForPresentFrameId) - 1].ShownTicks;
        const int64_t returnTicks = shownTicks + random.Draw(settings.PresentWaitReturn.MinTicks, settings.PresentWaitReturn.MaxTicks);
        const int64_t timeoutTicks = now + startPlan.WaitForPresentTimeout.Ticks();
        PresentWaitReport waitReport;
        waitReport.FrameId = startPlan.WaitForPresentFrameId;
        waitReport.BeginTime = TickCount64(now);
        waitReport.Shown = returnTicks <= timeoutTicks;
        now = std::max(now, std::min(returnTicks, timeoutTicks));
        waitReport.EndTime = TickCount64(now);
        pacer.AddPresentWait(waitReport);
        // Planned again: the wait may have taken long, and the grid may have moved
        startPlan = pacer.PlanFrame(TickCount64(now));
      }
      if (startPlan.WaitsForStartTime())
      {
        frame.WaitTargetTicks = startPlan.StartTime.Ticks();
        now = WaitUntil(now, frame.WaitTargetTicks, random, settings.TimerLate);
      }
      if (settings.WaitsForPreviousGpuWork)
      {
        now = std::max(now, previousGpuEndTicks);
      }
      now = display.AcquireTicks(now);

      frame.StartTicks = now;
      frame.PendingAtStart = display.Pending(now);
      const FrameSchedule schedule = pacer.BeginFrame(TickCount64(now));
      const FrameWindowState window = pacer.FrameWindow();
      frame.FrameId = schedule.FrameId;
      frame.SwapInterval = schedule.SwapInterval;
      frame.AnimationTicks = schedule.AnimationTime.Ticks();
      frame.AnimationStepTicks = schedule.AnimationStep.Ticks();
      frame.IntendedDisplayTicks = schedule.IntendedDisplayTime.Ticks();
      frame.NextFrameStartTicks = schedule.NextFrameStartTime.Ticks();
      frame.TargetFrameTimeTicks = static_cast<int64_t>(schedule.TargetFrameTime.Ticks());
      frame.WindowFrames = window.Frames;
      frame.WindowLateFrames = window.LateFrames;

      frame.WorkCpuTicks = CpuWork(settings, index, random);
      frame.WorkGpuTicks = pacer.GpuTime().Ticks();
      now += frame.WorkCpuTicks;
      frame.WorkEndTicks = now;
      const PresentPlan presentPlan = pacer.EndFrame(TickCount64(now));
      const int64_t gpuWorkTicks = random.Draw(settings.GpuWork.MinTicks, settings.GpuWork.MaxTicks);
      frame.GpuBeginTicks = std::max(now, previousGpuEndTicks);
      frame.GpuEndTicks = frame.GpuBeginTicks + gpuWorkTicks;
      previousGpuEndTicks = frame.GpuEndTicks;

      if (presentPlan.WaitsForPresentTime())
      {
        frame.PresentWaitBeginTicks = now;
        frame.PresentWaitTargetTicks = presentPlan.PresentTime.Ticks();
        now = WaitUntil(now, frame.PresentWaitTargetTicks, random, settings.TimerLate);
      }
      frame.PresentTicks = now;
      frame.ShownTicks = display.Present(now, frame.GpuEndTicks);
      report.FrameId = presentPlan.FrameId;
      report.CallTime = TickCount64(now);
      report.ReturnTime = TickCount64(now);
      frames.push_back(frame);
      now += settings.LoopTicks;
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
    int64_t now = settings.Display.FirstBlankTicks + settings.LoopTicks;
    int64_t previousGpuEndTicks = 0;
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
      reading.VBlankTime = TickCount64(display.BlankTicks(display.BlankAtOrBefore(now)));
      reading.ReadTime = TickCount64(now);
      pacer.AddVBlank(reading);

      // Before the frame takes anything: the wait the pacer gives
      const FrameStartPlan startPlan = pacer.PlanFrame(TickCount64(now));
      if (startPlan.WaitsForStartTime())
      {
        frame.WaitBeginTicks = now;
        frame.WaitTargetTicks = startPlan.StartTime.Ticks();
        now = WaitUntil(now, frame.WaitTargetTicks, random, settings.TimerLate);
      }
      // The application's own waits, which the pacer is not asked about
      if (settings.WaitsForPreviousGpuWork)
      {
        now = std::max(now, previousGpuEndTicks);
      }
      now = display.AcquireTicks(now);

      frame.StartTicks = now;
      frame.PendingAtStart = display.Pending(now);
      const FrameSchedule schedule = pacer.BeginFrame(TickCount64(now));
      const FrameWindowState window = pacer.FrameWindow();
      frame.FrameId = schedule.FrameId;
      frame.SwapInterval = schedule.SwapInterval;
      frame.AnimationTicks = schedule.AnimationTime.Ticks();
      frame.AnimationStepTicks = schedule.AnimationStep.Ticks();
      frame.IntendedDisplayTicks = schedule.IntendedDisplayTime.Ticks();
      frame.NextFrameStartTicks = schedule.NextFrameStartTime.Ticks();
      frame.TargetFrameTimeTicks = static_cast<int64_t>(schedule.TargetFrameTime.Ticks());
      frame.WindowFrames = window.Frames;
      frame.WindowLateFrames = window.LateFrames;

      frame.WorkCpuTicks = CpuWork(settings, index, random);
      frame.WorkGpuTicks = pacer.GpuTime().Ticks();
      now += frame.WorkCpuTicks;
      frame.WorkEndTicks = now;
      const PresentPlan presentPlan = pacer.EndFrame(TickCount64(now));
      const int64_t gpuWorkTicks = random.Draw(settings.GpuWork.MinTicks, settings.GpuWork.MaxTicks);
      frame.GpuBeginTicks = std::max(now, previousGpuEndTicks);
      frame.GpuEndTicks = frame.GpuBeginTicks + gpuWorkTicks;
      previousGpuEndTicks = frame.GpuEndTicks;

      // Before the present: the wait the pacer gives
      if (presentPlan.WaitsForPresentTime())
      {
        frame.PresentWaitBeginTicks = now;
        frame.PresentWaitTargetTicks = presentPlan.PresentTime.Ticks();
        now = WaitUntil(now, frame.PresentWaitTargetTicks, random, settings.TimerLate);
      }
      frame.PresentTicks = now;
      frame.ShownTicks = display.Present(now, frame.GpuEndTicks);
      report.FrameId = presentPlan.FrameId;
      report.CallTime = TickCount64(now);
      report.ReturnTime = TickCount64(now);
      frames.push_back(frame);
      now += settings.LoopTicks;
    }
    return frames;
  }

  std::string ToFrameLog(const std::vector<LoopFrame>& frames, const LoopSettings& settings)
  {
    const int64_t periodTicks = RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToTimeSpan().Ticks();
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
      out << index << ',' << frame.FrameId << ",1," << frame.SwapInterval << ',' << (settings.HasVBlankTimes ? 1 : 0) << ',' << periodTicks << ','
          << frame.TargetFrameTimeTicks << ',' << frame.AnimationTicks << ',' << frame.AnimationStepTicks << ',' << frame.WorkCpuTicks << ','
          << frame.WorkGpuTicks << ',' << frame.WindowFrames << ',' << frame.WindowLateFrames << ',' << frame.PendingAtStart;
      Moment(out, frame.WaitBeginTicks);
      Moment(out, frame.WaitTargetTicks);
      Moment(out, frame.StartTicks);
      Moment(out, frame.WorkEndTicks);
      Moment(out, frame.WorkEndTicks);
      Moment(out, frame.WorkEndTicks);
      Moment(out, frame.GpuBeginTicks);
      Moment(out, frame.GpuEndTicks);
      Moment(out, frame.PresentWaitBeginTicks);
      Moment(out, frame.PresentWaitTargetTicks);
      Moment(out, frame.PresentWaitBeginTicks != 0 ? frame.PresentTicks : 0);
      Moment(out, frame.PresentTicks);
      Moment(out, frame.PresentTicks);
      Moment(out, frame.IntendedDisplayTicks);
      Moment(out, frame.NextFrameStartTicks);
      Moment(out, frame.ShownTicks);
      out << '\n';
    }
    return out.str();
  }
}
