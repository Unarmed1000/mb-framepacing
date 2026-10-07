#ifndef MB_FRAMEPACING_PACER_SIMULATION_FRAMELOOPSIMULATION_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_FRAMELOOPSIMULATION_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// A frame loop on a display that does what the first integration's logs show (DisplayModel): a present that never waits, frames
// that wait to be shown behind it, a GPU that works on a frame after its present. PacerSimulation's model has none of that: there
// a frame starts when the previous one is shown.
//
// The loop is the first integration's sample as it was on 2026-10-06: every frame it asks the pacer (BeginFrame, EndFrame) and
// then works out its waits itself from the schedule's NextFrameStartTime: on which side of the present the wait goes
// (LoopProfile), the time carried from frame to frame and started again after a frame that came more than half a frame time
// late, that time moved onto the vertical blanks where it has them, and a frame of a longer swap interval held before its
// present. That is the state the pacer's redesign starts from: what such a loop does when the display falls behind is what the
// tests pin here.

#include <string>
#include <vector>
#include "LoopFrame.hpp"
#include "LoopSettings.hpp"

namespace MB::FramePacing::Pacer::Simulation
{
  //! Run the loop: every frame with its stages, what the pacer said and when the display showed it.
  std::vector<LoopFrame> SimulateLoop(const LoopSettings& settings);

  //! The same display and the same work with an application that carries out what the pacer of the lowest tier gives
  //! it (TimerPeriodOnlyPacer) and works out nothing itself: it waits until the times it is given, and reports its present.
  //! The loop's own settings for where it waits and what it knows of the display (Profile, HasVBlankTimes) have no part in it.
  std::vector<LoopFrame> SimulateTimerPeriodOnlyLoop(const LoopSettings& settings);

  //! The same with the pacer of a timer and a wait for a present (TimerWaitForPresentPacer): before a frame the application
  //! waits until the present it is told was shown, which on the model returns PresentWaitReturn after the display took it (at
  //! once when that has passed), and reports the wait.
  std::vector<LoopFrame> SimulateTimerWaitForPresentLoop(const LoopSettings& settings);

  //! The same with the pacer of vertical blank times (VBlankPeriodOnlyPacer): before every frame the application gives the
  //! pacer the display's last vertical blank, as a window system would, and otherwise only carries out what it is given.
  std::vector<LoopFrame> SimulateVBlankPeriodOnlyLoop(const LoopSettings& settings);

  //! The same with the pacer of vertical blank times and a wait for a present (VBlankWaitForPresentPacer): the application
  //! also carries out the wait until a present was shown and reports what became of it.
  std::vector<LoopFrame> SimulateVBlankWaitForPresentLoop(const LoopSettings& settings);

  //! The frames as a frame log with the column names of the first integration's logs (the ones tools/frame_stages_chart.py draws),
  //! a row per frame, "\n" line ends. A moment a frame did not have is an empty cell. The display times are the model's own.
  std::string ToFrameLog(const std::vector<LoopFrame>& frames, const LoopSettings& settings);
}

#endif
