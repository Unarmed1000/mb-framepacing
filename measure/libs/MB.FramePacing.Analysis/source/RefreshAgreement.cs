//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Whether two refresh rates agree, and the refresh periods the markers carry: one rule for the analysis's warnings and the report card's.
//* The markers' refresh period is the application's word about its display; it is compared with the refresh the analysis measures with,
//* never taken as it, and a disagreement never says which of the two is wrong.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MB.FramePacing.Analysis
{
  public static class RefreshAgreement
  {
    /// <summary>How far two refresh rates may be apart, relative, and still agree: 1 % (60 Hz for 59.94 Hz is 0.1 %).</summary>
    public const double Tolerance = 0.01;

    /// <summary>The two rates agree: <paramref name="actualHz"/> is within <see cref="Tolerance"/> of <paramref name="expectedHz"/>.</summary>
    public static bool Within(double actualHz, double expectedHz) => Math.Abs((actualHz / expectedHz) - 1) <= Tolerance;

    /// <summary>The rate of a refresh period, in Hz.</summary>
    public static double RateOf(NanosecondTimeDuration period) => NanosecondTimeSpan.NanosecondsPerSecond / (double)period.Nanoseconds;

    /// <summary>The refresh periods the frames' markers carry (0, unknown, left out) with how many frames carry each, the most first.</summary>
    public static IReadOnlyList<MarkedRefresh> MarkerPeriods(IEnumerable<PresentedFrame> frames) =>
      frames
        .Where(frame => frame.MarkerRefreshPeriod != NanosecondTimeDuration.Zero)
        .GroupBy(frame => frame.MarkerRefreshPeriod)
        .Select(group => new MarkedRefresh(group.Key, group.Count()))
        .OrderByDescending(marked => marked.Frames)
        .ThenBy(marked => marked.Period.Nanoseconds)
        .ToList();

    /// <summary>The marked periods whose rate does not agree with <paramref name="refreshHz"/>, the most frames first.</summary>
    public static IReadOnlyList<MarkedRefresh> Disagreeing(IEnumerable<MarkedRefresh> marked, double refreshHz) =>
      marked.Where(m => !Within(m.Hz, refreshHz)).ToList();

    /// <summary>"a 144 Hz display (1200 frames), a 60 Hz display (300 frames)".</summary>
    public static string Displays(IEnumerable<MarkedRefresh> marked) =>
      string.Join(
        ", ",
        marked.Select(m => string.Create(CultureInfo.InvariantCulture, $"a {m.Hz:0.##} Hz display ({m.Frames} frame{(m.Frames == 1 ? "" : "s")})"))
      );
  }
}
