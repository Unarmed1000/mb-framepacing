//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* TickCount64: a point on a steady clock in 100 ns ticks, stored unsigned, wrap-around safe (the same cases as the C++ core's tests).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.UnitTest
{
  [TestFixture]
  public class TickCount64Tests
  {
    private const long MaxTicks = long.MaxValue;
    private const long MinTicks = long.MinValue;

    [Test]
    public void HasTheUnitsOfATimeSpan()
    {
      Assert.That((TickCount64.NanosecondsPerTick, TickCount64.TicksPerMicrosecond), Is.EqualTo((100L, 10L)));
      Assert.That((TickCount64.TicksPerMillisecond, TickCount64.TicksPerSecond), Is.EqualTo((10_000L, 10_000_000L)));
      Assert.That((TickCount64.TicksPerMinute, TickCount64.TicksPerHour), Is.EqualTo((600_000_000L, 36_000_000_000L)));
      Assert.That(TickCount64.TicksPerDay, Is.EqualTo(864_000_000_000L));
      Assert.That((TickCount64.MinDays, TickCount64.MaxDays), Is.EqualTo((-10_675_199L, 10_675_199L)));
      Assert.That((TickCount64.MinHours, TickCount64.MaxHours), Is.EqualTo((-256_204_778L, 256_204_778L)));
      Assert.That((TickCount64.MinMinutes, TickCount64.MaxMinutes), Is.EqualTo((-15_372_286_728L, 15_372_286_728L)));
      Assert.That((TickCount64.MinSeconds, TickCount64.MaxSeconds), Is.EqualTo((-922_337_203_685L, 922_337_203_685L)));
      Assert.That((TickCount64.MinMilliseconds, TickCount64.MaxMilliseconds), Is.EqualTo((-922_337_203_685_477L, 922_337_203_685_477L)));
      Assert.That((TickCount64.MinMicroseconds, TickCount64.MaxMicroseconds), Is.EqualTo((-922_337_203_685_477_580L, 922_337_203_685_477_580L)));
    }

    [Test]
    public void IsAnUnsignedCountWithASignedView()
    {
      Assert.That(default(TickCount64).Ticks, Is.EqualTo(0));
      Assert.That(new TickCount64(-1).Ticks, Is.EqualTo(-1));
      Assert.That(new TickCount64(-1).UnsignedTicks, Is.EqualTo(ulong.MaxValue));
      Assert.That(new TickCount64(new TimeSpan(42)).Ticks, Is.EqualTo(42));
      Assert.That(TickCount64.FromTicks(MinTicks).Ticks, Is.EqualTo(MinTicks));
      Assert.That(TickCount64.FromUnsignedTicks(ulong.MaxValue).Ticks, Is.EqualTo(-1));
      Assert.That(TickCount64.FromUnsignedTicks(5).UnsignedTicks, Is.EqualTo(5UL));
      Assert.That(new TickCount64(123).ToTimeSpan(), Is.EqualTo(new TimeSpan(123)));
    }

    [Test]
    public void TheFactoriesThrowOutsideTheirRange()
    {
      Assert.That(TickCount64.FromDays(TickCount64.MaxDays).Ticks, Is.EqualTo(9_223_371_936_000_000_000L));
      Assert.That(TickCount64.FromDays(TickCount64.MinDays).Ticks, Is.EqualTo(-9_223_371_936_000_000_000L));
      Assert.That(() => TickCount64.FromDays(TickCount64.MaxDays + 1), Throws.TypeOf<OverflowException>());
      Assert.That(() => TickCount64.FromDays(TickCount64.MinDays - 1), Throws.TypeOf<OverflowException>());
      Assert.That(TickCount64.FromHours(2).Ticks, Is.EqualTo(72_000_000_000L));
      Assert.That(() => TickCount64.FromHours(TickCount64.MaxHours + 1), Throws.TypeOf<OverflowException>());
      Assert.That(TickCount64.FromMinutes(-2).Ticks, Is.EqualTo(-1_200_000_000L));
      Assert.That(() => TickCount64.FromMinutes(TickCount64.MinMinutes - 1), Throws.TypeOf<OverflowException>());
      Assert.That(TickCount64.FromSeconds(3).Ticks, Is.EqualTo(30_000_000L));
      Assert.That(() => TickCount64.FromSeconds(TickCount64.MaxSeconds + 1), Throws.TypeOf<OverflowException>());
      Assert.That(TickCount64.FromMilliseconds(16).Ticks, Is.EqualTo(160_000L));
      Assert.That(() => TickCount64.FromMilliseconds(TickCount64.MaxMilliseconds + 1), Throws.TypeOf<OverflowException>());
      Assert.That(TickCount64.FromMicroseconds(TickCount64.MaxMicroseconds).Ticks, Is.EqualTo(9_223_372_036_854_775_800L));
      Assert.That(() => TickCount64.FromMicroseconds(TickCount64.MinMicroseconds - 1), Throws.TypeOf<OverflowException>());
    }

    [Test]
    public void NanosecondsRoundDownToTheTickTheyAreIn()
    {
      Assert.That(TickCount64.FromNanoseconds(0).Ticks, Is.EqualTo(0));
      Assert.That(TickCount64.FromNanoseconds(99).Ticks, Is.EqualTo(0));
      Assert.That(TickCount64.FromNanoseconds(100).Ticks, Is.EqualTo(1));
      Assert.That(TickCount64.FromNanoseconds(150).Ticks, Is.EqualTo(1));
      Assert.That(TickCount64.FromNanoseconds(16_683_350).Ticks, Is.EqualTo(166_833));
      Assert.That(TickCount64.FromNanoseconds(-1).Ticks, Is.EqualTo(-1));
      Assert.That(TickCount64.FromNanoseconds(-100).Ticks, Is.EqualTo(-1));
      Assert.That(TickCount64.FromNanoseconds(-101).Ticks, Is.EqualTo(-2));
      Assert.That(TickCount64.FromNanoseconds(-150).Ticks, Is.EqualTo(-2));
      Assert.That(TickCount64.FromNanoseconds(MaxTicks).Ticks, Is.EqualTo(MaxTicks / 100));
    }

    [Test]
    public void ACounterConvertsExactlyAtAnyValue()
    {
      // A 10 MHz counter (Stopwatch.Frequency on current Windows) is in ticks already
      Assert.That(TickCount64.FromCounter(123_456_789, 10_000_000).Ticks, Is.EqualTo(123_456_789));
      Assert.That(TickCount64.FromCounter(20_000_000, 10_000_000).Ticks, Is.EqualTo(20_000_000));
      // A 3 GHz counter: one second and a third of a tick
      Assert.That(TickCount64.FromCounter(3_000_000_100, 3_000_000_000).Ticks, Is.EqualTo(TickCount64.TicksPerSecond));
      Assert.That(TickCount64.FromCounter(3_000_000_300, 3_000_000_000).Ticks, Is.EqualTo(TickCount64.TicksPerSecond + 1));
      // Nanoseconds as a counter agree with FromNanoseconds, negative values included
      Assert.That(TickCount64.FromCounter(-150, 1_000_000_000), Is.EqualTo(TickCount64.FromNanoseconds(-150)));
      // A counter near its limit does not overflow: 2^63 - 1 at 3 GHz is about 97 years
      const long Frequency = 3_000_000_000;
      const long TicksPerSecond = TickCount64.TicksPerSecond;
      Assert.That(
        TickCount64.FromCounter(MaxTicks, Frequency).Ticks,
        Is.EqualTo(((MaxTicks / Frequency) * TicksPerSecond) + (((MaxTicks % Frequency) * TicksPerSecond) / Frequency))
      );
      // The fastest counter it takes, at its limit too
      const long Fastest = TickCount64.MaxCounterFrequency;
      Assert.That(Fastest, Is.EqualTo(922_337_203_685L));
      Assert.That(TickCount64.FromCounter(Fastest, Fastest).Ticks, Is.EqualTo(TicksPerSecond));
      Assert.That(
        TickCount64.FromCounter(MaxTicks, Fastest).Ticks,
        Is.EqualTo(((MaxTicks / Fastest) * TicksPerSecond) + (((MaxTicks % Fastest) * TicksPerSecond) / Fastest))
      );
    }

    [Test]
    public void ACounterFrequencyOutsideItsRangeThrows()
    {
      Assert.That(() => TickCount64.FromCounter(123, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => TickCount64.FromCounter(123, -10_000_000), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => TickCount64.FromCounter(123, TickCount64.MaxCounterFrequency + 1), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ComponentsAndTotalsReadTheSignedView()
    {
      var count = new TickCount64(123_456_789_012_345);
      Assert.That(count.Days, Is.EqualTo(142));
      Assert.That(count.Hours, Is.EqualTo(21));
      Assert.That(count.Minutes, Is.EqualTo(21));
      Assert.That(count.Seconds, Is.EqualTo(18));
      Assert.That(count.Milliseconds, Is.EqualTo(901));
      Assert.That(count.Microseconds, Is.EqualTo(234));
      Assert.That(count.TotalDays, Is.EqualTo(142.88980209762153));
      Assert.That(count.TotalHours, Is.EqualTo(3429.355250342917));
      Assert.That(count.TotalMinutes, Is.EqualTo(205761.315020575));
      Assert.That(count.TotalSeconds, Is.EqualTo(12345678.9012345));
      Assert.That(count.TotalMilliseconds, Is.EqualTo(12345678901.2345));
      Assert.That(count.TotalMicroseconds, Is.EqualTo(12345678901234.5));
      Assert.That(count.TotalNanoseconds, Is.EqualTo(12345678901234500.0));

      var negative = new TickCount64(-937_840_050_067);
      Assert.That(negative.Days, Is.EqualTo(-1));
      Assert.That(negative.Hours, Is.EqualTo(-2));
      Assert.That(negative.Minutes, Is.EqualTo(-3));
      Assert.That(negative.Seconds, Is.EqualTo(-4));
      Assert.That(negative.Milliseconds, Is.EqualTo(-5));
      Assert.That(negative.Microseconds, Is.EqualTo(-6));
      Assert.That(negative.TotalSeconds, Is.EqualTo(-93784.0050067));
    }

    [Test]
    public void ArithmeticWrapsAround()
    {
      Assert.That(new TickCount64(100) + new TimeSpan(5), Is.EqualTo(new TickCount64(105)));
      Assert.That(new TickCount64(100) - new TimeSpan(5), Is.EqualTo(new TickCount64(95)));
      Assert.That(new TickCount64(100) - new TickCount64(30), Is.EqualTo(new TimeSpan(70)));
      Assert.That(new TickCount64(30) - new TickCount64(100), Is.EqualTo(new TimeSpan(-70)));
      // Past the end of the signed view
      Assert.That(new TickCount64(MaxTicks) + new TimeSpan(1), Is.EqualTo(new TickCount64(MinTicks)));
      Assert.That(new TickCount64(MinTicks) - new TimeSpan(1), Is.EqualTo(new TickCount64(MaxTicks)));
      Assert.That(new TickCount64(MinTicks) - new TickCount64(MaxTicks), Is.EqualTo(new TimeSpan(1)));

      var count = new TickCount64(10);
      count += new TimeSpan(-15);
      Assert.That(count.Ticks, Is.EqualTo(-5));
      count -= new TimeSpan(-5);
      Assert.That(count.Ticks, Is.EqualTo(0));
    }

    [Test]
    public void ComparesTheShorterWayRoundAcrossTheWrap()
    {
      var earlier = new TickCount64(-1);
      var later = new TickCount64(0);
      Assert.That(earlier == new TickCount64(-1), Is.True);
      Assert.That(earlier != later, Is.True);
      Assert.That(earlier < later, Is.True);
      Assert.That(later < earlier, Is.False);
      Assert.That(earlier <= later, Is.True);
      Assert.That(later <= new TickCount64(0), Is.True);
      Assert.That(later <= earlier, Is.False);
      Assert.That(later > earlier, Is.True);
      Assert.That(earlier > later, Is.False);
      Assert.That(later >= earlier, Is.True);
      Assert.That(later >= new TickCount64(0), Is.True);
      Assert.That(earlier >= later, Is.False);
      // Across the wrap of the stored count: MaxTicks + 1 tick is MinTicks, and it is later
      var beforeWrap = new TickCount64(MaxTicks);
      TickCount64 afterWrap = beforeWrap + new TimeSpan(1);
      Assert.That(beforeWrap < afterWrap, Is.True);
      Assert.That(afterWrap > beforeWrap, Is.True);
      Assert.That(afterWrap - beforeWrap, Is.EqualTo(new TimeSpan(1)));
      TickCount64 unsignedEnd = TickCount64.FromUnsignedTicks(ulong.MaxValue);
      Assert.That(unsignedEnd < TickCount64.FromUnsignedTicks(1), Is.True);
      Assert.That(TickCount64.FromUnsignedTicks(1) - unsignedEnd, Is.EqualTo(new TimeSpan(2)));
      Assert.That(TickCount64.FromSeconds(1) - default(TickCount64), Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void ComparesByValue_AndWritesTheTimeSinceTheEpoch()
    {
      var count = new TickCount64(166_667);
      Assert.That(count.Equals(new TickCount64(166_667)), Is.True);
      Assert.That(count.Equals((object)new TickCount64(166_667)), Is.True);
      Assert.That(count.Equals((object)new TickCount64(166_668)), Is.False);
      Assert.That(count.Equals("166667"), Is.False);
      Assert.That(count.GetHashCode(), Is.EqualTo(new TickCount64(166_667).GetHashCode()));
      Assert.That(count.GetHashCode(), Is.Not.EqualTo(new TickCount64(166_668).GetHashCode()));
      Assert.That(count.ToString(), Is.EqualTo("00:00:00.0166667"));
      Assert.That(new TickCount64(-TimeSpan.TicksPerDay).ToString(), Is.EqualTo("-1.00:00:00"));
    }
  }
}
