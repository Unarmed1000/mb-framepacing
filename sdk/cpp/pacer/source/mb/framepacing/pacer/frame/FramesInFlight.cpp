// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The frames in flight of sdk/doc/pacer.md: what the display times of the platform's present feedback say about the
// frames. Statistics and the intended display time; nothing here paces a frame.
#include <mb/framepacing/pacer/frame/FramesInFlight.hpp>
#include <mb/framepacing/pacer/frame/PresentResult.hpp>
#include <algorithm>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    //! A display time counts as a refresh when it is within the period divided by this of one
    constexpr int64_t GridToleranceDivisor = 8;
  }

  FramesInFlight::FramesInFlight(const RefreshPeriod period) noexcept
    : m_period(period)
  {
  }

  uint64_t FramesInFlight::Begin(const uint32_t swapInterval, const TickCount64 startTime) noexcept
  {
    ++m_newestId;
    if (m_newestId - m_oldestId >= Capacity)
    {
      // The oldest frame leaves: without feedback when none came for it or for a newer frame
      m_state.Missing += m_oldestId > m_newestFeedbackId ? 1u : 0u;
      ++m_oldestId;
    }
    // A display time whose frame has left is too old to count from
    if (m_hasAnchor && m_anchorId < m_oldestId)
    {
      m_hasAnchor = false;
      m_hasCandidate = false;
      m_lead = 0;
    }
    m_swapSum += swapInterval;
    At(m_newestId) = Entry{m_swapSum, startTime};
    return m_newestId;
  }

  void FramesInFlight::End(const TickCount64 presentTime) noexcept
  {
    if (m_newestId >= m_oldestId)
    {
      At(m_newestId).PresentTime = presentTime;
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
    // Feedback comes oldest first: the frames kept between the one before and this one get none any more
    m_state.Missing += frameId - std::max(m_newestFeedbackId + 1u, m_oldestId);
    m_newestFeedbackId = frameId;
    const Entry& entry = At(frameId);
    if (feedback.Result != PresentResult::Shown)
    {
      // Never shown: the display time after it counts across it
      ++m_state.NotShown;
      return;
    }
    if (feedback.DisplayTime < (feedback.HasPresentTime ? feedback.PresentTime : entry.PresentTime))
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
        // is beyond them, and beyond the lead, is how far the display fell behind
        const int64_t refreshes = m_period.NearestRefreshes(step);
        const auto swapIntervals = static_cast<int64_t>(entry.SwapSum - m_anchorSwapSum);
        const int64_t behind = refreshes - swapIntervals - static_cast<int64_t>(m_lead);
        m_state.LateRefreshes += static_cast<uint64_t>(std::max(behind, int64_t{0}));
        m_lead = static_cast<uint64_t>(std::max(-behind, int64_t{0}));
      }
      else if (m_hasCandidate && IsWholeRefreshes(feedback.DisplayTime - m_candidateTime))
      {
        // Two display times in a row on a grid of their own: the display's refreshes moved, and nothing is counted across that
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
    m_anchorSwapSum = entry.SwapSum;
    m_hasCandidate = false;
    ++m_state.Used;
  }

  TickCount64 FramesInFlight::IntendedDisplayTime() const noexcept
  {
    // From the display time alone, without the lead: a frame shown a refresh early (a loop paced by sleeping does that now and then)
    // is followed by frames a swap interval after it, not a refresh later still
    return m_hasAnchor ? m_anchorTime + m_period.TimeFor(static_cast<int64_t>(m_swapSum - m_anchorSwapSum)) : TickCount64();
  }

  void FramesInFlight::Restart() noexcept
  {
    m_oldestId = m_newestId + 1u;
    m_newestFeedbackId = m_newestId;
    m_hasAnchor = false;
    m_hasCandidate = false;
    m_lead = 0;
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
