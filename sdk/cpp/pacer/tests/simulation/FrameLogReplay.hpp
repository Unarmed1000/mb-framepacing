#ifndef MB_FRAMEPACING_PACER_SIMULATION_FRAMELOGREPLAY_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_FRAMELOGREPLAY_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// A frame log replayed through the pacer: the pacer is given what the log's application gave it, in the log's order, and what it
// answers is held against what the log shows the display did. It is an open loop: a log can show what a pacer would have seen and
// said, never what a different answer would have caused (FrameLoopSimulation is for that).
//
// The logs are frame logs with the column names of the first integration's sample (a CSV, a row per frame, times in ticks on the
// application's CPU clock), which FrameLoopSimulation's ToFrameLog writes too.

#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <filesystem>
#include <string>
#include <string_view>
#include <vector>
#include "LoggedFrame.hpp"
#include "ReplayResult.hpp"

namespace MB::FramePacing::Pacer::Simulation
{
  //! The columns of a replay's rows
  inline constexpr std::string_view ReplayHeader =
    "frameIndex,startTicks,swapInterval,loggedSwapInterval,animationStepTicks,loggedAnimationStepTicks,nextFrameStartTicks,"
    "loggedNextFrameStartTicks,intendedDisplayTicks,shownTicks,shownMinusIntendedTicks,refreshesToDisplay,pendingAtStart";

  //! The rows of a frame log, from its text: the columns are found by name, and one the log does not have is 0 in every frame.
  //! Throws std::runtime_error for a text without the frameIndex and frameStartTicks columns, and for a log with a frame's start
  //! one row early (the sample's OpenGL ES logs before 2026-10-06, known by a pacer frame id two ahead of the frame index).
  std::vector<LoggedFrame> ReadFrameLog(std::string_view text);

  //! The same from a file. Throws std::runtime_error when it can not be opened.
  std::vector<LoggedFrame> ReadFrameLogFile(const std::filesystem::path& path);

  //! The refresh period the log's pacer paced at: its target frame time over its swap interval, the value most frames have.
  //! To the tick only: a replay that has to agree with the log to the tick needs the period the application gave its pacer.
  //! Throws std::runtime_error for a log without a target frame time.
  RefreshPeriod LoggedRefreshPeriod(const std::vector<LoggedFrame>& frames);

  //! Replay the frames through a pacer on that refresh period, with the swap interval rule on or off (the other settings at their
  //! defaults, present feedback off).
  ReplayResult ReplayLog(const std::vector<LoggedFrame>& frames, RefreshPeriod period, bool autoSwapInterval);
}

#endif
