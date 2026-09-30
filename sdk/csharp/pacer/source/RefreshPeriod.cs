//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The display's refresh period, exact to 2^-32 of a tick (100 ns). A period in whole ticks would drift: 60 Hz is 166'666.67 ticks, and
//* rounding it to 166'667 adds 2 µs a second. TicksFor computes the time of any number of refreshes from the exact period, so a grid of refreshes
//* stays on the rate it was made from.
//*
//* Always valid when made by its factories: from MinTicksQ32 (1 tick, a 10 MHz rate) to MaxTicksQ32 (1 s, 1 Hz); a period outside is clamped into
//* the range. default(RefreshPeriod) is its one invalid value (IsDefault): the pacer's types refuse it, since only the application knows its
//* display's period. The same arithmetic as the C++ library, to the 2^-32 tick.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Pacer
{
  public readonly struct RefreshPeriod : IEquatable<RefreshPeriod>
  {
    /// <summary>One tick in the fixed point of <see cref="TicksQ32"/>.</summary>
    public const long OneTickQ32 = 1L << 32;

    /// <summary>The shortest period: 1 tick (100 ns).</summary>
    public const long MinTicksQ32 = OneTickQ32;

    /// <summary>The longest period: 1 s.</summary>
    public const long MaxTicksQ32 = TimeSpan.TicksPerSecond * OneTickQ32;

    /// <summary>The period in ticks times 2^32; 0 only for default(RefreshPeriod).</summary>
    public readonly long TicksQ32;

    private RefreshPeriod(long ticksQ32)
    {
      TicksQ32 = Math.Clamp(ticksQ32, MinTicksQ32, MaxTicksQ32);
    }

    /// <summary>default(RefreshPeriod): not a period. The factories never make it.</summary>
    public bool IsDefault => TicksQ32 == 0;

    /// <summary>The period rounded to the nearest tick.</summary>
    public long Ticks => (TicksQ32 + (OneTickQ32 / 2)) >> 32;

    /// <summary>The period rounded to the nearest nanosecond: what a platform reports it as (<see cref="FrameInput.RefreshPeriodNanoseconds"/>).</summary>
    public long Nanoseconds => ((TicksQ32 * 100) + (OneTickQ32 / 2)) >> 32;

    /// <summary>
    /// A refresh rate of <paramref name="numerator"/> / <paramref name="denominator"/> Hz: a DXGI_RATIONAL (60000 / 1001), wl_output's mHz
    /// (59940 / 1000), a whole rate (60). From 1 Hz to 10 MHz; a numerator or denominator of 0 is outside.
    /// </summary>
    public static RefreshPeriod FromRate(uint numerator, uint denominator = 1)
    {
      if (numerator == 0)
        return new RefreshPeriod(MaxTicksQ32 + 1);
      // TicksPerSecond * denominator / numerator, the whole ticks and the rest apart so nothing overflows (a period beyond the range stops just
      // past it)
      ulong dividend = (ulong)TimeSpan.TicksPerSecond * denominator;
      ulong whole = Math.Min(dividend / numerator, (ulong)TimeSpan.TicksPerSecond + 1);
      ulong rest = dividend % numerator;
      ulong fraction = ((rest << 32) + (numerator / 2)) / numerator;
      return new RefreshPeriod((long)((whole << 32) + fraction));
    }

    /// <summary>A period in whole ticks: 1 to TimeSpan.TicksPerSecond.</summary>
    public static RefreshPeriod FromTicks(long ticks) => new RefreshPeriod(Math.Clamp(ticks, -1, TimeSpan.TicksPerSecond + 1) * OneTickQ32);

    /// <summary>
    /// A period in nanoseconds (Choreographer's vsync period, VK_GOOGLE_display_timing's refreshDuration), exact to 2^-32 tick: 100 ns to 1 s.
    /// </summary>
    public static RefreshPeriod FromNanoseconds(long nanoseconds)
    {
      long limited = Math.Clamp(nanoseconds, -1, (TimeSpan.TicksPerSecond * 100) + 1);
      long whole = limited / 100;
      long rest = limited % 100;
      return new RefreshPeriod((whole * OneTickQ32) + (((rest * OneTickQ32) + 50) / 100));
    }

    /// <summary>A period in ticks times 2^32 (<see cref="TicksQ32"/> of another period): MinTicksQ32 to MaxTicksQ32.</summary>
    public static RefreshPeriod FromTicksQ32(long ticksQ32) => new RefreshPeriod(ticksQ32);

    /// <summary><paramref name="refreshes"/> times the period, rounded to the nearest tick (half up). Exact for any refreshes up to 2^31.</summary>
    public long TicksFor(long refreshes)
    {
      if (refreshes < 0)
        return -TicksFor(-refreshes);
      long whole = TicksQ32 >> 32;
      ulong fraction = (ulong)TicksQ32 & 0xFFFF_FFFFUL;
      ulong fractionTicks = (((ulong)refreshes * fraction) + (1UL << 31)) >> 32;
      return (refreshes * whole) + (long)fractionTicks;
    }

    /// <summary>The whole number of refreshes nearest to <paramref name="ticks"/> (half up; 0 for ticks &lt;= 0).</summary>
    public long NearestRefreshes(long ticks)
    {
      if (ticks <= 0)
        return 0;
      long refreshes = FloorRefreshes(ticks);
      // The nearer of the two refreshes around ticks, the later one on a tie
      if ((ticks - TicksFor(refreshes)) * 2 >= TicksFor(refreshes + 1) - TicksFor(refreshes))
        ++refreshes;
      return refreshes;
    }

    /// <summary>The most refreshes that fit in <paramref name="ticks"/>: the largest n with TicksFor(n) &lt;= ticks (0 for ticks &lt;= 0).</summary>
    public long FloorRefreshes(long ticks)
    {
      if (ticks <= 0)
        return 0;
      // An estimate from the rounded period (at least 1 tick), then exact: the rounding is at most half a tick a refresh
      long refreshes = ticks / Ticks;
      while (refreshes > 0 && TicksFor(refreshes) > ticks)
        --refreshes;
      while (TicksFor(refreshes + 1) <= ticks)
        ++refreshes;
      return refreshes;
    }

    public bool Equals(RefreshPeriod other) => TicksQ32 == other.TicksQ32;

    public override bool Equals(object obj) => obj is RefreshPeriod other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(TicksQ32);

    public static bool operator ==(RefreshPeriod left, RefreshPeriod right) => left.Equals(right);

    public static bool operator !=(RefreshPeriod left, RefreshPeriod right) => !left.Equals(right);

    public override string ToString() => IsDefault ? "{default}" : FormattableString.Invariant($"{{{TicksQ32 / (double)OneTickQ32:0.####} ticks}}");
  }
}
