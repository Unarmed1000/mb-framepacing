// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include "DisplayModel.hpp"
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <algorithm>
#include <cstddef>
#include <utility>

namespace MB::FramePacing::Pacer::Simulation
{
  DisplayModel::DisplayModel(const RefreshPeriod period, DisplayModelSettings settings)
    : m_period(period)
    , m_settings(std::move(settings))
  {
  }

  int64_t DisplayModel::BlankTicks(const int64_t blank) const noexcept
  {
    return m_settings.FirstBlankTicks + m_period.TimeFor(blank).Ticks();
  }

  int64_t DisplayModel::BlankAtOrBefore(const int64_t ticks) const noexcept
  {
    return ticks <= m_settings.FirstBlankTicks ? 0 : m_period.FloorRefreshes(TimeSpan(ticks - m_settings.FirstBlankTicks));
  }

  int64_t DisplayModel::Present(const int64_t presentTicks, const int64_t gpuEndTicks, const uint32_t swapInterval)
  {
    // The first blank the frame is ready for in time
    const int64_t latchTicks = std::max(presentTicks, gpuEndTicks) + m_settings.LatchLeadTicks;
    int64_t blank = BlankAtOrBefore(latchTicks);
    if (BlankTicks(blank) < latchTicks)
    {
      ++blank;
    }
    // One frame per blank, in the order of the presents, and no sooner after the frame before it than its swap interval
    if (m_anyTaken)
    {
      blank = std::max(blank, m_lastTakenBlank + static_cast<int64_t>(std::max(swapInterval, 1u)));
    }
    while (std::binary_search(m_settings.HeldBlanks.begin(), m_settings.HeldBlanks.end(), blank))
    {
      ++blank;
    }
    m_lastTakenBlank = blank;
    m_anyTaken = true;
    const int64_t shownTicks = BlankTicks(blank + m_settings.PipelineRefreshes);
    m_presents.push_back({presentTicks, shownTicks});
    return shownTicks;
  }

  int32_t DisplayModel::Pending(const int64_t ticks) const noexcept
  {
    // Shown times rise with the presents, so the frames still waiting are the last ones
    int32_t pending = 0;
    for (std::size_t index = m_presents.size(); index > 0 && m_presents[index - 1].ShownTicks > ticks; --index)
    {
      if (m_presents[index - 1].PresentTicks <= ticks)
      {
        ++pending;
      }
    }
    return pending;
  }

  int64_t DisplayModel::AcquireTicks(const int64_t ticks) const noexcept
  {
    if (m_settings.Images <= 0)
    {
      return ticks;
    }
    const int32_t allowed = std::max(m_settings.Images, 2) - 2;
    const int32_t pending = Pending(ticks);
    if (pending <= allowed)
    {
      return ticks;
    }
    // The waiting frames are the last `pending` presents: the acquire returns when all but `allowed` of them are shown
    const std::size_t first = m_presents.size() - static_cast<std::size_t>(pending);
    return m_presents[first + static_cast<std::size_t>(pending - allowed - 1)].ShownTicks;
  }
}
