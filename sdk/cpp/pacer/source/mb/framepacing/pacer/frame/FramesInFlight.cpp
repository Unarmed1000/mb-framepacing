// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The frames in flight of sdk/doc/pacer.md: frames measured by the display times the platform's present feedback reports.
#include <mb/framepacing/pacer/frame/FramesInFlight.hpp>
#include <mb/framepacing/pacer/frame/PresentResult.hpp>
#include <algorithm>
#include <utility>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    //! A display time counts as a refresh when it is within the period divided by this of one
    constexpr int64_t GridToleranceDivisor = 8;

    //! The most refreshes one measurement counts as late, and the most the count runs ahead of the display
    constexpr int64_t MaxRefreshes = int64_t{1} << 20;
  }

  FramesInFlight::FramesInFlight(const RefreshPeriod period) noexcept
    : m_period(period)
  {
  }

  uint64_t FramesInFlight::Begin(const uint32_t swapInterval, const TimeSpan animationTime, const TickCount64 startTime) noexcept
  {
    ++m_newestId;
    if (m_newestId - m_oldestId >= Capacity)
    {
      ++m_oldestId;
    }
    // A frame that left without being taken is not given later, and a display time whose frame has left is too old to measure from
    m_nextMeasuredId = std::max(m_nextMeasuredId, m_oldestId);
    if (m_hasAnchor && m_anchorId < m_oldestId)
    {
      m_hasAnchor = false;
      m_hasCandidate = false;
      m_lead = 0;
    }
    m_swapSum += swapInterval;
    At(m_newestId) = Entry{m_swapSum, animationTime, TimeSpan(), startTime, false, false};
    return m_newestId;
  }

  void FramesInFlight::End(const TimeSpan work, const TickCount64 presentTime) noexcept
  {
    if (m_newestId >= m_oldestId)
    {
      Entry& rEntry = At(m_newestId);
      rEntry.Work = work;
      rEntry.PresentTime = presentTime;
    }
  }

  void FramesInFlight::Add(const PresentFeedback& feedback) noexcept
  {
    const uint64_t frameId = feedback.FrameId;
    if (frameId <= m_newestFeedbackId || frameId < m_oldestId || frameId > m_newestId)
    {
      ++m_state.Refused;
      return;
    }
    m_newestFeedbackId = frameId;
    Entry& rEntry = At(frameId);
    rEntry.HasFeedback = true;
    if (feedback.Result != PresentResult::Shown)
    {
      ++m_state.NotShown;
      return;
    }
    if (feedback.DisplayTime < (feedback.HasPresentTime ? feedback.PresentTime : rEntry.PresentTime))
    {
      ++m_state.Refused;
      return;
    }
    if (m_hasAnchor)
    {
      const TimeSpan step = feedback.DisplayTime - m_anchorTime;
      if (IsWholeRefreshes(step))
      {
        // The refreshes since the display time used before, against the swap intervals of the frames from that one to this one. What
        // is beyond them, and beyond the lead, is how far the animation is behind the display
        const int64_t refreshes = m_period.NearestRefreshes(step);
        const auto swapIntervals = static_cast<int64_t>(rEntry.SwapSum - m_anchorSwapSum);
        const int64_t behind = refreshes - swapIntervals - int64_t{m_lead};
        rEntry.Late = refreshes > swapIntervals;
        m_lateRefreshes += static_cast<uint32_t>(std::clamp(behind, int64_t{0}, MaxRefreshes));
        m_lead = static_cast<uint32_t>(std::clamp(-behind, int64_t{0}, MaxRefreshes));
      }
      else if (m_hasCandidate && IsWholeRefreshes(feedback.DisplayTime - m_candidateTime))
      {
        // Two display times in a row on a grid of their own: the display's refreshes moved, and nothing is measured across that
        m_lead = 0;
      }
      else
      {
        m_hasCandidate = true;
        m_candidateTime = feedback.DisplayTime;
        ++m_state.Refused;
        return;
      }
    }
    m_hasAnchor = true;
    m_anchorTime = feedback.DisplayTime;
    m_anchorId = frameId;
    m_anchorSwapSum = rEntry.SwapSum;
    m_hasCandidate = false;
    m_decidedId = frameId;
    ++m_state.Used;
  }

  uint32_t FramesInFlight::TakeLateRefreshes() noexcept
  {
    return std::exchange(m_lateRefreshes, 0u);
  }

  bool FramesInFlight::TakeMeasured(MeasuredFrame& rFrame) noexcept
  {
    // Decided by a display time at or after it, or about to leave: the next frame begun takes its place
    if (m_nextMeasuredId > m_decidedId && m_nextMeasuredId + Capacity > m_newestId + 1u)
    {
      return false;
    }
    const Entry& entry = At(m_nextMeasuredId);
    m_state.Missing += entry.HasFeedback ? 0u : 1u;
    rFrame = MeasuredFrame{m_nextMeasuredId, entry.AnimationTime, entry.Work, entry.Late};
    ++m_nextMeasuredId;
    return true;
  }

  TickCount64 FramesInFlight::IntendedDisplayTime() const noexcept
  {
    return m_hasAnchor ? m_anchorTime + m_period.TimeFor(int64_t{m_lead} + static_cast<int64_t>(m_swapSum - m_anchorSwapSum)) : TickCount64();
  }

  void FramesInFlight::Restart() noexcept
  {
    m_oldestId = m_newestId + 1u;
    m_nextMeasuredId = m_oldestId;
    m_decidedId = m_newestId;
    m_newestFeedbackId = m_newestId;
    m_hasAnchor = false;
    m_hasCandidate = false;
    m_lead = 0;
    m_lateRefreshes = 0;
  }

  void FramesInFlight::SetRefreshPeriod(const RefreshPeriod period) noexcept
  {
    m_period = period;
    Restart();
  }

  FramesInFlight::Entry& FramesInFlight::At(const uint64_t frameId) noexcept
  {
    return m_entries[frameId % Capacity];
  }

  bool FramesInFlight::IsWholeRefreshes(const TimeSpan step) const noexcept
  {
    // A step back counts as none: the same refresh, when it is within the tolerance
    const int64_t off = step.Ticks() - m_period.TimeFor(m_period.NearestRefreshes(step)).Ticks();
    const int64_t tolerance = m_period.ToTimeSpan().Ticks() / GridToleranceDivisor;
    return off >= -tolerance && off <= tolerance;
  }
}
