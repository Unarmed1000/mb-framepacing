#ifndef MB_FRAMEPACING_PACER_SIMULATION_DISPLAYMODELSETTINGS_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_DISPLAYMODELSETTINGS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>
#include <vector>

namespace MB::FramePacing::Pacer::Simulation
{
  //! What a DisplayModel is made with. The values are knobs to try a pacer against, not a description of any system: what the first
  //! integration's logs show of one system fits several settings, and why its display now and then held a frame is not understood
  //! (which is what HeldBlanks is for).
  struct DisplayModelSettings
  {
    //! Vertical blank 0 on the steady clock: at 1 s unless set
    int64_t FirstBlankNanoseconds{1'000'000'000};
    //! How long before a vertical blank a frame has to be ready (presented, and its GPU work done) to be taken for that blank
    int64_t LatchLeadNanoseconds{0};
    //! Whole refreshes from the vertical blank a frame is taken for to the one it is shown at: 0 shows it at that blank, 1 is a
    //! compositor that takes a refresh of its own
    int32_t PipelineRefreshes{0};
    //! 0: nothing bounds the frames that wait to be shown, and an acquire never waits (the first integration's swap chain in a
    //! window). From 2: the swap chain's images, one on screen and one drawn into, so an acquire waits while Images - 2 presented
    //! frames wait to be shown (its full screen swap chain)
    int32_t Images{0};
    //! Vertical blanks at which the display takes no frame although one is ready, ascending
    std::vector<int64_t> HeldBlanks;
    //! A stretch of time in which the window is covered (from, until; until 0: none): an acquire returns at once, and a frame
    //! presented then is never shown and waits for nothing, as measured on the first integration's Windows system
    int64_t CoveredFromNanoseconds{0};
    int64_t CoveredUntilNanoseconds{0};
    //! The longest an acquire waits (0: as long as the images say): a hold that lasts a share of a refresh, as a virtual
    //! machine's present was measured to wait about a third of one. The frames it lets through are queued all the same
    int64_t AcquireWaitsAtMostNanoseconds{0};
  };
}

#endif
