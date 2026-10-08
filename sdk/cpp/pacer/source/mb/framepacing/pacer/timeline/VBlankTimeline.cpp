// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. Where the display's refreshes are, from vertical blank readings (sdk/doc/pacer-design.md, "How a pacer is put
// together").
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/timeline/VBlankTimeline.hpp>

namespace MB::FramePacing::Pacer
{
  void VBlankTimeline::AddVBlank(const VBlankReading& reading, const RefreshPeriod period) noexcept
  {
    if (m_hasReadTime && reading.ReadTime < m_lastReadTime)
    {
      // Read before the one the timeline has
      return;
    }
    m_lastReadTime = reading.ReadTime;
    m_hasReadTime = true;
    NanosecondTickCount blankTime = reading.VBlankTime;
    if (m_hasAnchor)
    {
      // The vertical blank it is, by where the timeline has them: the nearest one. The frames keep the blanks they are for
      const int64_t periodNanoseconds = period.ToNanosecondTimeSpan().Nanoseconds();
      int64_t slot = BlankAtOrBefore(reading.VBlankTime, period);
      slot += ((reading.VBlankTime - TimeOfBlank(slot, period)).Nanoseconds() * 2) >= periodNanoseconds ? 1 : 0;
      const int64_t offNanoseconds = (reading.VBlankTime - TimeOfBlank(slot, period)).Nanoseconds();
      if (m_hasReading && (offNanoseconds >= 0 ? offNanoseconds : -offNanoseconds) > (periodNanoseconds / JumpDivisor))
      {
        // Off where the readings before it put the vertical blanks: counted, and not taken by itself, as a reading that is not
        // exact is not to move the frames. Readings in a row that are on one grid of their own are the display's, which has
        // changed: the last of them moves the vertical blanks to it, whole
        ++m_jumps;
        const int64_t apart = (reading.VBlankTime - m_offGridTime).Nanoseconds();
        const int64_t distance = apart >= 0 ? apart : -apart;
        const int64_t onItsGrid = distance - period.TimeFor(period.NearestRefreshes(NanosecondTimeSpan(distance))).Nanoseconds();
        const bool agrees = m_offGridReadings > 0 && distance >= (periodNanoseconds / 2) &&
                            (onItsGrid >= 0 ? onItsGrid : -onItsGrid) <= (periodNanoseconds / JumpDivisor);
        m_offGridReadings = agrees ? m_offGridReadings + 1u : 1u;
        m_offGridTime = reading.VBlankTime;
        if (m_offGridReadings < ReadingsToMoveGrid)
        {
          return;
        }
      }
      else if (m_hasReading)
      {
        // Near where the timeline has that vertical blank: it is moved a part of the way to the reading. One reading is not
        // exact, and the next one moves it again, so a display that is a little off its period is followed all the same
        blankTime = TimeOfBlank(slot, period) + NanosecondTimeSpan(offNanoseconds / FollowDivisor);
      }
      m_anchorSlot = slot;
    }
    m_offGridReadings = 0;
    m_anchorTime = blankTime;
    m_hasAnchor = true;
    m_hasReading = true;
  }

  void VBlankTimeline::StartAt(const NanosecondTickCount time) noexcept
  {
    if (!m_hasAnchor)
    {
      m_anchorTime = time;
      m_anchorSlot = 0;
      m_hasAnchor = true;
    }
  }

  void VBlankTimeline::Clear() noexcept
  {
    m_hasAnchor = false;
    m_hasReading = false;
  }

  NanosecondTickCount VBlankTimeline::TimeOfBlank(const int64_t slot, const RefreshPeriod period) const noexcept
  {
    const int64_t steps = slot - m_anchorSlot;
    return steps >= 0 ? m_anchorTime + period.TimeFor(steps) : m_anchorTime - period.TimeFor(-steps);
  }

  int64_t VBlankTimeline::BlankAtOrBefore(const NanosecondTickCount time, const RefreshPeriod period) const noexcept
  {
    const int64_t nanoseconds = (time - m_anchorTime).Nanoseconds();
    return nanoseconds >= 0 ? m_anchorSlot + period.FloorRefreshes(NanosecondTimeSpan(nanoseconds))
                            : m_anchorSlot - period.RefreshesToFit(NanosecondTimeSpan(-nanoseconds));
  }
}
