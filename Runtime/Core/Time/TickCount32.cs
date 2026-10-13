//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A point on a 32-bit clock in ticks of 100 ns: the count wraps around every 2^32 ticks (429.4967296 s). The SDK never reads a clock;
//* the application fills it, or takes the low 32 bits of a TickCount64 (FromTickCount64). The count is stored unsigned, so adding and
//* subtracting wrap around; Ticks is its signed view.
//*
//* Two counts compare and subtract correctly while they are less than 2^31 ticks (214.7483648 s) apart, across the wrap too: a < b when
//* b is ahead of a, the serial number rule (RFC 1982). So the comparisons are not a total order (the type is not IComparable), and counts
//* exactly 2^31 apart are each "less" than the other. The From... factories throw OverflowException for a value outside the range;
//* nothing else throws or allocates. The C++ core's TickCount32, member for member.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing
{
  public readonly struct TickCount32 : IEquatable<TickCount32>
  {
    public const long NanosecondsPerTick = TickCount64.NanosecondsPerTick;
    public const long TicksPerMicrosecond = TickCount64.TicksPerMicrosecond;
    public const long TicksPerMillisecond = TickCount64.TicksPerMillisecond;
    public const long TicksPerSecond = TickCount64.TicksPerSecond;
    public const long TicksPerMinute = TickCount64.TicksPerMinute;
    public const long TicksPerHour = TickCount64.TicksPerHour;
    public const long TicksPerDay = TickCount64.TicksPerDay;

    // The range of each From... factory: what the signed 32-bit view holds (a day or an hour does not fit, so their range is 0)
    public const int MinDays = (int)(int.MinValue / TicksPerDay);
    public const int MaxDays = (int)(int.MaxValue / TicksPerDay);
    public const int MinHours = (int)(int.MinValue / TicksPerHour);
    public const int MaxHours = (int)(int.MaxValue / TicksPerHour);
    public const int MinMinutes = (int)(int.MinValue / TicksPerMinute);
    public const int MaxMinutes = (int)(int.MaxValue / TicksPerMinute);
    public const int MinSeconds = (int)(int.MinValue / TicksPerSecond);
    public const int MaxSeconds = (int)(int.MaxValue / TicksPerSecond);
    public const int MinMilliseconds = (int)(int.MinValue / TicksPerMillisecond);
    public const int MaxMilliseconds = (int)(int.MaxValue / TicksPerMillisecond);
    public const int MinMicroseconds = (int)(int.MinValue / TicksPerMicrosecond);
    public const int MaxMicroseconds = (int)(int.MaxValue / TicksPerMicrosecond);

    public TickCount32(uint ticks)
    {
      UnsignedTicks = ticks;
    }

    public TickCount32(int ticks)
    {
      UnsignedTicks = unchecked((uint)ticks);
    }

    /// <summary>The low 32 bits of the span's ticks: the point sinceEpoch after the clock's epoch, wrapped.</summary>
    public TickCount32(TimeSpan sinceEpoch)
    {
      UnsignedTicks = unchecked((uint)sinceEpoch.Ticks);
    }

    /// <summary>The count as stored.</summary>
    public readonly uint UnsignedTicks;

    public static TickCount32 FromTicks(int ticks) => new TickCount32(ticks);

    /// <summary>The count as stored (UnsignedTicks).</summary>
    public static TickCount32 FromUnsignedTicks(uint ticks) => new TickCount32(ticks);

    /// <summary>The same point on the 32-bit clock: the low 32 bits of the count.</summary>
    public static TickCount32 FromTickCount64(TickCount64 count) => new TickCount32(unchecked((uint)count.UnsignedTicks));

    /// <summary>Throws OverflowException outside MinDays to MaxDays (only 0 fits).</summary>
    public static TickCount32 FromDays(int days) => FromUnits(days, MinDays, MaxDays, TicksPerDay);

    /// <summary>Throws OverflowException outside MinHours to MaxHours (only 0 fits).</summary>
    public static TickCount32 FromHours(int hours) => FromUnits(hours, MinHours, MaxHours, TicksPerHour);

    /// <summary>Throws OverflowException outside MinMinutes to MaxMinutes.</summary>
    public static TickCount32 FromMinutes(int minutes) => FromUnits(minutes, MinMinutes, MaxMinutes, TicksPerMinute);

    /// <summary>Throws OverflowException outside MinSeconds to MaxSeconds.</summary>
    public static TickCount32 FromSeconds(int seconds) => FromUnits(seconds, MinSeconds, MaxSeconds, TicksPerSecond);

    /// <summary>Throws OverflowException outside MinMilliseconds to MaxMilliseconds.</summary>
    public static TickCount32 FromMilliseconds(int milliseconds) => FromUnits(milliseconds, MinMilliseconds, MaxMilliseconds, TicksPerMillisecond);

    /// <summary>Throws OverflowException outside MinMicroseconds to MaxMicroseconds.</summary>
    public static TickCount32 FromMicroseconds(int microseconds) => FromUnits(microseconds, MinMicroseconds, MaxMicroseconds, TicksPerMicrosecond);

    /// <summary>Nanoseconds rounded down to the tick they are in; every int fits.</summary>
    public static TickCount32 FromNanoseconds(int nanoseconds)
    {
      int ticks = (int)(nanoseconds / NanosecondsPerTick);
      return new TickCount32((nanoseconds % NanosecondsPerTick) < 0 ? ticks - 1 : ticks);
    }

    /// <summary>The count as a signed number of ticks.</summary>
    public int Ticks => unchecked((int)UnsignedTicks);

    /// <summary>The whole days of Ticks (always 0). Every component is truncated toward zero and has the sign of Ticks.</summary>
    public int Days => (int)(Ticks / TicksPerDay);

    public int Hours => (int)((Ticks / TicksPerHour) % 24);

    public int Minutes => (int)((Ticks / TicksPerMinute) % 60);

    public int Seconds => (int)((Ticks / TicksPerSecond) % 60);

    public int Milliseconds => (int)((Ticks / TicksPerMillisecond) % 1000);

    public int Microseconds => (int)((Ticks / TicksPerMicrosecond) % 1000);

    public double TotalNanoseconds => (double)Ticks * NanosecondsPerTick;

    public double TotalMicroseconds => (double)Ticks / TicksPerMicrosecond;

    public double TotalMilliseconds => (double)Ticks / TicksPerMillisecond;

    public double TotalSeconds => (double)Ticks / TicksPerSecond;

    public double TotalMinutes => (double)Ticks / TicksPerMinute;

    public double TotalHours => (double)Ticks / TicksPerHour;

    public double TotalDays => (double)Ticks / TicksPerDay;

    /// <summary>Ticks as a span since the clock's epoch.</summary>
    public TimeSpan ToTimeSpan() => new TimeSpan(Ticks);

    /// <summary>Adds the span modulo 2^32: wraps around.</summary>
    public static TickCount32 operator +(TickCount32 count, TimeSpan span) => new TickCount32(unchecked(count.UnsignedTicks + (uint)span.Ticks));

    /// <summary>Subtracts the span modulo 2^32: wraps around.</summary>
    public static TickCount32 operator -(TickCount32 count, TimeSpan span) => new TickCount32(unchecked(count.UnsignedTicks - (uint)span.Ticks));

    /// <summary>
    /// The time from right to left, -2^31 to 2^31 - 1 ticks: correct while the counts are less than 2^31 ticks apart.
    /// </summary>
    public static TimeSpan operator -(TickCount32 left, TickCount32 right) => new TimeSpan(Distance(left, right));

    public bool Equals(TickCount32 other) => UnsignedTicks == other.UnsignedTicks;

    public override bool Equals(object obj) => obj is TickCount32 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(UnsignedTicks);

    public static bool operator ==(TickCount32 left, TickCount32 right) => left.Equals(right);

    public static bool operator !=(TickCount32 left, TickCount32 right) => !left.Equals(right);

    /// <summary>left is before right (see the type: less than 2^31 ticks apart).</summary>
    public static bool operator <(TickCount32 left, TickCount32 right) => Distance(left, right) < 0;

    public static bool operator <=(TickCount32 left, TickCount32 right) => Distance(left, right) <= 0;

    public static bool operator >(TickCount32 left, TickCount32 right) => Distance(left, right) > 0;

    public static bool operator >=(TickCount32 left, TickCount32 right) => Distance(left, right) >= 0;

    /// <summary>The time since the clock's epoch as TimeSpan writes it (culture invariant).</summary>
    public override string ToString() => ToTimeSpan().ToString();

    /// <summary>
    /// The ticks from right to left as a signed 32-bit number: the unsigned difference wraps, its signed view is the shorter way round.
    /// </summary>
    private static int Distance(TickCount32 left, TickCount32 right) => unchecked((int)(left.UnsignedTicks - right.UnsignedTicks));

    private static TickCount32 FromUnits(int value, int minValue, int maxValue, long ticksPerUnit)
    {
      if (value < minValue || value > maxValue)
      {
        throw new OverflowException("The value is outside the range of a TickCount32");
      }
      return new TickCount32((int)(value * ticksPerUnit));
    }
  }
}
