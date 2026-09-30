//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the application knows when it presents a frame (FramePacer.EndFrame).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Pacer
{
  public readonly struct FrameEnd
  {
    public FrameEnd(long presentTicks, long workTicks = 0)
    {
      PresentTicks = presentTicks;
      WorkTicks = workTicks;
    }

    /// <summary>When Present is called, on the steady clock of FrameInput (required).</summary>
    public readonly long PresentTicks;

    /// <summary>
    /// How long the frame needed, as the swap interval rule should count it: the CPU's time, or the CPU's and the GPU's, or whatever the
    /// application measures. 0: the CPU busy time (PresentTicks - the frame's NowTicks).
    /// </summary>
    public readonly long WorkTicks;
  }
}
