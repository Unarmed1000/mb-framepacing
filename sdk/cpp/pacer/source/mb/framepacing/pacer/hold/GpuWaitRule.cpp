// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The wait for the GPU's work on an earlier frame, as every tier without a wait for a present makes it where the
// application can (sdk/doc/pacer-design.md, "How a pacer is put together").
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/hold/GpuWaitRule.hpp>
#include <algorithm>

namespace MB::FramePacing::Pacer
{
  uint32_t GpuWaitRule::FramesInFlight(const PacerSettings& settings) noexcept
  {
    // One with the aim of low latency. Two with smoothness, where the application lets two frames be in flight
    return settings.Aim() == PacerAim::Smoothness ? std::min(settings.MaxFramesInFlight(), 2u) : 1u;
  }

  FrameStartPlan GpuWaitRule::Plan(const PacerSettings& settings, const RefreshPeriod period, const uint32_t swapInterval) const noexcept
  {
    FrameStartPlan plan;
    // The frame to wait for: the one FramesInFlight back from the frame that is about to be made
    const uint64_t back = uint64_t{FramesInFlight(settings)} - 1u;
    if (!m_waitReported && m_lastAcceptedId > back && (m_lastAcceptedId - back) >= m_oldestWaitableId && (m_lastAcceptedId - back) != m_waitedForId)
    {
      plan.WaitForGpuWorkFrameId = m_lastAcceptedId - back;
      // As long as a few of the frame's own swap intervals, as the wait for a present: a GPU that never finishes a frame
      // holds the loop no longer
      const int64_t refreshes = int64_t{settings.PresentWaitSwapIntervals()} * swapInterval;
      plan.WaitForGpuWorkTimeout = NanosecondTimeDuration(period.TimeFor(refreshes));
    }
    return plan;
  }

  void GpuWaitRule::AddGpuWait(const GpuWaitReport& report, const RefreshPeriod period) noexcept
  {
    m_waitedForId = report.FrameId;
    m_waitReported = true;
    m_timeouts += report.Done ? 0u : 1u;
    m_heldTheLoop = m_heldTheLoop || report.Blocked().Nanoseconds() >= (period.ToNanosecondTimeSpan().Nanoseconds() / HeldDivisor);
  }

  void GpuWaitRule::AddPresent(const PresentReport& report) noexcept
  {
    if (report.Accepted)
    {
      m_lastAcceptedId = report.FrameId;
      // A frame presented again after its first present was not taken
      m_oldestWaitableId = std::min(m_oldestWaitableId, report.FrameId);
    }
    else
    {
      // The swap chain the present was made for is gone, and the frames before it with it
      m_oldestWaitableId = report.FrameId + 1u;
    }
  }

  void GpuWaitRule::ForgetPresents() noexcept
  {
    m_oldestWaitableId = m_lastAcceptedId + 1u;
  }

  void GpuWaitRule::BeginFrame() noexcept
  {
    m_waitReported = false;
    m_heldTheLoop = false;
  }

  void GpuWaitRule::Reset() noexcept
  {
    ForgetPresents();
    m_waitReported = false;
    m_heldTheLoop = false;
  }
}
