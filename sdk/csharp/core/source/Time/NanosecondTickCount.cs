//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A point on a clock that counts in nanoseconds, any epoch (the same clock for the whole run): what a platform that counts in
//* nanoseconds reports (CLOCK_MONOTONIC, Vulkan and EGL present times, Choreographer), kept as it is given. A TickCount64 counts in ticks
//* of 100 ns; ToTickCount64 is the tick a point is in. The SDK never reads a clock; the application fills it from its own. The count is
//* stored unsigned, so adding and subtracting wrap around; Nanoseconds is its signed view.
//*
//* Wrap-around safe, as TickCount64: two counts compare and subtract correctly while they are less than 2^63 nanoseconds apart (about 292
//* years), across the wrap too. So the comparisons are not a total order, and the type is not IComparable. The From... factories throw
//* OverflowException for a value outside the range; nothing else throws or allocates. The C++ core's NanosecondTickCount, member for
//* member.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Globalization;

namespace MB.FramePacing
{
  public readonly struct NanosecondTickCount : IEquatable<NanosecondTickCount>
  {
    public const long NanosecondsPerTick = NanosecondTimeSpan.NanosecondsPerTick;
    public const long NanosecondsPerMicrosecond = NanosecondTimeSpan.NanosecondsPerMicrosecond;
    public const long NanosecondsPerMillisecond = NanosecondTimeSpan.NanosecondsPerMillisecond;
    public const long NanosecondsPerSecond = NanosecondTimeSpan.NanosecondsPerSecond;

    // The range of each From... factory
    public const long MinSeconds = long.MinValue / NanosecondsPerSecond;
    public const long MaxSeconds = long.MaxValue / NanosecondsPerSecond;
    public const long MinMilliseconds = long.MinValue / NanosecondsPerMillisecond;
    public const long MaxMilliseconds = long.MaxValue / NanosecondsPerMillisecond;
    public const long MinMicroseconds = long.MinValue / NanosecondsPerMicrosecond;
    public const long MaxMicroseconds = long.MaxValue / NanosecondsPerMicrosecond;
    public const long MinTicks = long.MinValue / NanosecondsPerTick;
    public const long MaxTicks = long.MaxValue / NanosecondsPerTick;

    public NanosecondTickCount(long nanoseconds)
    {
      UnsignedNanoseconds = unchecked((ulong)nanoseconds);
    }

    /// <summary>The point sinceEpoch after the clock's epoch.</summary>
    public NanosecondTickCount(NanosecondTimeSpan sinceEpoch)
      : this(sinceEpoch.Nanoseconds) { }

    private NanosecondTickCount(ulong unsignedNanoseconds)
    {
      UnsignedNanoseconds = unsignedNanoseconds;
    }

    /// <summary>The count as stored.</summary>
    public readonly ulong UnsignedNanoseconds;

    public static NanosecondTickCount FromNanoseconds(long nanoseconds) => new NanosecondTickCount(nanoseconds);

    /// <summary>The count as stored (UnsignedNanoseconds).</summary>
    public static NanosecondTickCount FromUnsignedNanoseconds(ulong nanoseconds) => new NanosecondTickCount(nanoseconds);

    /// <summary>Throws OverflowException outside MinSeconds to MaxSeconds.</summary>
    public static NanosecondTickCount FromSeconds(long seconds) => FromUnits(seconds, MinSeconds, MaxSeconds, NanosecondsPerSecond);

    /// <summary>Throws OverflowException outside MinMilliseconds to MaxMilliseconds.</summary>
    public static NanosecondTickCount FromMilliseconds(long milliseconds) =>
      FromUnits(milliseconds, MinMilliseconds, MaxMilliseconds, NanosecondsPerMillisecond);

    /// <summary>Throws OverflowException outside MinMicroseconds to MaxMicroseconds.</summary>
    public static NanosecondTickCount FromMicroseconds(long microseconds) =>
      FromUnits(microseconds, MinMicroseconds, MaxMicroseconds, NanosecondsPerMicrosecond);

    /// <summary>
    /// A point in ticks of 100 ns, exactly. Throws OverflowException when its ticks are outside MinTicks to MaxTicks (a TickCount64 reaches
    /// a hundred times as far).
    /// </summary>
    public static NanosecondTickCount FromTickCount64(TickCount64 count) => FromUnits(count.Ticks, MinTicks, MaxTicks, NanosecondsPerTick);

    /// <summary>The count as a signed number of nanoseconds.</summary>
    public long Nanoseconds => unchecked((long)UnsignedNanoseconds);

    /// <summary>The tick of 100 ns the point is in: rounded down, as TickCount64.FromNanoseconds.</summary>
    public TickCount64 ToTickCount64() => TickCount64.FromNanoseconds(Nanoseconds);

    /// <summary>The time since the clock's epoch.</summary>
    public NanosecondTimeSpan ToNanosecondTimeSpan() => new NanosecondTimeSpan(Nanoseconds);

    public double TotalMicroseconds => (double)Nanoseconds / NanosecondsPerMicrosecond;

    public double TotalMilliseconds => (double)Nanoseconds / NanosecondsPerMillisecond;

    public double TotalSeconds => (double)Nanoseconds / NanosecondsPerSecond;

    /// <summary>Wraps around.</summary>
    public static NanosecondTickCount operator +(NanosecondTickCount count, NanosecondTimeSpan span) =>
      new NanosecondTickCount(unchecked(count.UnsignedNanoseconds + (ulong)span.Nanoseconds));

    /// <summary>Wraps around.</summary>
    public static NanosecondTickCount operator -(NanosecondTickCount count, NanosecondTimeSpan span) =>
      new NanosecondTickCount(unchecked(count.UnsignedNanoseconds - (ulong)span.Nanoseconds));

    /// <summary>
    /// The time from right to left, the shorter way round: correct while the counts are less than 2^63 nanoseconds apart.
    /// </summary>
    public static NanosecondTimeSpan operator -(NanosecondTickCount left, NanosecondTickCount right) => new NanosecondTimeSpan(Distance(left, right));

    public bool Equals(NanosecondTickCount other) => UnsignedNanoseconds == other.UnsignedNanoseconds;

    public override bool Equals(object obj) => obj is NanosecondTickCount other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(UnsignedNanoseconds);

    public static bool operator ==(NanosecondTickCount left, NanosecondTickCount right) => left.Equals(right);

    public static bool operator !=(NanosecondTickCount left, NanosecondTickCount right) => !left.Equals(right);

    /// <summary>left is before right: the wrap-safe order of TickCount64, for counts less than 2^63 nanoseconds apart.</summary>
    public static bool operator <(NanosecondTickCount left, NanosecondTickCount right) => Distance(left, right) < 0;

    public static bool operator <=(NanosecondTickCount left, NanosecondTickCount right) => Distance(left, right) <= 0;

    public static bool operator >(NanosecondTickCount left, NanosecondTickCount right) => Distance(left, right) > 0;

    public static bool operator >=(NanosecondTickCount left, NanosecondTickCount right) => Distance(left, right) >= 0;

    /// <summary>The signed count of nanoseconds and its unit (culture invariant): "4166389 ns".</summary>
    public override string ToString() => Nanoseconds.ToString(CultureInfo.InvariantCulture) + " ns";

    /// <summary>
    /// The nanoseconds from right to left as a signed 64-bit number: the unsigned difference wraps, its signed view is the shorter way
    /// round.
    /// </summary>
    private static long Distance(NanosecondTickCount left, NanosecondTickCount right) =>
      unchecked((long)(left.UnsignedNanoseconds - right.UnsignedNanoseconds));

    private static NanosecondTickCount FromUnits(long value, long minValue, long maxValue, long nanosecondsPerUnit)
    {
      if (value < minValue || value > maxValue)
      {
        throw new OverflowException("The value is outside the range of a NanosecondTickCount");
      }
      return new NanosecondTickCount(value * nanosecondsPerUnit);
    }
  }
}
