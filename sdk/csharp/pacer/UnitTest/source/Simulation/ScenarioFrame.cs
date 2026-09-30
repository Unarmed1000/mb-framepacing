//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One frame of a scenario given frame by frame: how long it works, and what a reference simulation paced it at (for a cross-check).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Pacer.UnitTest.Simulation
{
  internal readonly struct ScenarioFrame
  {
    public ScenarioFrame(long workTicks, int referenceSwapInterval, long referenceShownRefresh)
    {
      WorkTicks = workTicks;
      ReferenceSwapInterval = referenceSwapInterval;
      ReferenceShownRefresh = referenceShownRefresh;
    }

    public readonly long WorkTicks;

    /// <summary>The reference's swap interval for this frame, 0 = none.</summary>
    public readonly int ReferenceSwapInterval;

    /// <summary>The refresh the reference showed this frame on (any origin), -1 = none.</summary>
    public readonly long ReferenceShownRefresh;
  }
}
