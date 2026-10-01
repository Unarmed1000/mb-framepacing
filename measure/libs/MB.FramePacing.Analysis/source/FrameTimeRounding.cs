//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A frame time in whole refreshes, as the display shows frames: rounded up, with a little slack so a target just below a whole number of
//* refreshes (59.9 fps on 60 Hz) still means one. The analysis measures every frame against it, and the charts draw the same values.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Analysis
{
  public static class FrameTimeRounding
  {
    /// <summary>A frame time this share of a refresh above a whole number of refreshes still rounds down to it.</summary>
    public const double Slack = 0.05;

    /// <summary>
    /// <paramref name="frameTime"/> in whole refreshes of <paramref name="refresh"/>, rounded up, at least one (a 60 fps target on 144 Hz
    /// alternates 2 and 3 refreshes: 3 is the target).
    /// </summary>
    public static TimeSpan WholeRefreshes(TimeSpan frameTime, TimeSpan refresh) => WholeRefreshes((double)frameTime.Ticks, refresh);

    /// <summary>The frame time of <paramref name="framesPerSecond"/> in whole refreshes of <paramref name="refresh"/>, as <see cref="WholeRefreshes(TimeSpan, TimeSpan)"/>.</summary>
    public static TimeSpan WholeRefreshesAtRate(double framesPerSecond, TimeSpan refresh) =>
      WholeRefreshes(TimeSpan.TicksPerSecond / framesPerSecond, refresh);

    // A rate's frame time is not a whole number of ticks: the rounding takes it as it is
    private static TimeSpan WholeRefreshes(double frameTimeTicks, TimeSpan refresh) =>
      new TimeSpan(Math.Max(1, (long)Math.Ceiling((frameTimeTicks / refresh.Ticks) - Slack)) * refresh.Ticks);
  }
}
