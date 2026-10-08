#ifndef MB_FRAMEPACING_PACER_HOLD_GPUWAITRULE_HPP
#define MB_FRAMEPACING_PACER_HOLD_GPUWAITRULE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWaitReport.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built; the simulation only, not measured).
  //! What holds the frame loop where the application can wait until the GPU finished a frame and has no wait for a present:
  //! one of the parts a pacer is put together from, the same in every tier that uses it. It says which frame's GPU work
  //! the frame start plan asks for and how long the wait may take. Values in, values out: no clock is read and nothing is
  //! allocated.
  //!
  //! The wait is for the frame before the one that is about to be made with the aim of low latency (one frame in flight:
  //! the CPU's and the GPU's work on a frame come one after the other), and for the frame before that with the aim of
  //! smoothness (two in flight: the CPU works on a frame while the GPU works on the one before it), where the application
  //! lets two frames be in flight (PacerSettings::MaxFramesInFlight). It is the application's one wait for a frame slot:
  //! the pacer names the frame, and the application makes no such wait of its own next to it.
  //!
  //! It keeps a loop that the GPU limits from making frames the GPU has not got to. It says nothing of the display: a
  //! frame the GPU finished can wait to be shown.
  class GpuWaitRule
  {
    // The frames presented: the last one the system took, and the first whose GPU work can still be waited for
    uint64_t m_lastAcceptedId{0};
    uint64_t m_oldestWaitableId{1};
    uint64_t m_timeouts{0};
    // The frame the wait before the next frame was made for: it is not asked for again
    uint64_t m_waitedForId{0};
    // A wait was reported since the last frame started: a frame has one wait for the GPU's work, not two
    bool m_waitReported{false};
    // A wait since the last frame started held the loop
    bool m_heldTheLoop{false};

  public:
    //! A wait held the loop when it took this share of a refresh period or more: one in this many.
    static constexpr int64_t HeldDivisor = 8;

    //! The frames that are in flight while a frame is made with these settings, the one being made among them: 1 or 2.
    [[nodiscard]] static uint32_t FramesInFlight(const PacerSettings& settings) noexcept;

    //! A frame start plan with the wait for the GPU's work the next frame begins with, if there is one to make: the frame
    //! so many back, and the longest the wait may take (a few of the frame's own swap intervals). The plan has no start time.
    [[nodiscard]] FrameStartPlan Plan(const PacerSettings& settings, RefreshPeriod period, uint32_t swapInterval) const noexcept;

    //! How a wait the plan asked for ended.
    void AddGpuWait(const GpuWaitReport& report, RefreshPeriod period) noexcept;

    //! A present was made: the GPU's work on a frame the system took can be waited for, and a frame it refused is gone
    //! with the frames before it.
    void AddPresent(const PresentReport& report) noexcept;

    //! The swap chain was made anew: none of the frames made so far is waited for.
    void ForgetPresents() noexcept;

    //! A frame starts: the wait before it is done with. Call it after reading HeldTheLoop for that frame.
    void BeginFrame() noexcept;

    //! ForgetPresents, and what the wait before the next frame said is dropped.
    void Reset() noexcept;

    //! True when the wait since the last frame started took a share of a refresh period (one in HeldDivisor) or more.
    [[nodiscard]] bool HeldTheLoop() const noexcept
    {
      return m_heldTheLoop;
    }

    //! The waits that ended without the GPU done with their frame.
    [[nodiscard]] uint64_t Timeouts() const noexcept
    {
      return m_timeouts;
    }
  };
}

#endif
