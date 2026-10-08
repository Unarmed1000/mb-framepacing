//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A length of time that is never negative, in ticks of 100 ns: a System.TimeSpan that has been shown to be zero or more (a frame's work, a
//* refresh period, a time something was counted over). Always valid: the constructor makes a negative span zero. It widens to a TimeSpan
//* by itself; the way back is the constructor, which is where the clamp is.
//*
//* What a result is follows from what it can be: two durations added are a duration, one minus another is a TimeSpan (it can be negative),
//* and a duration times or divided by an unsigned count is a duration. Arithmetic is in whole ticks (a division truncates) and overflows as
//* TimeSpan's own does, with an OverflowException. Nothing here allocates.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Diagnostics;

namespace MB.FramePacing
{
  public readonly struct TimeDuration : IEquatable<TimeDuration>, IComparable<TimeDuration>
  {
    public static readonly TimeDuration Zero = default;

    public static readonly TimeDuration MaxValue = new TimeDuration(TimeSpan.MaxValue);

    /// <summary>The span as a duration. A negative span becomes zero.</summary>
    public TimeDuration(TimeSpan value)
    {
      Value = value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }

    // The span as it is: the caller has shown that it is not negative (the flag only picks this constructor)
    private TimeDuration(TimeSpan value, bool isNotNegative)
    {
      Debug.Assert(isNotNegative && value >= TimeSpan.Zero, "A duration is never negative");
      Value = value;
    }

    /// <summary>Never negative.</summary>
    public readonly TimeSpan Value;

    public long Ticks => Value.Ticks;

    /// <summary>The span as a duration, without the check: for a span the caller knows is not negative (asserted in debug builds).</summary>
    public static TimeDuration UncheckedCreate(TimeSpan value) => new TimeDuration(value, true);

    /// <summary>A negative count becomes zero.</summary>
    public static TimeDuration FromTicks(long ticks) => new TimeDuration(new TimeSpan(ticks));

    /// <summary>A 32-bit span in ticks, which is never negative, as a duration.</summary>
    public static TimeDuration From(TimeSpan32 value) => new TimeDuration(value.ToTimeSpan(), true);

    /// <summary>A duration is a TimeSpan that is not negative, so it is one wherever a TimeSpan is asked for.</summary>
    public static implicit operator TimeSpan(TimeDuration value) => value.Value;

    public static TimeDuration operator +(TimeDuration left, TimeDuration right) => new TimeDuration(left.Value + right.Value, true);

    /// <summary>The difference can be negative, so it is a TimeSpan.</summary>
    public static TimeSpan operator -(TimeDuration left, TimeDuration right) => left.Value - right.Value;

    /// <summary>A count is never negative, so the result is a duration.</summary>
    public static TimeDuration operator *(TimeDuration left, uint right) => new TimeDuration(new TimeSpan(checked(left.Value.Ticks * right)), true);

    /// <summary>In whole ticks, truncated. Throws DivideByZeroException for a count of zero.</summary>
    public static TimeDuration operator /(TimeDuration left, uint right) => new TimeDuration(new TimeSpan(left.Value.Ticks / right), true);

    public static TimeDuration Min(TimeDuration first, TimeDuration second) => first <= second ? first : second;

    public static TimeDuration Max(TimeDuration first, TimeDuration second) => first >= second ? first : second;

    public bool Equals(TimeDuration other) => Value == other.Value;

    public override bool Equals(object obj) => obj is TimeDuration other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Value);

    public int CompareTo(TimeDuration other) => Value.CompareTo(other.Value);

    public static bool operator ==(TimeDuration left, TimeDuration right) => left.Equals(right);

    public static bool operator !=(TimeDuration left, TimeDuration right) => !left.Equals(right);

    public static bool operator <(TimeDuration left, TimeDuration right) => left.Value < right.Value;

    public static bool operator <=(TimeDuration left, TimeDuration right) => left.Value <= right.Value;

    public static bool operator >(TimeDuration left, TimeDuration right) => left.Value > right.Value;

    public static bool operator >=(TimeDuration left, TimeDuration right) => left.Value >= right.Value;

    /// <summary>The duration as TimeSpan writes it (culture invariant): "00:00:00.0166667".</summary>
    public override string ToString() => Value.ToString();
  }
}
