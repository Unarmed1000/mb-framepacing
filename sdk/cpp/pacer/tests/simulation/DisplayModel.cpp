// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include "DisplayModel.hpp"
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
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

  int64_t DisplayModel::BlankNanoseconds(const int64_t blank) const noexcept
  {
    return m_settings.FirstBlankNanoseconds + m_period.TimeFor(blank).Nanoseconds();
  }

  int64_t DisplayModel::BlankAtOrBefore(const int64_t nanoseconds) const noexcept
  {
    return nanoseconds <= m_settings.FirstBlankNanoseconds
             ? 0
             : m_period.FloorRefreshes(NanosecondTimeSpan(nanoseconds - m_settings.FirstBlankNanoseconds));
  }

  int64_t DisplayModel::BlankAtOrAfter(const int64_t nanoseconds) const noexcept
  {
    const int64_t blank = BlankAtOrBefore(nanoseconds);
    return BlankNanoseconds(blank) < nanoseconds ? blank + 1 : blank;
  }

  int64_t DisplayModel::Present(const int64_t presentNanoseconds, const int64_t gpuEndNanoseconds, const uint32_t swapInterval)
  {
    // One frame per blank, in the order of the presents, and no sooner after the frame before it than its swap interval
    const int64_t earliestBlank = m_anyTaken ? m_lastTakenBlank + static_cast<int64_t>(std::max(swapInterval, 1u)) : 0;
    return Take(presentNanoseconds, gpuEndNanoseconds, earliestBlank);
  }

  int64_t DisplayModel::PresentTimed(const int64_t presentNanoseconds, const int64_t gpuEndNanoseconds, const int64_t notBeforeNanoseconds,
                                     const int64_t minimumDurationNanoseconds)
  {
    // One frame per blank, in the order of the presents
    int64_t earliestBlank = m_anyTaken ? m_lastTakenBlank + 1 : 0;
    if (notBeforeNanoseconds != 0)
    {
      // Not shown before the time: taken for the first blank whose frame is shown at or after it
      earliestBlank = std::max(earliestBlank, BlankAtOrAfter(notBeforeNanoseconds) - m_settings.PipelineRefreshes);
    }
    if (m_anyTaken && minimumDurationNanoseconds > 0)
    {
      // The frame before it stays that long: as long from the blank it was taken for, as the two are shown as much later
      earliestBlank = std::max(earliestBlank, BlankAtOrAfter(BlankNanoseconds(m_lastTakenBlank) + minimumDurationNanoseconds));
    }
    return Take(presentNanoseconds, gpuEndNanoseconds, earliestBlank);
  }

  int64_t DisplayModel::Take(const int64_t presentNanoseconds, const int64_t gpuEndNanoseconds, const int64_t earliestBlank)
  {
    // The first blank the frame is ready for in time
    int64_t blank = std::max(BlankAtOrAfter(std::max(presentNanoseconds, gpuEndNanoseconds) + m_settings.LatchLeadNanoseconds), earliestBlank);
    while (std::binary_search(m_settings.HeldBlanks.begin(), m_settings.HeldBlanks.end(), blank))
    {
      ++blank;
    }
    m_lastTakenBlank = blank;
    m_anyTaken = true;
    const int64_t shownNanoseconds = BlankNanoseconds(blank + m_settings.PipelineRefreshes);
    m_presents.push_back({presentNanoseconds, shownNanoseconds});
    return shownNanoseconds;
  }

  int32_t DisplayModel::Pending(const int64_t nanoseconds) const noexcept
  {
    // Shown times rise with the presents, so the frames still waiting are the last ones
    int32_t pending = 0;
    for (std::size_t index = m_presents.size(); index > 0 && m_presents[index - 1].ShownNanoseconds > nanoseconds; --index)
    {
      if (m_presents[index - 1].PresentNanoseconds <= nanoseconds)
      {
        ++pending;
      }
    }
    return pending;
  }

  int64_t DisplayModel::AcquireNanoseconds(const int64_t nanoseconds) const noexcept
  {
    if (m_settings.Images <= 0)
    {
      return nanoseconds;
    }
    const int32_t allowed = std::max(m_settings.Images, 2) - 2;
    const int32_t pending = Pending(nanoseconds);
    if (pending <= allowed)
    {
      return nanoseconds;
    }
    // The waiting frames are the last `pending` presents: the acquire returns when all but `allowed` of them are shown
    const std::size_t first = m_presents.size() - static_cast<std::size_t>(pending);
    return m_presents[first + static_cast<std::size_t>(pending - allowed - 1)].ShownNanoseconds;
  }
}
