//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the pacer's tests share: 60 Hz, milliseconds in ticks, and the settings they pace with.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Pacer.UnitTest
{
  internal static class TestPacing
  {
    public const long Ms = TimeSpan.TicksPerMillisecond;

    public const long Second = TimeSpan.TicksPerSecond;

    public static readonly RefreshPeriod Hz60 = RefreshPeriod.FromRate(60);

    public static PacerSettings Settings(SlowDownRule rule = SlowDownRule.LateCount) => new PacerSettings(Hz60) { SlowDown = rule };
  }
}
