//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A time interval of 0 to 429.4967295 s in ticks of 100 ns, unsigned 32-bit: an interval as four bytes of a file or a message hold it in
//* ticks (the tools' data still does; the marker's own durations are nanoseconds, a NanosecondTimeDuration). It has no arithmetic: compute
//* with TimeSpan and convert the result. The C++ core's TimeSpan32, member for member.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing
{
  public readonly struct TimeSpan32 : IEquatable<TimeSpan32>, IComparable<TimeSpan32>
  {
    public static readonly TimeSpan32 Zero = default;

    public static readonly TimeSpan32 MaxValue = new TimeSpan32(uint.MaxValue);

    public TimeSpan32(uint ticks)
    {
      Ticks = ticks;
    }

    public readonly uint Ticks;

    public static TimeSpan32 FromTicks(uint ticks) => new TimeSpan32(ticks);

    /// <summary>The span exactly. Throws ArgumentOutOfRangeException for a negative span or one longer than MaxValue.</summary>
    public static TimeSpan32 FromTimeSpan(TimeSpan span)
    {
      if (span.Ticks < 0 || span.Ticks > uint.MaxValue)
      {
        throw new ArgumentOutOfRangeException(nameof(span), span, "A TimeSpan32 is 0 to 429.4967295 s");
      }
      return new TimeSpan32((uint)span.Ticks);
    }

    public TimeSpan ToTimeSpan() => new TimeSpan(Ticks);

    public bool Equals(TimeSpan32 other) => Ticks == other.Ticks;

    public override bool Equals(object obj) => obj is TimeSpan32 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Ticks);

    public int CompareTo(TimeSpan32 other) => Ticks.CompareTo(other.Ticks);

    public static bool operator ==(TimeSpan32 left, TimeSpan32 right) => left.Equals(right);

    public static bool operator !=(TimeSpan32 left, TimeSpan32 right) => !left.Equals(right);

    public static bool operator <(TimeSpan32 left, TimeSpan32 right) => left.Ticks < right.Ticks;

    public static bool operator <=(TimeSpan32 left, TimeSpan32 right) => left.Ticks <= right.Ticks;

    public static bool operator >(TimeSpan32 left, TimeSpan32 right) => left.Ticks > right.Ticks;

    public static bool operator >=(TimeSpan32 left, TimeSpan32 right) => left.Ticks >= right.Ticks;

    /// <summary>The span as TimeSpan writes it (culture invariant): "00:00:00.0166667".</summary>
    public override string ToString() => ToTimeSpan().ToString();
  }
}
