#ifndef MB_FRAMEPACING_PACER_TIER_FREERUNLOOP_HPP
#define MB_FRAMEPACING_PACER_TIER_FREERUNLOOP_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/RefreshTime.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/hold/GpuWaitRule.hpp>
#include <mb/framepacing/pacer/hold/PresentWaitRule.hpp>
#include <mb/framepacing/pacer/rule/FrameWorkRule.hpp>
#include <mb/framepacing/pacer/tier/PacerHandover.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md, decision 7). The frames of an application that is not pacing
  //! (TierPacer::SetPacing): a frame loop that runs as it likes while the pacer is told of everything as before, so that
  //! pacing starts again with a history and not as a pacer that was just made.
  //!
  //! It places no frame and holds nothing: a frame has no time to start at, no time to wait until before its present and no
  //! time for the present. What goes on is what a run hands over (PacerHandover): the frames and their ids, the animation
  //! time, the GPU's work on the frames in flight and the presents that can be waited for. A frame's animation step is the
  //! time since the frame before it started, as an application without a pacer steps; after a pause (a gap longer than the
  //! caller says, or a clock that went back) it is one refresh. The swap interval rule gets no frame: a frame that is not
  //! placed can not be late.
  //!
  //! Pacing that starts again is as a start for the frames that wait to be shown: the pause after start-up is to be made
  //! once more when a frame was begun here.
  //!
  //! Values in, values out: no platform API, no clock read, no wait, and nothing is allocated.
  class FreeRunLoop
  {
    PresentWaitRule m_wait;
    GpuWaitRule m_gpuWait;
    FrameWorkRule m_frameWork;
    // The last frame that was begun, here or by the part that handed over
    bool m_hasFrame{false};
    uint64_t m_frameId{0};
    NanosecondTickCount m_startTime;
    bool m_frameOpen{false};
    bool m_frameEnded{false};
    RefreshTime m_animationTime;
    NanosecondTimeSpan m_lastAnimationTime;
    uint64_t m_refreshesBehindClock{0};
    NanosecondTimeDuration m_lastPresentBlocked;
    // The pause after start-up, as the part that handed over had it, and to be made once more after a frame of this loop
    bool m_pausePending{true};
    bool m_pauseHeldByWait{false};
    bool m_pauseHasFirstFrame{false};
    NanosecondTickCount m_pauseFirstFrameTime;
    bool m_presentTaken{false};

  public:
    //! The frame starts, at cpuStartTime. period: the refresh period the pacer is on. pauseAfter: a gap between two frame
    //! starts longer than this is a pause, across which the animation time goes on by one refresh.
    FrameSchedule BeginFrame(NanosecondTickCount cpuStartTime, RefreshPeriod period, NanosecondTimeSpan pauseAfter) noexcept;

    //! The frame's CPU work is done, at workDoneTime: nothing to wait for and nothing to give the present, and the marker's
    //! CPU busy time.
    PresentPlan EndFrame(NanosecondTickCount workDoneTime) noexcept;

    //! The frame's CPU busy time so far, at now. Zero: no frame is open.
    [[nodiscard]] NanosecondTimeDuration CpuBusyAt(NanosecondTickCount now) const noexcept;

    //! After the present: it is one that can be waited for when pacing starts again.
    void AddPresent(const PresentReport& report) noexcept;

    //! The GPU's work on an earlier frame. margin: the frame margin of the pacer's settings.
    void AddGpuWork(const GpuWorkReport& report, NanosecondTimeSpan margin) noexcept;

    //! The presents made so far are gone (a swap chain was made anew).
    void ForgetPresents() noexcept;

    //! Start again: the GPU's work and the presents are forgotten. The frame ids and the animation time go on.
    void Reset() noexcept;

    //! True between a frame's start and its end.
    [[nodiscard]] bool IsFrameOpen() const noexcept
    {
      return m_frameOpen && !m_frameEnded;
    }

    //! The GPU time that was reported last, zero without one.
    [[nodiscard]] NanosecondTimeDuration GpuTime() const noexcept
    {
      return m_frameWork.GpuTime();
    }

    //! How long the last present that was reported held the frame loop.
    [[nodiscard]] NanosecondTimeDuration LastPresentBlocked() const noexcept
    {
      return m_lastPresentBlocked;
    }

    //! What goes on when a part that paces takes over: the frames, the animation time, the GPU's work and the presents. Its
    //! first frame may start at once, and no present of this loop says where its presents are to be made.
    [[nodiscard]] PacerHandover GiveOver() const noexcept;

    //! Go on from a part that paced.
    void TakeOver(const PacerHandover& handover) noexcept;
  };
}

#endif
