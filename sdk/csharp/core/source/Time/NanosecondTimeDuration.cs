//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A length of time that is never negative, in nanoseconds: a NanosecondTimeSpan that has been shown to be zero or more (how long something
//* took, a refresh period, the span from a begin to an end that can not be before it). Always valid: the constructor makes a negative span
//* zero. It widens to a NanosecondTimeSpan by itself; the way back is the constructor, which is where the clamp is. It is to nanoseconds
//* what TimeDuration is to ticks of 100 ns: exact from one, and to one truncated to the tick.
//*
//* What a result is follows from what it can be: two durations added are a duration, one minus another is a NanosecondTimeSpan (it can be
//* negative), and so is a duration with a NanosecondTimeSpan added or taken away, either way round (NanosecondTimeSpan's own operators take
//* a duration). Out of range throws as NanosecondTimeSpan does: OverflowException from the arithmetic, ArgumentOutOfRangeException from
//* FromTimeDuration. Nothing here allocates. The C++ core's NanosecondTimeDuration.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Diagnostics;

namespace MB.FramePacing
{
  public readonly struct NanosecondTimeDuration : IEquatable<NanosecondTimeDuration>, IComparable<NanosecondTimeDuration>
  {
    public static readonly NanosecondTimeDuration Zero = default;

    public static readonly NanosecondTimeDuration MaxValue = new NanosecondTimeDuration(NanosecondTimeSpan.MaxValue);

    /// <summary>The span as a duration. A negative span becomes zero.</summary>
    public NanosecondTimeDuration(NanosecondTimeSpan value)
    {
      Value = value < NanosecondTimeSpan.Zero ? NanosecondTimeSpan.Zero : value;
    }

    // The span as it is: the caller has shown that it is not negative (the flag only picks this constructor)
    private NanosecondTimeDuration(NanosecondTimeSpan value, bool isNotNegative)
    {
      Debug.Assert(isNotNegative && value >= NanosecondTimeSpan.Zero, "A duration is never negative");
      Value = value;
    }

    /// <summary>Never negative.</summary>
    public readonly NanosecondTimeSpan Value;

    public long Nanoseconds => Value.Nanoseconds;

    /// <summary>The count of nanoseconds as an unsigned number, which a count that is never negative always is.</summary>
    public ulong UnsignedNanoseconds => (ulong)Value.Nanoseconds;

    /// <summary>The span as a duration, without the check: for a span the caller knows is not negative (asserted in debug builds).</summary>
    public static NanosecondTimeDuration UncheckedCreate(NanosecondTimeSpan value) => new NanosecondTimeDuration(value, true);

    /// <summary>A negative count becomes zero.</summary>
    public static NanosecondTimeDuration FromNanoseconds(long nanoseconds) => new NanosecondTimeDuration(new NanosecondTimeSpan(nanoseconds));

    /// <summary>
    /// A duration in ticks of 100 ns, exactly. Throws ArgumentOutOfRangeException for a duration of more than 292 years, which nanoseconds
    /// can not hold (a TimeDuration reaches a hundred times as far).
    /// </summary>
    public static NanosecondTimeDuration FromTimeDuration(TimeDuration duration)
    {
      if (duration.Ticks > long.MaxValue / NanosecondTimeSpan.NanosecondsPerTick)
      {
        throw new ArgumentOutOfRangeException(nameof(duration), duration.Ticks, "The value is outside the range of a NanosecondTimeDuration");
      }
      return new NanosecondTimeDuration(new NanosecondTimeSpan(duration.Ticks * NanosecondTimeSpan.NanosecondsPerTick), true);
    }

    /// <summary>The duration in ticks of 100 ns, truncated to the tick.</summary>
    public TimeDuration ToTimeDuration() => TimeDuration.UncheckedCreate(Value.ToTimeSpan());

    /// <summary>A duration is a NanosecondTimeSpan that is not negative, so it is one wherever a NanosecondTimeSpan is asked for.</summary>
    public static implicit operator NanosecondTimeSpan(NanosecondTimeDuration value) => value.Value;

    /// <summary>Throws OverflowException if the sum is outside the range.</summary>
    public static NanosecondTimeDuration operator +(NanosecondTimeDuration left, NanosecondTimeDuration right) =>
      new NanosecondTimeDuration(left.Value + right.Value, true);

    /// <summary>The difference can be negative, so it is a NanosecondTimeSpan.</summary>
    public static NanosecondTimeSpan operator -(NanosecondTimeDuration left, NanosecondTimeDuration right) => left.Value - right.Value;

    public static NanosecondTimeDuration Min(NanosecondTimeDuration first, NanosecondTimeDuration second) => first <= second ? first : second;

    public static NanosecondTimeDuration Max(NanosecondTimeDuration first, NanosecondTimeDuration second) => first >= second ? first : second;

    public bool Equals(NanosecondTimeDuration other) => Value == other.Value;

    public override bool Equals(object obj) => obj is NanosecondTimeDuration other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Value);

    public int CompareTo(NanosecondTimeDuration other) => Value.CompareTo(other.Value);

    public static bool operator ==(NanosecondTimeDuration left, NanosecondTimeDuration right) => left.Equals(right);

    public static bool operator !=(NanosecondTimeDuration left, NanosecondTimeDuration right) => !left.Equals(right);

    public static bool operator <(NanosecondTimeDuration left, NanosecondTimeDuration right) => left.Value < right.Value;

    public static bool operator <=(NanosecondTimeDuration left, NanosecondTimeDuration right) => left.Value <= right.Value;

    public static bool operator >(NanosecondTimeDuration left, NanosecondTimeDuration right) => left.Value > right.Value;

    public static bool operator >=(NanosecondTimeDuration left, NanosecondTimeDuration right) => left.Value >= right.Value;

    /// <summary>The count of nanoseconds and its unit, as NanosecondTimeSpan writes it (culture invariant): "4166389 ns".</summary>
    public override string ToString() => Value.ToString();
  }
}
