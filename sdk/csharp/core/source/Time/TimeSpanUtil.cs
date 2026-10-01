//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Conversions to System.TimeSpan that give the same ticks on every runtime. TimeSpan.FromSeconds itself does not: .NET Framework and
//* Unity's Mono round it to a whole millisecond (1 / 60 s becomes 17 ms), which an animation time cannot take. FromSeconds here is the C++
//* core's TimeSpan::FromSeconds (and .NET 10's): truncated toward zero to a tick.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing
{
  public static class TimeSpanUtil
  {
    // 2^63: what TimeSpan.MaxValue's ticks round to as a double
    private const double TickLimit = 9_223_372_036_854_775_808.0;

    /// <summary>
    /// Seconds (an animation clock's time, for example) truncated toward zero to a tick: 1.0 / 60 is 166 666 ticks. Throws
    /// ArgumentException for NaN and OverflowException outside the range of a TimeSpan.
    /// </summary>
    public static TimeSpan FromSeconds(double seconds)
    {
      if (double.IsNaN(seconds))
      {
        throw new ArgumentException("The value is NaN", nameof(seconds));
      }
      double ticks = seconds * TimeSpan.TicksPerSecond;
      if (ticks < -TickLimit || ticks > TickLimit)
      {
        throw new OverflowException("The value is outside the range of a TimeSpan");
      }
      return ticks == TickLimit ? TimeSpan.MaxValue : new TimeSpan((long)ticks);
    }
  }
}
