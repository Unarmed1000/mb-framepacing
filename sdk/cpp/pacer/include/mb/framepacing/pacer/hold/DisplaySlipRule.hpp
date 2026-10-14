#ifndef MB_FRAMEPACING_PACER_HOLD_DISPLAYSLIPRULE_HPP
#define MB_FRAMEPACING_PACER_HOLD_DISPLAYSLIPRULE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/display/DisplayReport.hpp>
#include <array>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: decision 20). How many refreshes the display has slipped behind the
  //! frames' plan, from the display times the platform reports: one of the parts a pacer is put together from, for the second
  //! major tier (the display's side places a frame after the one before it) with the aim of low latency and no wait for a
  //! present.
  //!
  //! Placed after the frame before, a frame shown a refresh late moves every frame after it a refresh later: the display stays
  //! smooth, and the frames the loop makes on its plan wait a refresh longer each. A frame's lag is its reported display time
  //! less its planned one, less the refreshes the plan was moved by after it began (its plan did not have them yet). The least
  //! lag seen is the display's own delay, which is no slip; a lag that is whole refreshes more is, and AddDisplayReport gives
  //! them, and raises the least by them: the frames planned after the move are not counted again, whether the move took the
  //! wait away or not (a display that has no frame waiting, which a move can not help, is moved once and not again). A lag
  //! below the least lowers it. Nothing moves the animation time.
  //!
  //! Values in, values out: no clock is read and nothing is allocated.
  class DisplaySlipRule
  {
  public:
    //! The frames kept. A report for an older frame is not counted: more than any platform's queue of results.
    static constexpr uint32_t Capacity = 64;

  private:
    // The planned display time of every frame kept and what the plan was moved by when it began, by its id
    std::array<NanosecondTickCount, Capacity> m_planned{};
    std::array<int64_t, Capacity> m_movedAtBegin{};
    // The newest frame begun (0: none) and the oldest one kept
    uint64_t m_newestId{0};
    uint64_t m_oldestId{1};
    // The least lag (raised by every move) and whether there is one
    NanosecondTimeSpan m_least;
    bool m_hasLeast{false};
    // The refreshes the plan was moved later by so far
    int64_t m_moved{0};

  public:
    //! A frame begins: its id (one more than the frame before) and the time it is planned to be shown at. An id that is not the next
    //! one starts again from it.
    void AddFrame(uint64_t frameId, NanosecondTickCount plannedDisplayTime) noexcept;

    //! The loop found out by itself that a frame is shown later than it was planned (its present was late, and the frames after
    //! it are planned from where it is shown): its lag counts from there, a time on the plan as it is now. A frame not kept is
    //! passed over.
    void Replan(uint64_t frameId, NanosecondTickCount plannedDisplayTime) noexcept;

    //! The loop moved its plan later by itself, by that many refreshes (0 or more): the frames begun before are late by them, and
    //! they are not counted as a slip.
    void AddMove(int64_t refreshes) noexcept;

    //! A display report: the refreshes to move the plan later by now (0 for none, a frame not shown, or one not kept).
    [[nodiscard]] int64_t AddDisplayReport(const DisplayReport& report, RefreshPeriod period) noexcept;

    //! Start again: the frames begun so far are forgotten, and the least lag is measured anew.
    void Restart() noexcept;

    //! The refreshes the plan was moved later by since the frames started: by this rule and by the loop (AddMove).
    [[nodiscard]] int64_t Moved() const noexcept
    {
      return m_moved;
    }
  };
}

#endif
