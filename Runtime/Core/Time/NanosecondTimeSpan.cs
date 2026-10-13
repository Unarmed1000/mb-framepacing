//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A signed time interval in nanoseconds: what a platform that counts in nanoseconds reports (a refresh period, a present's duration, the
//* time between two display times), kept as it is given. A System.TimeSpan counts in ticks of 100 ns, so a value that goes through it
//* loses up to 99 ns: a refresh period of 4,166,389 ns is 41,663 ticks, 21 parts in a million short. About 292 years either way.
//*
//* Out of range throws, as TimeSpan does: ArgumentOutOfRangeException from the factories, OverflowException from the arithmetic. Seconds
//* as a double throw as TimeSpanUtil.FromSeconds does: OverflowException outside the range, ArgumentException for NaN. Nothing here
//* allocates. The C++ core's NanosecondTimeSpan, member for member.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Globalization;

namespace MB.FramePacing
{
  public readonly struct NanosecondTimeSpan : IEquatable<NanosecondTimeSpan>, IComparable<NanosecondTimeSpan>
  {
    public const long NanosecondsPerTick = 100;
    public const long NanosecondsPerMicrosecond = 1_000;
    public const long NanosecondsPerMillisecond = 1_000_000;
    public const long NanosecondsPerSecond = 1_000_000_000;

    // 2^63: what MaxValue's nanoseconds round to as a double
    private const double NanosecondLimit = 9_223_372_036_854_775_808.0;

    public static readonly NanosecondTimeSpan Zero = default;

    public static readonly NanosecondTimeSpan MinValue = new NanosecondTimeSpan(long.MinValue);

    public static readonly NanosecondTimeSpan MaxValue = new NanosecondTimeSpan(long.MaxValue);

    public NanosecondTimeSpan(long nanoseconds)
    {
      Nanoseconds = nanoseconds;
    }

    public readonly long Nanoseconds;

    public static NanosecondTimeSpan FromNanoseconds(long nanoseconds) => new NanosecondTimeSpan(nanoseconds);

    /// <summary>A whole number of microseconds. Throws ArgumentOutOfRangeException if it is outside the range.</summary>
    public static NanosecondTimeSpan FromMicroseconds(long microseconds) => FromUnits(microseconds, NanosecondsPerMicrosecond, nameof(microseconds));

    /// <summary>A whole number of milliseconds. Throws ArgumentOutOfRangeException if it is outside the range.</summary>
    public static NanosecondTimeSpan FromMilliseconds(long milliseconds) => FromUnits(milliseconds, NanosecondsPerMillisecond, nameof(milliseconds));

    /// <summary>A whole number of seconds. Throws ArgumentOutOfRangeException if it is outside the range.</summary>
    public static NanosecondTimeSpan FromSeconds(long seconds) => FromUnits(seconds, NanosecondsPerSecond, nameof(seconds));

    /// <summary>
    /// Seconds (an animation clock's time, for example) truncated toward zero to a nanosecond: 1.0 / 60 is 16 666 666 ns. Throws
    /// ArgumentException for NaN and OverflowException outside the range, as TimeSpanUtil.FromSeconds does.
    /// </summary>
    public static NanosecondTimeSpan FromSeconds(double seconds)
    {
      if (double.IsNaN(seconds))
      {
        throw new ArgumentException("The value is NaN", nameof(seconds));
      }
      double nanoseconds = seconds * NanosecondsPerSecond;
      if (nanoseconds < -NanosecondLimit || nanoseconds > NanosecondLimit)
      {
        throw new OverflowException("The value is outside the range of a NanosecondTimeSpan");
      }
      return nanoseconds == NanosecondLimit ? MaxValue : new NanosecondTimeSpan((long)nanoseconds);
    }

    /// <summary>
    /// A span in ticks of 100 ns, exactly. Throws ArgumentOutOfRangeException if it is outside the range (a TimeSpan reaches a hundred
    /// times as far).
    /// </summary>
    public static NanosecondTimeSpan FromTimeSpan(TimeSpan span) => FromUnits(span.Ticks, NanosecondsPerTick, nameof(span));

    /// <summary>The span in ticks of 100 ns, truncated toward zero to a tick, as every conversion to a TimeSpan is.</summary>
    public TimeSpan ToTimeSpan() => new TimeSpan(Nanoseconds / NanosecondsPerTick);

    public double TotalMicroseconds => (double)Nanoseconds / NanosecondsPerMicrosecond;

    public double TotalMilliseconds => (double)Nanoseconds / NanosecondsPerMillisecond;

    public double TotalSeconds => (double)Nanoseconds / NanosecondsPerSecond;

    /// <summary>The span with the other sign. Throws OverflowException for MinValue, which has no positive counterpart.</summary>
    public NanosecondTimeSpan Negate() => new NanosecondTimeSpan(checked(-Nanoseconds));

    /// <summary>The absolute value. Throws OverflowException for MinValue, which has no positive counterpart.</summary>
    public NanosecondTimeSpan Duration() => Nanoseconds < 0 ? Negate() : this;

    public static NanosecondTimeSpan operator +(NanosecondTimeSpan span) => span;

    /// <summary>Throws OverflowException for MinValue.</summary>
    public static NanosecondTimeSpan operator -(NanosecondTimeSpan span) => span.Negate();

    /// <summary>Throws OverflowException if the sum is outside the range.</summary>
    public static NanosecondTimeSpan operator +(NanosecondTimeSpan left, NanosecondTimeSpan right) =>
      new NanosecondTimeSpan(checked(left.Nanoseconds + right.Nanoseconds));

    /// <summary>Throws OverflowException if the difference is outside the range.</summary>
    public static NanosecondTimeSpan operator -(NanosecondTimeSpan left, NanosecondTimeSpan right) =>
      new NanosecondTimeSpan(checked(left.Nanoseconds - right.Nanoseconds));

    public bool Equals(NanosecondTimeSpan other) => Nanoseconds == other.Nanoseconds;

    public override bool Equals(object obj) => obj is NanosecondTimeSpan other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Nanoseconds);

    public int CompareTo(NanosecondTimeSpan other) => Nanoseconds.CompareTo(other.Nanoseconds);

    public static bool operator ==(NanosecondTimeSpan left, NanosecondTimeSpan right) => left.Equals(right);

    public static bool operator !=(NanosecondTimeSpan left, NanosecondTimeSpan right) => !left.Equals(right);

    public static bool operator <(NanosecondTimeSpan left, NanosecondTimeSpan right) => left.Nanoseconds < right.Nanoseconds;

    public static bool operator <=(NanosecondTimeSpan left, NanosecondTimeSpan right) => left.Nanoseconds <= right.Nanoseconds;

    public static bool operator >(NanosecondTimeSpan left, NanosecondTimeSpan right) => left.Nanoseconds > right.Nanoseconds;

    public static bool operator >=(NanosecondTimeSpan left, NanosecondTimeSpan right) => left.Nanoseconds >= right.Nanoseconds;

    /// <summary>The count of nanoseconds and its unit (culture invariant): "4166389 ns".</summary>
    public override string ToString() => Nanoseconds.ToString(CultureInfo.InvariantCulture) + " ns";

    private static NanosecondTimeSpan FromUnits(long value, long nanosecondsPerUnit, string name)
    {
      if (value < long.MinValue / nanosecondsPerUnit || value > long.MaxValue / nanosecondsPerUnit)
      {
        throw new ArgumentOutOfRangeException(name, value, "The value is outside the range of a NanosecondTimeSpan");
      }
      return new NanosecondTimeSpan(value * nanosecondsPerUnit);
    }
  }
}
