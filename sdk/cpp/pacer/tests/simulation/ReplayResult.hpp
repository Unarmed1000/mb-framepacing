#ifndef MB_FRAMEPACING_PACER_SIMULATION_REPLAYRESULT_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_REPLAYRESULT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>
#include <map>
#include <string>

namespace MB::FramePacing::Pacer::Simulation
{
  //! What a replay of a frame log gave (ReplayLog): a row per frame, and the counts a test or a person reads first.
  struct ReplayResult
  {
    //! A row per replayed frame (ReplayHeader), "\n" line ends
    std::string Csv;
    //! The frames given to the pacer: the log's rows with the pacer on and a frame start
    int64_t Frames{0};
    //! Of them, the frames for which the log has the pacer's answer, and those where the replay's answer is the log's (swap
    //! interval, animation step and next frame start). A log replayed with the settings it was written with agrees on all of them.
    int64_t Compared{0};
    int64_t Agreeing{0};
    //! The frames with a display time
    int64_t Shown{0};
    //! How far behind the display was, which the pacer is not told: the frames by the whole refreshes from their start to their
    //! display time (rounded), and by the earlier frames that were presented and not yet shown when they started
    std::map<int64_t, int64_t> RefreshesToDisplay;
    std::map<int64_t, int64_t> PendingAtStart;
    //! The frames by their display time minus the intended display time the pacer gave, in whole refreshes (rounded)
    std::map<int64_t, int64_t> RefreshesAfterIntended;
  };
}

#endif
