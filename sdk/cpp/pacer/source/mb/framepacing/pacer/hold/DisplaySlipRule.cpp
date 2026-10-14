// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The slip of the display behind the frames' plan, from reported display times (sdk/doc/pacer-design.md, decision 20).
#include <mb/framepacing/pacer/hold/DisplaySlipRule.hpp>
#include <cstddef>

namespace MB::FramePacing::Pacer
{
  void DisplaySlipRule::AddFrame(const uint64_t frameId, const NanosecondTickCount plannedDisplayTime) noexcept
  {
    if (frameId == 0 || frameId != m_newestId + 1u)
    {
      // Not the next frame: the ids started again, and the frames before are not this run's
      Restart();
      m_oldestId = frameId;
    }
    m_newestId = frameId;
    if ((m_newestId - m_oldestId) >= Capacity)
    {
      ++m_oldestId;
    }
    const auto index = static_cast<std::size_t>(frameId % Capacity);
    m_planned[index] = plannedDisplayTime;
    m_movedAtBegin[index] = m_moved;
  }

  void DisplaySlipRule::Replan(const uint64_t frameId, const NanosecondTickCount plannedDisplayTime) noexcept
  {
    if (m_newestId != 0 && frameId >= m_oldestId && frameId <= m_newestId)
    {
      // The new plan is on the plan as it is now, with every move so far
      const auto index = static_cast<std::size_t>(frameId % Capacity);
      m_planned[index] = plannedDisplayTime;
      m_movedAtBegin[index] = m_moved;
    }
  }

  void DisplaySlipRule::AddMove(const int64_t refreshes) noexcept
  {
    m_moved += refreshes;
  }

  int64_t DisplaySlipRule::AddDisplayReport(const DisplayReport& report, const RefreshPeriod period) noexcept
  {
    if (!report.Shown || m_newestId == 0 || report.FrameId < m_oldestId || report.FrameId > m_newestId)
    {
      return 0;
    }
    const auto index = static_cast<std::size_t>(report.FrameId % Capacity);
    // The lag less the moves its plan did not have yet: the frames on their way when the plan moved were shown late by the move
    const NanosecondTimeSpan lag((report.DisplayTime - m_planned[index]).Nanoseconds() -
                                 period.TimeFor(m_moved - m_movedAtBegin[index]).Nanoseconds());
    if (!m_hasLeast || lag < m_least)
    {
      m_least = lag;
      m_hasLeast = true;
      return 0;
    }
    const int64_t slip = period.NearestRefreshes(NanosecondTimeSpan(lag.Nanoseconds() - m_least.Nanoseconds()));
    if (slip > 0)
    {
      m_moved += slip;
      m_least = NanosecondTimeSpan(m_least.Nanoseconds() + period.TimeFor(slip).Nanoseconds());
    }
    return slip;
  }

  void DisplaySlipRule::Restart() noexcept
  {
    m_newestId = 0;
    m_oldestId = 1;
    m_least = NanosecondTimeSpan();
    m_hasLeast = false;
    m_moved = 0;
  }
}
