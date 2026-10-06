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
    int64_t FirstBlankTicks{10'000'000};
    //! How long before a vertical blank a frame has to be ready (presented, and its GPU work done) to be taken for that blank
    int64_t LatchLeadTicks{0};
    //! Whole refreshes from the vertical blank a frame is taken for to the one it is shown at: 0 shows it at that blank, 1 is a
    //! compositor that takes a refresh of its own
    int32_t PipelineRefreshes{0};
    //! 0: nothing bounds the frames that wait to be shown, and an acquire never waits (the first integration's swap chain in a
    //! window). From 2: the swap chain's images, one on screen and one drawn into, so an acquire waits while Images - 2 presented
    //! frames wait to be shown (its full screen swap chain)
    int32_t Images{0};
    //! Vertical blanks at which the display takes no frame although one is ready, ascending
    std::vector<int64_t> HeldBlanks;
  };
}

#endif
