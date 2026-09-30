//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A load to pace: given frame by frame (Frames, played Passes times), or as stages of time (Stages, Calm outside them) until DurationTicks, each
//* frame's work drawn by the stage its start falls in with SplitMix64 from Seed. The C++ tests' Scenario.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Pacer.UnitTest.Simulation
{
  internal sealed class Scenario
  {
    public string Name { get; init; } = "";

    /// <summary>The refresh rate in Hz, RateNumerator / RateDenominator.</summary>
    public uint RateNumerator { get; init; } = 60;

    public uint RateDenominator { get; init; } = 1;

    public ScenarioFrame[] Frames { get; init; } = Array.Empty<ScenarioFrame>();

    /// <summary>false: paced at a fixed swap interval of 1, the rule off (both rules give the same frames: one result, &lt;name&gt;-Fixed.csv).</summary>
    public bool AutoSwapInterval { get; init; } = true;

    public int Passes { get; init; } = 1;

    public LoadStage[] Stages { get; init; } = Array.Empty<LoadStage>();

    public LoadStage Calm { get; init; }

    public long DurationTicks { get; init; }

    public ulong Seed { get; init; }
  }
}
