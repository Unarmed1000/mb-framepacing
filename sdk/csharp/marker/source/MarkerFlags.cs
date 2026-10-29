//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The payload's flags byte. See doc/marker-format.md "Flags". Bits 4 to 7 are reserved: write 0; a decoded payload keeps whatever it
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
    /// <summary>No flag. Named as in the C++ library, where X11's headers define "None" as a macro.</summary>
    NoFlags = 0,

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

    /// <summary>
    /// On a start marker: the application logs the frame record of this run to its own trace (the trace module,
    /// doc/trace-frame-record.md), so there is a trace the capture of the run can be merged with. Only the application knows: it sets the
    /// flag itself and nothing in the SDK does, so it is off unless set. It says nothing on a frame, end or sync marker.
    /// </summary>
    FrameRecordLogged = 1 << 2,

    /// <summary>
    /// A pacer or a frame limiter held this frame's start to a plan (it waited, or set when the frame began); a present that blocks until
    /// the display takes it (vsync alone) is not that. The application sets it on every such frame, from its own pacer or limiter; it is off
    /// unless set. A start or end marker carries the value of the frame that shows it.
    /// </summary>
    Paced = 1 << 3,
  }
}
