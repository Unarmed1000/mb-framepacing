//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* When the swap interval rule slows down (sdk/doc/pacer.md). Both speed up the same way.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Pacer
{
  public enum SlowDownRule : byte
  {
    /// <summary>
    /// The default: as FullWindow, and also as soon as the late frames since the last change pass the share of a full window's frames
    /// (SlowDownLatePercent of the frames the window holds at the current rate), without waiting for the window to fill again.
    /// </summary>
    LateCount = 0,

    /// <summary>Swappy's rule: only on a full window (more than WindowTicks of frames) with more than SlowDownLatePercent of them late.</summary>
    FullWindow = 1,
  }
}
