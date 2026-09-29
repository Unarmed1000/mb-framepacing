//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The payload's flags byte. See doc/marker-format.md "Flags". Bits 1 to 7 are reserved: write 0; a decoded payload keeps whatever it
//* carried, so values without a name here survive a round trip.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FrameMarker
{
  [Flags]
  public enum MarkerFlags : byte
  {
    None = 0,

    /// <summary>
    /// Nothing animates in this frame (an idle screen, a paused menu with nothing moving): the analysis does not judge the animation error of
    /// a step from or to it.
    /// </summary>
    Static = 1 << 0,
  }
}
