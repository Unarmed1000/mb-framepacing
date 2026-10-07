#ifndef MB_FRAMEPACING_PACER_RULE_FRAMEWORKRULE_HPP
#define MB_FRAMEPACING_PACER_RULE_FRAMEWORKRULE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <array>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md "A frame's work is two stretches of time, not a sum"). What a frame's
  //! work is, for the swap interval rule: the frame time the loop needs for it. The CPU works on a frame from its start to the end
  //! of its work, the GPU for the time a GPU work report gives, and the two are put together by how they lie in time:
  //!   - side by side (the CPU works on a frame while the GPU works on the one before it): the longer of the two;
  //!   - one after the other (a frame starts when the GPU is done with the one before it): the two added.
  //! They lie side by side when the application says it lets two frames or more be in flight
  //! (PacerSettings::MaxFramesInFlight), or when the newest GPU work report with an end time showed it: the frame after it began
  //! more than a margin before that end. One after the other is the cautious reading, and the one without either.
  //!
  //! A frame's GPU work is known frames after the frame, so a frame is judged with the newest GPU time there is, which is an
  //! earlier frame's. One that is FrameCapacity frames old or more is not used. Without a GPU work report a frame's work is the
  //! CPU's alone.
  //!
  //! The tier pacers use it; it keeps the starts of the last FrameCapacity frames and never allocates.
  class FrameWorkRule
  {
  public:
    //! The frames whose starts are kept: a GPU work report is held against the start of the frame after its own.
    static constexpr uint32_t FrameCapacity = 16;

  private:
    // The starts of the newest frames, by frame id
    std::array<NanosecondTickCount, FrameCapacity> m_startTimes{};
    uint64_t m_newestFrameId{0};
    // The newest GPU work: the frame it is of (reports of frames before it are not taken), how long, and whether the frame
    // after it began before it ended
    uint64_t m_gpuFrameId{0};
    NanosecondTimeSpan m_gpuTime;
    bool m_hasGpuTime{false};
    bool m_overlapSeen{false};

  public:
    //! A frame started: frames are given in the order of their ids, one after the other.
    void AddFrameStart(uint64_t frameId, NanosecondTickCount cpuStartTime) noexcept;

    //! The GPU's work on a frame. Taken when it is of one of the newest FrameCapacity frames that started and not of an older
    //! frame than the one the rule has: a later report for the same frame takes the place of the first, for an application that
    //! learns how long the work took before it learns when. margin: a frame began before the GPU's work ended when it began
    //! more than this before it (the two are measured on clocks that are placed against each other, which is not exact).
    void AddGpuWork(const GpuWorkReport& report, NanosecondTimeSpan margin) noexcept;

    //! The frame time the loop needs for a frame whose CPU work took cpuWork.
    [[nodiscard]] NanosecondTimeSpan WorkOf(NanosecondTimeSpan cpuWork, uint32_t maxFramesInFlight) const noexcept;

    //! True when there is a GPU time to judge a frame with.
    [[nodiscard]] bool HasGpuTime() const noexcept
    {
      return m_hasGpuTime && (m_newestFrameId - m_gpuFrameId) < FrameCapacity;
    }

    //! The newest GPU time (zero without one).
    [[nodiscard]] NanosecondTimeDuration GpuTime() const noexcept
    {
      return HasGpuTime() ? NanosecondTimeDuration(m_gpuTime) : NanosecondTimeDuration();
    }

    //! True when the newest GPU work report showed the CPU's and the GPU's work side by side.
    [[nodiscard]] bool OverlapSeen() const noexcept
    {
      return HasGpuTime() && m_overlapSeen;
    }

    //! Forget the frames and the GPU's work: reports of frames from before are not taken.
    void Clear() noexcept;
  };
}

#endif
