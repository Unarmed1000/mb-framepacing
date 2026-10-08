#ifndef MB_FRAMEPACING_PACER_SIMULATION_DISPLAYMODEL_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_DISPLAYMODEL_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <cstdint>
#include <vector>
#include "DisplayModelSettings.hpp"

namespace MB::FramePacing::Pacer::Simulation
{
  //! A display behind a present that never waits: every present is accepted at once and queued, and the display takes the oldest
  //! queued frame at a vertical blank, one frame per blank, in the order of the presents. So frames that are presented as fast as
  //! the display takes them keep whatever number of them is waiting, and every blank at which none is taken adds one.
  //!
  //! Test code. Times are nanoseconds on the steady clock the frame loop uses; presents come in the order of their times.
  class DisplayModel
  {
    struct Entry
    {
      int64_t PresentNanoseconds{0};
      int64_t ShownNanoseconds{0};
    };

    RefreshPeriod m_period;
    DisplayModelSettings m_settings;
    std::vector<Entry> m_presents;
    int64_t m_lastTakenBlank{-1};
    bool m_anyTaken{false};

  public:
    DisplayModel(RefreshPeriod period, DisplayModelSettings settings);

    //! The time of a vertical blank
    [[nodiscard]] int64_t BlankNanoseconds(int64_t blank) const noexcept;

    //! The last vertical blank at or before a time (blank 0 for a time before it)
    [[nodiscard]] int64_t BlankAtOrBefore(int64_t nanoseconds) const noexcept;

    //! The first vertical blank at or after a time (blank 0 for a time before it)
    [[nodiscard]] int64_t BlankAtOrAfter(int64_t nanoseconds) const noexcept;

    //! A frame is presented at presentNanoseconds, its GPU work done at gpuEndNanoseconds (before or after the present), to stay swapInterval
    //! refreshes behind the frame shown before it. Returns when it is shown: what the display does with it is decided here, as
    //! nothing presented later can change it.
    int64_t Present(int64_t presentNanoseconds, int64_t gpuEndNanoseconds, uint32_t swapInterval = 1);

    //! The same for a present that is given a time: the frame is not shown before notBeforeNanoseconds (0: no such time), and
    //! not before the frame shown before it was on screen for minimumDurationNanoseconds (0: no such time). With neither it
    //! is a present at a swap interval of 1.
    int64_t PresentTimed(int64_t presentNanoseconds, int64_t gpuEndNanoseconds, int64_t notBeforeNanoseconds, int64_t minimumDurationNanoseconds);

    //! The frames presented at or before a time that are shown after it
    [[nodiscard]] int32_t Pending(int64_t nanoseconds) const noexcept;

    //! When an acquire called at a time returns: the time itself where nothing bounds the waiting frames
    [[nodiscard]] int64_t AcquireNanoseconds(int64_t nanoseconds) const noexcept;

  private:
    //! The display takes the frame at the first vertical blank it is ready for, and no sooner than at earliestBlank
    int64_t Take(int64_t presentNanoseconds, int64_t gpuEndNanoseconds, int64_t earliestBlank);
  };
}

#endif
