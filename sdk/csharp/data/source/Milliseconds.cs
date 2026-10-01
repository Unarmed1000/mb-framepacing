//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The CSV files' time format: milliseconds with at most four decimals, which is exactly a whole number of 100 ns ticks, so reading one back
//* gives the tick it was written from.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Globalization;

namespace MB.FramePacing.Data
{
  public static class Milliseconds
  {
    public const long TicksPerMillisecond = TimeSpan.TicksPerMillisecond;

    /// <summary>"16.6667" for 166 667 ticks: invariant culture, at most four decimals, no trailing zeros.</summary>
    public static string Format(TimeSpan span) => (span.Ticks / (double)TicksPerMillisecond).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>A point in time as the milliseconds since its clock's zero.</summary>
    public static string Format(TickCount64 time) => Format(time.ToTimeSpan());

    public static string Format(TimeSpan32 span) => Format(span.ToTimeSpan());

    /// <summary>The span of a milliseconds value (rounded to the nearest tick).</summary>
    public static TimeSpan FromMilliseconds(double milliseconds) => new TimeSpan((long)Math.Round(milliseconds * TicksPerMillisecond));

    /// <summary>The span of a milliseconds text (invariant culture).</summary>
    public static TimeSpan ParseMilliseconds(string text) => FromMilliseconds(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
  }
}
