//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A point on the application's steady clock in ticks of 100 ns, any epoch (the same clock for the whole run): the marker's intended
//* display time and CPU start time. The SDK never reads a clock; the application fills it from its own. The count is stored unsigned, so
//* adding and subtracting wrap around; Ticks is its signed view.
//*
//* Wrap-around safe, as TickCount32: two counts compare and subtract correctly while they are less than 2^63 ticks apart, across the
//* wrap too (a < b when b is ahead of a, the serial number rule, RFC 1982). So the comparisons are not a total order, and the type is not
//* IComparable. The From... factories throw OverflowException for a value outside the range; nothing else throws or allocates.
//* The C++ core's TickCount64, member for member.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing
{
  public readonly struct TickCount64 : IEquatable<TickCount64>
  {
    public const long NanosecondsPerTick = 100;
    public const long TicksPerMicrosecond = 10;
    public const long TicksPerMillisecond = TimeSpan.TicksPerMillisecond;
    public const long TicksPerSecond = TimeSpan.TicksPerSecond;
    public const long TicksPerMinute = TimeSpan.TicksPerMinute;
    public const long TicksPerHour = TimeSpan.TicksPerHour;
    public const long TicksPerDay = TimeSpan.TicksPerDay;

    // The range of each From... factory
    public const long MinDays = long.MinValue / TicksPerDay;
    public const long MaxDays = long.MaxValue / TicksPerDay;
    public const long MinHours = long.MinValue / TicksPerHour;
    public const long MaxHours = long.MaxValue / TicksPerHour;
    public const long MinMinutes = long.MinValue / TicksPerMinute;
    public const long MaxMinutes = long.MaxValue / TicksPerMinute;
    public const long MinSeconds = long.MinValue / TicksPerSecond;
    public const long MaxSeconds = long.MaxValue / TicksPerSecond;
    public const long MinMilliseconds = long.MinValue / TicksPerMillisecond;
    public const long MaxMilliseconds = long.MaxValue / TicksPerMillisecond;
    public const long MinMicroseconds = long.MinValue / TicksPerMicrosecond;
    public const long MaxMicroseconds = long.MaxValue / TicksPerMicrosecond;

    /// <summary>
    /// The fastest counter FromCounter takes (about 922 GHz, beyond any platform's clock): its rest times TicksPerSecond fits a long.
    /// </summary>
    public const long MaxCounterFrequency = long.MaxValue / TicksPerSecond;

    public TickCount64(long ticks)
    {
      UnsignedTicks = unchecked((ulong)ticks);
    }

    /// <summary>The point sinceEpoch after the clock's epoch.</summary>
    public TickCount64(TimeSpan sinceEpoch)
      : this(sinceEpoch.Ticks) { }

    private TickCount64(ulong unsignedTicks)
    {
      UnsignedTicks = unsignedTicks;
    }

    /// <summary>The count as stored.</summary>
    public readonly ulong UnsignedTicks;

    public static TickCount64 FromTicks(long ticks) => new TickCount64(ticks);

    /// <summary>The count as stored (UnsignedTicks).</summary>
    public static TickCount64 FromUnsignedTicks(ulong ticks) => new TickCount64(ticks);

    /// <summary>Throws OverflowException outside MinDays to MaxDays.</summary>
    public static TickCount64 FromDays(long days) => FromUnits(days, MinDays, MaxDays, TicksPerDay);

    /// <summary>Throws OverflowException outside MinHours to MaxHours.</summary>
    public static TickCount64 FromHours(long hours) => FromUnits(hours, MinHours, MaxHours, TicksPerHour);

    /// <summary>Throws OverflowException outside MinMinutes to MaxMinutes.</summary>
    public static TickCount64 FromMinutes(long minutes) => FromUnits(minutes, MinMinutes, MaxMinutes, TicksPerMinute);

    /// <summary>Throws OverflowException outside MinSeconds to MaxSeconds.</summary>
    public static TickCount64 FromSeconds(long seconds) => FromUnits(seconds, MinSeconds, MaxSeconds, TicksPerSecond);

    /// <summary>Throws OverflowException outside MinMilliseconds to MaxMilliseconds.</summary>
    public static TickCount64 FromMilliseconds(long milliseconds) => FromUnits(milliseconds, MinMilliseconds, MaxMilliseconds, TicksPerMillisecond);

    /// <summary>Throws OverflowException outside MinMicroseconds to MaxMicroseconds.</summary>
    public static TickCount64 FromMicroseconds(long microseconds) => FromUnits(microseconds, MinMicroseconds, MaxMicroseconds, TicksPerMicrosecond);

    /// <summary>
    /// Nanoseconds (CLOCK_MONOTONIC, Vulkan and EGL present times, Choreographer) rounded down to the tick they are in.
    /// </summary>
    public static TickCount64 FromNanoseconds(long nanoseconds)
    {
      long ticks = nanoseconds / NanosecondsPerTick;
      return new TickCount64((nanoseconds % NanosecondsPerTick) < 0 ? ticks - 1 : ticks);
    }

    /// <summary>
    /// A counter value of a clock that counts frequency times a second (Stopwatch.GetTimestamp with Stopwatch.Frequency,
    /// QueryPerformanceCounter), rounded down to the tick it is in. Exact for any counter value: the whole seconds and the rest are
    /// converted apart, so nothing overflows. Throws ArgumentOutOfRangeException for a frequency that is not 1 to MaxCounterFrequency.
    /// </summary>
    public static TickCount64 FromCounter(long counter, long frequency)
    {
      if (frequency <= 0 || frequency > MaxCounterFrequency)
      {
        throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "The counter frequency must be 1 to MaxCounterFrequency");
      }
      long seconds = counter / frequency;
      long rest = counter % frequency;
      if (rest < 0)
      {
        --seconds;
        rest += frequency;
      }
      // rest < frequency <= MaxCounterFrequency, so rest * TicksPerSecond fits
      return new TickCount64(unchecked((seconds * TicksPerSecond) + ((rest * TicksPerSecond) / frequency)));
    }

    /// <summary>The count as a signed number of ticks.</summary>
    public long Ticks => unchecked((long)UnsignedTicks);

    /// <summary>The whole days of Ticks. Every component is truncated toward zero and has the sign of Ticks.</summary>
    public long Days => Ticks / TicksPerDay;

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

    /// <summary>The time since the clock's epoch.</summary>
    public TimeSpan ToTimeSpan() => new TimeSpan(Ticks);

    /// <summary>Wraps around.</summary>
    public static TickCount64 operator +(TickCount64 count, TimeSpan span) => new TickCount64(unchecked(count.UnsignedTicks + (ulong)span.Ticks));

    /// <summary>Wraps around.</summary>
    public static TickCount64 operator -(TickCount64 count, TimeSpan span) => new TickCount64(unchecked(count.UnsignedTicks - (ulong)span.Ticks));

    /// <summary>The time from right to left, the shorter way round: correct while the counts are less than 2^63 ticks apart.</summary>
    public static TimeSpan operator -(TickCount64 left, TickCount64 right) => new TimeSpan(Distance(left, right));

    public bool Equals(TickCount64 other) => UnsignedTicks == other.UnsignedTicks;

    public override bool Equals(object obj) => obj is TickCount64 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(UnsignedTicks);

    public static bool operator ==(TickCount64 left, TickCount64 right) => left.Equals(right);

    public static bool operator !=(TickCount64 left, TickCount64 right) => !left.Equals(right);

    /// <summary>left is before right: the wrap-safe order of TickCount32, for counts less than 2^63 ticks apart.</summary>
    public static bool operator <(TickCount64 left, TickCount64 right) => Distance(left, right) < 0;

    public static bool operator <=(TickCount64 left, TickCount64 right) => Distance(left, right) <= 0;

    public static bool operator >(TickCount64 left, TickCount64 right) => Distance(left, right) > 0;

    public static bool operator >=(TickCount64 left, TickCount64 right) => Distance(left, right) >= 0;

    /// <summary>The time since the clock's epoch as TimeSpan writes it (culture invariant).</summary>
    public override string ToString() => ToTimeSpan().ToString();

    /// <summary>
    /// The ticks from right to left as a signed 64-bit number: the unsigned difference wraps, its signed view is the shorter way round.
    /// </summary>
    private static long Distance(TickCount64 left, TickCount64 right) => unchecked((long)(left.UnsignedTicks - right.UnsignedTicks));

    private static TickCount64 FromUnits(long value, long minValue, long maxValue, long ticksPerUnit)
    {
      if (value < minValue || value > maxValue)
      {
        throw new OverflowException("The value is outside the range of a TickCount64");
      }
      return new TickCount64(value * ticksPerUnit);
    }
  }
}
