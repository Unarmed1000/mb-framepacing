//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A time interval of 0 to 4.294967295 s in nanoseconds, unsigned 32-bit: a 32-bit field that holds an interval to the nanosecond, as
//* TimeSpan32 is one that holds it in ticks of 100 ns. It says what a value becomes when it is put into four bytes: the conversion from a
//* NanosecondTimeSpan is exact or it throws, never a silent cut. It has no arithmetic: compute with NanosecondTimeSpan and convert the
//* result. The C++ core's NanosecondTimeSpan32, member for member.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Globalization;

namespace MB.FramePacing
{
  public readonly struct NanosecondTimeSpan32 : IEquatable<NanosecondTimeSpan32>, IComparable<NanosecondTimeSpan32>
  {
    public static readonly NanosecondTimeSpan32 Zero = default;

    public static readonly NanosecondTimeSpan32 MaxValue = new NanosecondTimeSpan32(uint.MaxValue);

    public NanosecondTimeSpan32(uint nanoseconds)
    {
      Nanoseconds = nanoseconds;
    }

    public readonly uint Nanoseconds;

    public static NanosecondTimeSpan32 FromNanoseconds(uint nanoseconds) => new NanosecondTimeSpan32(nanoseconds);

    /// <summary>The span exactly. Throws ArgumentOutOfRangeException for a negative span or one longer than MaxValue.</summary>
    public static NanosecondTimeSpan32 FromNanosecondTimeSpan(NanosecondTimeSpan span)
    {
      if (span.Nanoseconds < 0 || span.Nanoseconds > uint.MaxValue)
      {
        throw new ArgumentOutOfRangeException(nameof(span), span, "A NanosecondTimeSpan32 is 0 to 4.294967295 s");
      }
      return new NanosecondTimeSpan32((uint)span.Nanoseconds);
    }

    /// <summary>
    /// A span in ticks of 100 ns, exactly. Throws ArgumentOutOfRangeException for a negative span or one longer than MaxValue (42,949,672
    /// ticks is the longest).
    /// </summary>
    public static NanosecondTimeSpan32 FromTimeSpan(TimeSpan span)
    {
      if (span.Ticks < 0 || span.Ticks > uint.MaxValue / NanosecondTimeSpan.NanosecondsPerTick)
      {
        throw new ArgumentOutOfRangeException(nameof(span), span, "A NanosecondTimeSpan32 is 0 to 4.294967295 s");
      }
      return new NanosecondTimeSpan32((uint)(span.Ticks * NanosecondTimeSpan.NanosecondsPerTick));
    }

    public NanosecondTimeSpan ToNanosecondTimeSpan() => new NanosecondTimeSpan(Nanoseconds);

    /// <summary>The span in ticks of 100 ns, truncated to a tick, as NanosecondTimeSpan's.</summary>
    public TimeSpan ToTimeSpan() => ToNanosecondTimeSpan().ToTimeSpan();

    public bool Equals(NanosecondTimeSpan32 other) => Nanoseconds == other.Nanoseconds;

    public override bool Equals(object obj) => obj is NanosecondTimeSpan32 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Nanoseconds);

    public int CompareTo(NanosecondTimeSpan32 other) => Nanoseconds.CompareTo(other.Nanoseconds);

    public static bool operator ==(NanosecondTimeSpan32 left, NanosecondTimeSpan32 right) => left.Equals(right);

    public static bool operator !=(NanosecondTimeSpan32 left, NanosecondTimeSpan32 right) => !left.Equals(right);

    public static bool operator <(NanosecondTimeSpan32 left, NanosecondTimeSpan32 right) => left.Nanoseconds < right.Nanoseconds;

    public static bool operator <=(NanosecondTimeSpan32 left, NanosecondTimeSpan32 right) => left.Nanoseconds <= right.Nanoseconds;

    public static bool operator >(NanosecondTimeSpan32 left, NanosecondTimeSpan32 right) => left.Nanoseconds > right.Nanoseconds;

    public static bool operator >=(NanosecondTimeSpan32 left, NanosecondTimeSpan32 right) => left.Nanoseconds >= right.Nanoseconds;

    /// <summary>The count of nanoseconds and its unit (culture invariant): "4166389 ns".</summary>
    public override string ToString() => Nanoseconds.ToString(CultureInfo.InvariantCulture) + " ns";
  }
}
