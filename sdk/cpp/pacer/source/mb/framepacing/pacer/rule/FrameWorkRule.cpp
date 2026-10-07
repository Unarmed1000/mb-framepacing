// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. A frame's work as two stretches of time (sdk/doc/pacer-design.md): the CPU's and the GPU's, the longer of the two
// where they lie side by side and the two added where one follows the other.
#include <mb/framepacing/pacer/rule/FrameWorkRule.hpp>
#include <algorithm>
#include <limits>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    //! The longest GPU time that is kept: sums with a frame's CPU work stay far from the ends of a span's range
    constexpr int64_t MaxGpuTicks = std::numeric_limits<uint32_t>::max();
  }

  void FrameWorkRule::AddFrameStart(const uint64_t frameId, const TickCount64 cpuStartTime) noexcept
  {
    m_startTimes[frameId % FrameCapacity] = cpuStartTime;
    m_newestFrameId = frameId;
  }

  void FrameWorkRule::AddGpuWork(const GpuWorkReport& report, const TimeSpan margin) noexcept
  {
    // Of a frame that started, not older than the one the rule has (a later report for the same frame takes the place of the
    // first), and not so old that the start of the frame after it is gone
    if (report.FrameId == 0 || report.FrameId > m_newestFrameId || report.FrameId < m_gpuFrameId ||
        (m_newestFrameId - report.FrameId) >= FrameCapacity)
    {
      return;
    }
    m_gpuFrameId = report.FrameId;
    m_gpuTime = TimeSpan(std::min(report.Duration.Ticks(), MaxGpuTicks));
    m_hasGpuTime = true;
    if (!report.HasEndTime() || report.FrameId == m_newestFrameId)
    {
      // No end to hold against a start; or the frame after it has not started, so it starts after this end
      m_overlapSeen = false;
      return;
    }
    m_overlapSeen = (report.EndTime - m_startTimes[(report.FrameId + 1u) % FrameCapacity]) > margin;
  }

  TimeSpan FrameWorkRule::WorkOf(const TimeSpan cpuWork, const uint32_t maxFramesInFlight) const noexcept
  {
    if (!HasGpuTime())
    {
      return cpuWork;
    }
    const bool sideBySide = maxFramesInFlight >= 2u || m_overlapSeen;
    return sideBySide ? std::max(cpuWork, m_gpuTime) : TimeSpan(cpuWork.Ticks() + m_gpuTime.Ticks());
  }

  void FrameWorkRule::Clear() noexcept
  {
    m_gpuFrameId = m_newestFrameId + 1u;
    m_hasGpuTime = false;
    m_overlapSeen = false;
  }
}
