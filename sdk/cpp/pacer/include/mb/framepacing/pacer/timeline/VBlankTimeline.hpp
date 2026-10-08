#ifndef MB_FRAMEPACING_PACER_TIMELINE_VBLANKTIMELINE_HPP
#define MB_FRAMEPACING_PACER_TIMELINE_VBLANKTIMELINE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). Where the display's refreshes are,
  //! from the vertical blank times the application reads: one of the parts a pacer is put together from, the same in every
  //! tier that has the times. The vertical blanks are numbered, and a number stays the same vertical blank whatever the
  //! readings do to its time. Values in, values out: no clock is read and nothing is allocated.
  //!
  //! A reading is checked against the ones before it. Within a share of a refresh period (one in JumpDivisor) of where they
  //! put a vertical blank it is taken, and moves that blank a part of the way to it (one in FollowDivisor): one reading is
  //! not exact, and the next one moves it again. Further off it is counted (Jumps) and not taken by itself; ReadingsToMoveGrid
  //! of them in a row that are on one grid of their own are the display's, which has changed, and the last of them moves the
  //! vertical blanks to it. Until the first reading a time the pacer gives (a frame's start) stands in for a vertical blank.
  class VBlankTimeline
  {
    // The vertical blanks: the time of one of them and its number, from the newest reading that was taken (or a frame's start
    // until there is one)
    NanosecondTickCount m_anchorTime;
    int64_t m_anchorSlot{0};
    bool m_hasAnchor{false};
    bool m_hasReading{false};
    // The newest reading's read time: one read before it is not taken
    NanosecondTickCount m_lastReadTime;
    bool m_hasReadTime{false};
    uint64_t m_jumps{0};
    // The readings in a row that were off the vertical blanks and on one grid of their own, and the last of them
    uint32_t m_offGridReadings{0};
    NanosecondTickCount m_offGridTime;

  public:
    //! A reading is off where the readings before it put the vertical blanks when it is more than the period divided by this.
    static constexpr int64_t JumpDivisor = 8;
    //! A reading near a vertical blank moves it this share of the way to the reading: one in this many.
    static constexpr int64_t FollowDivisor = 4;
    //! The readings in a row, off the vertical blanks and on one grid of their own, that move the vertical blanks to theirs.
    static constexpr uint32_t ReadingsToMoveGrid = 8;

    //! A vertical blank the application read. One read before the newest is not taken.
    void AddVBlank(const VBlankReading& reading, RefreshPeriod period) noexcept;

    //! Without a vertical blank so far, the time is taken as one (number 0). With one it changes nothing.
    void StartAt(NanosecondTickCount time) noexcept;

    //! The vertical blanks are not known any more (another refresh period): the next reading, or StartAt, is the first.
    void Clear() noexcept;

    //! The time of the vertical blank with that number.
    [[nodiscard]] NanosecondTickCount TimeOfBlank(int64_t slot, RefreshPeriod period) const noexcept;

    //! The number of the last vertical blank at or before the time.
    [[nodiscard]] int64_t BlankAtOrBefore(NanosecondTickCount time, RefreshPeriod period) const noexcept;

    //! True when a reading was taken since the vertical blanks were last cleared.
    [[nodiscard]] bool HasReading() const noexcept
    {
      return m_hasReading;
    }

    //! The readings that were off where the readings before them put the vertical blanks.
    [[nodiscard]] uint64_t Jumps() const noexcept
    {
      return m_jumps;
    }
  };
}

#endif
