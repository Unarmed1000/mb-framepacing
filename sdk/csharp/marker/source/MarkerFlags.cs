//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The payload's flags byte. See doc/marker-format.md "Flags". Bits 2 to 7 are reserved: write 0; a decoded payload keeps whatever it
//* carried, so values without a name here survive a round trip.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  [Flags]
  public enum MarkerFlags : byte
  {
    None = 0,

    /// <summary>
    /// Nothing animates while this frame is on screen, until the next frame (the application has no pending work after it). Says nothing
    /// about whether this frame itself animated. The analysis does not judge the step from it to the next frame.
    /// </summary>
    StaticAfter = 1 << 0,

    /// <summary>
    /// Nothing animated while the frame before this one was on screen: <see cref="StaticAfter"/> of the previous frame, for an application
    /// that only knows it once it renders this frame.
    /// </summary>
    StaticBefore = 1 << 1,
  }
}
