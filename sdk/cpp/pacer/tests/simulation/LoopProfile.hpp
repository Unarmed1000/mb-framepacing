#ifndef MB_FRAMEPACING_PACER_SIMULATION_LOOPPROFILE_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_LOOPPROFILE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

namespace MB::FramePacing::Pacer::Simulation
{
  //! Which side of the present a frame loop waits on for the pacer's time (the first integration's two profiles)
  enum class LoopProfile
  {
    //! The frame's start waits, and the frame is presented when its CPU work is done
    RenderLate,
    //! The frame is rendered right away and its present waits; the next frame starts when the present was made
    RenderEarly
  };
}

#endif
