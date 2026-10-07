#ifndef MB_FRAMEPACING_PACER_FRAME_GPUWORKREPORT_HPP
#define MB_FRAMEPACING_PACER_FRAME_GPUWORKREPORT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: part of a redesign that is not built yet). The GPU's work on a frame
  //! that was presented earlier, given frames later. Either when it began and ended (PacerCapability::GpuWorkTimes), from which the
  //! pacer reads how the CPU's and the GPU's work lie in time; or when it ended and how long it took, where a platform gives an
  //! elapsed time and one timestamp after the last command; or only how long it took (PacerCapability::GpuWorkDurations): how
  //! long, not when. The time from the first to the last of the frame's GPU work is meant, gaps included, as two timestamps give
  //! it.
  struct GpuWorkReport
  {
    //! The frame: its PresentPlan::FrameId.
    uint64_t FrameId{0};
    //! When the GPU began the frame's work, on the application's steady clock; NanosecondTickCount(): not known. It is not worked out
    //! from an end and a duration: that only holds where the GPU did not pause inside the frame
    NanosecondTickCount BeginTime;
    //! When the GPU ended the frame's work, on the same clock; NanosecondTickCount(): not known
    NanosecondTickCount EndTime;
    //! How long the work took: the end minus the begin where both are given
    NanosecondTimeDuration Duration;

    //! True when the report says when the work began and when it ended.
    [[nodiscard]] constexpr bool HasTimes() const noexcept
    {
      return BeginTime != NanosecondTickCount() && EndTime != NanosecondTickCount();
    }

    //! True when the report says when the work ended, with or without when it began: enough to hold the frame against the
    //! refresh it was aimed at.
    [[nodiscard]] constexpr bool HasEndTime() const noexcept
    {
      return EndTime != NanosecondTickCount();
    }

    //! The GPU worked on the frame from beginTime to endTime. An end before the begin is no work.
    [[nodiscard]] static constexpr GpuWorkReport Times(const uint64_t frameId, const NanosecondTickCount beginTime,
                                                       const NanosecondTickCount endTime) noexcept
    {
      return {frameId, beginTime, endTime, NanosecondTimeDuration(endTime - beginTime)};
    }

    //! The GPU ended its work on the frame at endTime, after working on it for a duration; when it began is not known.
    [[nodiscard]] static constexpr GpuWorkReport EndAndDuration(const uint64_t frameId, const NanosecondTickCount endTime,
                                                                const NanosecondTimeDuration duration) noexcept
    {
      return {frameId, NanosecondTickCount(), endTime, duration};
    }

    //! The GPU worked on the frame for a duration; when is not known.
    [[nodiscard]] static constexpr GpuWorkReport OfDuration(const uint64_t frameId, const NanosecondTimeDuration duration) noexcept
    {
      return {frameId, NanosecondTickCount(), NanosecondTickCount(), duration};
    }
  };
}

#endif
