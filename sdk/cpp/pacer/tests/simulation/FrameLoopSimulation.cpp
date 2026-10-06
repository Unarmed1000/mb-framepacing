// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include "FrameLoopSimulation.hpp"
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
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
      frame.WorkCpuTicks = random.Draw(settings.CpuWork.MinTicks, settings.CpuWork.MaxTicks);
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
