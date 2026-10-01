//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* TickCount32: a point on a 32-bit clock of 100 ns ticks that wraps every 429.5 s and compares across the wrap (the same cases as the
//* C++ core's tests).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.UnitTest
{
  [TestFixture]
  public class TickCount32Tests
  {
    private const uint MaxCount = uint.MaxValue;
    private const long HalfRange = 1L << 31;

    [Test]
    public void HasTheUnitsOfATimeSpanAndTheRangeOfAnInt32()
    {
      Assert.That((TickCount32.NanosecondsPerTick, TickCount32.TicksPerMicrosecond), Is.EqualTo((100L, 10L)));
      Assert.That((TickCount32.TicksPerMillisecond, TickCount32.TicksPerSecond), Is.EqualTo((10_000L, 10_000_000L)));
      Assert.That((TickCount32.TicksPerMinute, TickCount32.TicksPerHour), Is.EqualTo((600_000_000L, 36_000_000_000L)));
      Assert.That(TickCount32.TicksPerDay, Is.EqualTo(864_000_000_000L));
      Assert.That((TickCount32.MinDays, TickCount32.MaxDays), Is.EqualTo((0, 0)));
      Assert.That((TickCount32.MinHours, TickCount32.MaxHours), Is.EqualTo((0, 0)));
      Assert.That((TickCount32.MinMinutes, TickCount32.MaxMinutes), Is.EqualTo((-3, 3)));
      Assert.That((TickCount32.MinSeconds, TickCount32.MaxSeconds), Is.EqualTo((-214, 214)));
      Assert.That((TickCount32.MinMilliseconds, TickCount32.MaxMilliseconds), Is.EqualTo((-214_748, 214_748)));
      Assert.That((TickCount32.MinMicroseconds, TickCount32.MaxMicroseconds), Is.EqualTo((-214_748_364, 214_748_364)));
    }

    [Test]
    public void IsAnUnsignedCountWithASignedView()
    {
      Assert.That(default(TickCount32).UnsignedTicks, Is.EqualTo(0u));
      Assert.That(new TickCount32(MaxCount).Ticks, Is.EqualTo(-1));
      Assert.That(new TickCount32(-2).UnsignedTicks, Is.EqualTo(MaxCount - 1u));
      Assert.That(TickCount32.FromTicks(-3).UnsignedTicks, Is.EqualTo(MaxCount - 2u));
      Assert.That(TickCount32.FromUnsignedTicks(7u).Ticks, Is.EqualTo(7));
      // A span or a 64-bit count gives its low 32 bits: the same point on the 32-bit clock
      Assert.That(new TickCount32(new TimeSpan((1L << 32) + 5)).UnsignedTicks, Is.EqualTo(5u));
      Assert.That(new TickCount32(new TimeSpan(-1)).UnsignedTicks, Is.EqualTo(MaxCount));
      Assert.That(TickCount32.FromTickCount64(new TickCount64((3L << 32) + 9)).UnsignedTicks, Is.EqualTo(9u));
      Assert.That(new TickCount32(-5).ToTimeSpan(), Is.EqualTo(new TimeSpan(-5)));
    }

    [Test]
    public void TheFactoriesThrowOutsideTheirRange()
    {
      Assert.That(TickCount32.FromDays(0).Ticks, Is.EqualTo(0));
      Assert.That(() => TickCount32.FromDays(1), Throws.TypeOf<OverflowException>());
      Assert.That(() => TickCount32.FromDays(-1), Throws.TypeOf<OverflowException>());
      Assert.That(TickCount32.FromHours(0).Ticks, Is.EqualTo(0));
      Assert.That(() => TickCount32.FromHours(1), Throws.TypeOf<OverflowException>());
      Assert.That(TickCount32.FromMinutes(3).Ticks, Is.EqualTo(1_800_000_000));
      Assert.That(TickCount32.FromMinutes(-3).Ticks, Is.EqualTo(-1_800_000_000));
      Assert.That(() => TickCount32.FromMinutes(4), Throws.TypeOf<OverflowException>());
      Assert.That(TickCount32.FromSeconds(214).Ticks, Is.EqualTo(2_140_000_000));
      Assert.That(() => TickCount32.FromSeconds(-215), Throws.TypeOf<OverflowException>());
      Assert.That(TickCount32.FromMilliseconds(-214_748).Ticks, Is.EqualTo(-2_147_480_000));
      Assert.That(() => TickCount32.FromMilliseconds(214_749), Throws.TypeOf<OverflowException>());
      Assert.That(TickCount32.FromMicroseconds(214_748_364).Ticks, Is.EqualTo(2_147_483_640));
      Assert.That(() => TickCount32.FromMicroseconds(-214_748_365), Throws.TypeOf<OverflowException>());
      // Nanoseconds stay on the tick they are in; every int of them fits
      Assert.That(TickCount32.FromNanoseconds(150).Ticks, Is.EqualTo(1));
      Assert.That(TickCount32.FromNanoseconds(-1).Ticks, Is.EqualTo(-1));
      Assert.That(TickCount32.FromNanoseconds(int.MinValue).Ticks, Is.EqualTo(-21_474_837));
    }

    [Test]
    public void ComponentsAndTotalsReadTheSignedView()
    {
      var count = new TickCount32(2_123_456_789);
      Assert.That(count.Days, Is.EqualTo(0));
      Assert.That(count.Hours, Is.EqualTo(0));
      Assert.That(count.Minutes, Is.EqualTo(3));
      Assert.That(count.Seconds, Is.EqualTo(32));
      Assert.That(count.Milliseconds, Is.EqualTo(345));
      Assert.That(count.Microseconds, Is.EqualTo(678));
      Assert.That(count.TotalNanoseconds, Is.EqualTo(212345678900.0));
      Assert.That(count.TotalMicroseconds, Is.EqualTo(212345678.9));
      Assert.That(count.TotalMilliseconds, Is.EqualTo(212345.6789));
      Assert.That(count.TotalSeconds, Is.EqualTo(212.3456789));
      Assert.That(count.TotalMinutes, Is.EqualTo(2_123_456_789.0 / 600_000_000.0));
      Assert.That(count.TotalHours, Is.EqualTo(2_123_456_789.0 / 36_000_000_000.0));
      Assert.That(count.TotalDays, Is.EqualTo(2_123_456_789.0 / 864_000_000_000.0));

      // Past 2^31 ticks the signed view is negative
      var wrapped = new TickCount32(MaxCount - 99u);
      Assert.That(wrapped.Ticks, Is.EqualTo(-100));
      Assert.That(wrapped.Microseconds, Is.EqualTo(-10));
      Assert.That(wrapped.TotalMicroseconds, Is.EqualTo(-10.0));
    }

    [Test]
    public void ArithmeticWrapsAround()
    {
      Assert.That(new TickCount32(MaxCount) + new TimeSpan(1), Is.EqualTo(new TickCount32(0u)));
      Assert.That(new TickCount32(0u) - new TimeSpan(1), Is.EqualTo(new TickCount32(MaxCount)));
      // A span is added modulo 2^32
      Assert.That(new TickCount32(5u) + new TimeSpan((7L << 32) + 3), Is.EqualTo(new TickCount32(8u)));
      Assert.That(new TickCount32(5u) + new TimeSpan(-6), Is.EqualTo(new TickCount32(MaxCount)));

      var count = new TickCount32(MaxCount - 1u);
      count += new TimeSpan(4);
      Assert.That(count.UnsignedTicks, Is.EqualTo(2u));
      count -= new TimeSpan(4);
      Assert.That(count.UnsignedTicks, Is.EqualTo(MaxCount - 1u));
    }

    [Test]
    public void TwoCountsSubtractTheShorterWayRoundAcrossTheWrap()
    {
      var before = new TickCount32(MaxCount - 1u);
      var after = new TickCount32(2u);
      Assert.That(after - before, Is.EqualTo(new TimeSpan(4)));
      Assert.That(before - after, Is.EqualTo(new TimeSpan(-4)));
      // The farthest apart that still subtracts correctly: 2^31 - 1 ticks (214.7483647 s)
      TickCount32 far = before + new TimeSpan(HalfRange - 1);
      Assert.That(far - before, Is.EqualTo(new TimeSpan(HalfRange - 1)));
      Assert.That(before - far, Is.EqualTo(new TimeSpan(-(HalfRange - 1))));
    }

    [Test]
    public void ComparesTheShorterWayRoundAcrossTheWrap()
    {
      var before = new TickCount32(MaxCount - 1u);
      var after = new TickCount32(2u);
      Assert.That(before < after, Is.True);
      Assert.That(after < before, Is.False);
      Assert.That(before <= after, Is.True);
      Assert.That(before <= new TickCount32(MaxCount - 1u), Is.True);
      Assert.That(after <= before, Is.False);
      Assert.That(after > before, Is.True);
      Assert.That(before > after, Is.False);
      Assert.That(after >= before, Is.True);
      Assert.That(after >= new TickCount32(2u), Is.True);
      Assert.That(before >= after, Is.False);
      Assert.That(before == new TickCount32(MaxCount - 1u), Is.True);
      Assert.That(before != after, Is.True);
      // Exactly 2^31 ticks apart there is no shorter way round: each is "less" than the other (documented)
      TickCount32 opposite = before + new TimeSpan(HalfRange);
      Assert.That(before < opposite, Is.True);
      Assert.That(opposite < before, Is.True);
      Assert.That(opposite - before, Is.EqualTo(new TimeSpan(-HalfRange)));
    }

    [Test]
    public void ComparesByValue_AndWritesTheTimeSinceTheEpoch()
    {
      var count = new TickCount32(166_667);
      Assert.That(count.Equals(new TickCount32(166_667u)), Is.True);
      Assert.That(count.Equals((object)new TickCount32(166_667)), Is.True);
      Assert.That(count.Equals((object)new TickCount32(166_668)), Is.False);
      Assert.That(count.Equals("166667"), Is.False);
      Assert.That(count.GetHashCode(), Is.EqualTo(new TickCount32(166_667).GetHashCode()));
      Assert.That(count.GetHashCode(), Is.Not.EqualTo(new TickCount32(166_668).GetHashCode()));
      Assert.That(count.ToString(), Is.EqualTo("00:00:00.0166667"));
      Assert.That(new TickCount32(MaxCount).ToString(), Is.EqualTo("-00:00:00.0000001"));
    }
  }
}
