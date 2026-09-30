//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A stretch of a scenario's time in which every frame's work is drawn from [MinWorkTicks, MaxWorkTicks] (the C++ tests' LoadStage).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Pacer.UnitTest.Simulation
{
  internal readonly struct LoadStage
  {
    public LoadStage(long fromTicks, long toTicks, long minWorkTicks, long maxWorkTicks)
    {
      FromTicks = fromTicks;
      ToTicks = toTicks;
      MinWorkTicks = minWorkTicks;
      MaxWorkTicks = maxWorkTicks;
    }

    /// <summary>From (and to, excluded) the simulation's start.</summary>
    public readonly long FromTicks;

    public readonly long ToTicks;

    public readonly long MinWorkTicks;

    public readonly long MaxWorkTicks;
  }
}
