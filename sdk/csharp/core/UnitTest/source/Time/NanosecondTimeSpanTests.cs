//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* NanosecondTimeSpan: a signed interval in nanoseconds, kept as a platform that counts in nanoseconds gives it. To and from a TimeSpan
//* (ticks of 100 ns) it is exact one way and truncated the other, and out of range throws as TimeSpan does. The same cases as the C++
//* core's tests, and seconds as a double with the cases of TimeSpanUtil.FromSeconds.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.UnitTest
{
  [TestFixture]
  public class NanosecondTimeSpanTests
  {
    [Test]
    public void HoldsASignedCountOfNanoseconds()
    {
      Assert.That(default(NanosecondTimeSpan).Nanoseconds, Is.EqualTo(0L));
      Assert.That(NanosecondTimeSpan.Zero.Nanoseconds, Is.EqualTo(0L));
      Assert.That(new NanosecondTimeSpan(4_166_389).Nanoseconds, Is.EqualTo(4_166_389L));
      Assert.That(new NanosecondTimeSpan(-1).Nanoseconds, Is.EqualTo(-1L));
      Assert.That(NanosecondTimeSpan.MinValue.Nanoseconds, Is.EqualTo(long.MinValue));
      Assert.That(NanosecondTimeSpan.MaxValue.Nanoseconds, Is.EqualTo(long.MaxValue));
      Assert.That(NanosecondTimeSpan.FromNanoseconds(-7), Is.EqualTo(new NanosecondTimeSpan(-7)));
      Assert.That(NanosecondTimeSpan.NanosecondsPerTick, Is.EqualTo(100L));
      Assert.That(NanosecondTimeSpan.NanosecondsPerMicrosecond, Is.EqualTo(1_000L));
      Assert.That(NanosecondTimeSpan.NanosecondsPerMillisecond, Is.EqualTo(1_000_000L));
      Assert.That(NanosecondTimeSpan.NanosecondsPerSecond, Is.EqualTo(1_000_000_000L));
    }

    [Test]
    public void IsMadeFromWholeUnitsAndThrowsOutsideItsRange()
    {
      Assert.That(NanosecondTimeSpan.FromMicroseconds(4_166).Nanoseconds, Is.EqualTo(4_166_000L));
      Assert.That(NanosecondTimeSpan.FromMilliseconds(-16).Nanoseconds, Is.EqualTo(-16_000_000L));
      Assert.That(NanosecondTimeSpan.FromSeconds(2).Nanoseconds, Is.EqualTo(2_000_000_000L));
      // The ends of the range: about 292 years either way
      Assert.That(NanosecondTimeSpan.FromSeconds(9_223_372_036).Nanoseconds, Is.EqualTo(9_223_372_036_000_000_000L));
      Assert.That(NanosecondTimeSpan.FromSeconds(-9_223_372_036).Nanoseconds, Is.EqualTo(-9_223_372_036_000_000_000L));
      Assert.That(() => NanosecondTimeSpan.FromSeconds(9_223_372_037), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => NanosecondTimeSpan.FromSeconds(-9_223_372_037), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => NanosecondTimeSpan.FromMilliseconds(long.MaxValue), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => NanosecondTimeSpan.FromMicroseconds(long.MinValue), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void SecondsAsADoubleAreTruncatedTowardZeroToANanosecond()
    {
      Assert.That(NanosecondTimeSpan.FromSeconds(0.0).Nanoseconds, Is.EqualTo(0L));
      Assert.That(NanosecondTimeSpan.FromSeconds(-0.0), Is.EqualTo(NanosecondTimeSpan.Zero));
      Assert.That(NanosecondTimeSpan.FromSeconds(1.0).Nanoseconds, Is.EqualTo(1_000_000_000L));
      // A frame at 60 Hz: 16,666,666.67 ns, less its fraction
      Assert.That(NanosecondTimeSpan.FromSeconds(1.0 / 60).Nanoseconds, Is.EqualTo(16_666_666L));
      Assert.That(NanosecondTimeSpan.FromSeconds(-1.0 / 60).Nanoseconds, Is.EqualTo(-16_666_666L));
      Assert.That(NanosecondTimeSpan.FromSeconds(0.016_666_667_9).Nanoseconds, Is.EqualTo(16_666_667L));
      Assert.That(NanosecondTimeSpan.FromSeconds(0.000_000_001_5).Nanoseconds, Is.EqualTo(1L));
      Assert.That(NanosecondTimeSpan.FromSeconds(-0.000_000_001_5).Nanoseconds, Is.EqualTo(-1L));
      Assert.That(NanosecondTimeSpan.FromSeconds(0.000_000_000_9), Is.EqualTo(NanosecondTimeSpan.Zero));
      Assert.That(NanosecondTimeSpan.FromSeconds(3600.5).Nanoseconds, Is.EqualTo(3_600_500_000_000L));
      Assert.That(NanosecondTimeSpan.FromSeconds(0.5f).Nanoseconds, Is.EqualTo(500_000_000L), "a float is seconds as a double");
    }

    [Test]
    public void SecondsAsADoubleOutsideItsRangeThrow()
    {
      // 2^63 ns, which is what MaxValue's nanoseconds round to as a double, is MaxValue
      Assert.That(NanosecondTimeSpan.FromSeconds(9_223_372_036.854_775_808), Is.EqualTo(NanosecondTimeSpan.MaxValue));
      Assert.That(NanosecondTimeSpan.FromSeconds(-9_223_372_036.854_775_808), Is.EqualTo(NanosecondTimeSpan.MinValue));
      // The double below 2^63 ns, 1024 ns short of it, is in the range as it is
      Assert.That(NanosecondTimeSpan.FromSeconds(9_223_372_036.854_774).Nanoseconds, Is.EqualTo(9_223_372_036_854_774_784L));
      Assert.That(NanosecondTimeSpan.FromSeconds(-9_223_372_036.854_774).Nanoseconds, Is.EqualTo(-9_223_372_036_854_774_784L));
      Assert.That(() => NanosecondTimeSpan.FromSeconds(1e10), Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTimeSpan.FromSeconds(-1e10), Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTimeSpan.FromSeconds(double.MaxValue), Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTimeSpan.FromSeconds(double.PositiveInfinity), Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTimeSpan.FromSeconds(double.NegativeInfinity), Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTimeSpan.FromSeconds(double.NaN), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void AWholeNumberOfSecondsStillPicksTheWholeNumberFactory()
    {
      // An int and a long pick FromSeconds(long): exact where a double is not, and its own exception outside the range
      const int IntSeconds = 2;
      const long LongSeconds = 9_223_372_035;
      Assert.That(NanosecondTimeSpan.FromSeconds(IntSeconds).Nanoseconds, Is.EqualTo(2_000_000_000L));
      Assert.That(NanosecondTimeSpan.FromSeconds(int.MaxValue).Nanoseconds, Is.EqualTo(2_147_483_647_000_000_000L));
      Assert.That(NanosecondTimeSpan.FromSeconds(int.MinValue).Nanoseconds, Is.EqualTo(-2_147_483_648_000_000_000L));
      Assert.That(NanosecondTimeSpan.FromSeconds(LongSeconds).Nanoseconds, Is.EqualTo(9_223_372_035_000_000_000L));
      // The same seconds as a double are 512 ns off: a double that large holds every 1024th nanosecond
      Assert.That(NanosecondTimeSpan.FromSeconds((double)LongSeconds).Nanoseconds, Is.EqualTo(9_223_372_035_000_000_512L));
      Assert.That(() => NanosecondTimeSpan.FromSeconds(9_223_372_037), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => NanosecondTimeSpan.FromSeconds(9_223_372_037.0), Throws.TypeOf<OverflowException>());
    }

    [Test]
    public void ATimeSpanIsExactInNanosecondsAndTheWayBackTruncatesToATick()
    {
      // 100 ns a tick, exactly
      Assert.That(NanosecondTimeSpan.FromTimeSpan(new TimeSpan(41_664)).Nanoseconds, Is.EqualTo(4_166_400L));
      Assert.That(NanosecondTimeSpan.FromTimeSpan(new TimeSpan(-1)).Nanoseconds, Is.EqualTo(-100L));
      Assert.That(NanosecondTimeSpan.FromTimeSpan(TimeSpan.Zero), Is.EqualTo(NanosecondTimeSpan.Zero));
      // A TimeSpan reaches a hundred times as far
      Assert.That(NanosecondTimeSpan.FromTimeSpan(new TimeSpan(long.MaxValue / 100)).Nanoseconds, Is.EqualTo((long.MaxValue / 100) * 100));
      Assert.That(NanosecondTimeSpan.FromTimeSpan(new TimeSpan(long.MinValue / 100)).Nanoseconds, Is.EqualTo((long.MinValue / 100) * 100));
      Assert.That(() => NanosecondTimeSpan.FromTimeSpan(new TimeSpan((long.MaxValue / 100) + 1)), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => NanosecondTimeSpan.FromTimeSpan(new TimeSpan((long.MinValue / 100) - 1)), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => NanosecondTimeSpan.FromTimeSpan(TimeSpan.MaxValue), Throws.TypeOf<ArgumentOutOfRangeException>());

      // The refresh period of a 240.016 Hz mode: 4,166,389 ns is 41,663 ticks and 89 ns that a TimeSpan does not hold
      Assert.That(new NanosecondTimeSpan(4_166_389).ToTimeSpan(), Is.EqualTo(new TimeSpan(41_663)));
      Assert.That(new NanosecondTimeSpan(99).ToTimeSpan(), Is.EqualTo(TimeSpan.Zero));
      Assert.That(new NanosecondTimeSpan(100).ToTimeSpan(), Is.EqualTo(new TimeSpan(1)));
      // Toward zero, as every conversion to a TimeSpan
      Assert.That(new NanosecondTimeSpan(-99).ToTimeSpan(), Is.EqualTo(TimeSpan.Zero));
      Assert.That(new NanosecondTimeSpan(-199).ToTimeSpan(), Is.EqualTo(new TimeSpan(-1)));
      Assert.That(NanosecondTimeSpan.MaxValue.ToTimeSpan(), Is.EqualTo(new TimeSpan(long.MaxValue / 100)));
      Assert.That(NanosecondTimeSpan.MinValue.ToTimeSpan(), Is.EqualTo(new TimeSpan(long.MinValue / 100)));
    }

    [Test]
    public void GivesItsTotalInLargerUnits()
    {
      var span = new NanosecondTimeSpan(4_166_389);
      Assert.That(span.TotalMicroseconds, Is.EqualTo(4_166.389).Within(1e-9));
      Assert.That(span.TotalMilliseconds, Is.EqualTo(4.166389).Within(1e-12));
      Assert.That(span.TotalSeconds, Is.EqualTo(0.004166389).Within(1e-15));
      Assert.That(new NanosecondTimeSpan(-1_500_000_000).TotalSeconds, Is.EqualTo(-1.5));
    }

    [Test]
    public void AddsSubtractsAndNegatesAndThrowsOutsideItsRange()
    {
      var a = new NanosecondTimeSpan(4_166_389);
      var b = new NanosecondTimeSpan(-389);
      Assert.That((a + b).Nanoseconds, Is.EqualTo(4_166_000L));
      Assert.That((a - b).Nanoseconds, Is.EqualTo(4_166_778L));
      Assert.That((b - a).Nanoseconds, Is.EqualTo(-4_166_778L));
      Assert.That(+a, Is.EqualTo(a));
      Assert.That((-a).Nanoseconds, Is.EqualTo(-4_166_389L));
      Assert.That(a.Negate(), Is.EqualTo(-a));
      Assert.That(b.Duration().Nanoseconds, Is.EqualTo(389L));
      Assert.That(a.Duration(), Is.EqualTo(a));
      Assert.That(NanosecondTimeSpan.Zero.Duration(), Is.EqualTo(NanosecondTimeSpan.Zero));

      // The ends
      Assert.That(NanosecondTimeSpan.MaxValue + NanosecondTimeSpan.Zero, Is.EqualTo(NanosecondTimeSpan.MaxValue));
      Assert.That(NanosecondTimeSpan.MaxValue + new NanosecondTimeSpan(-1), Is.EqualTo(new NanosecondTimeSpan(long.MaxValue - 1)));
      Assert.That(NanosecondTimeSpan.MinValue - new NanosecondTimeSpan(-1), Is.EqualTo(new NanosecondTimeSpan(long.MinValue + 1)));
      Assert.That(NanosecondTimeSpan.MinValue + NanosecondTimeSpan.MaxValue, Is.EqualTo(new NanosecondTimeSpan(-1)));
      Assert.That(() => NanosecondTimeSpan.MaxValue + new NanosecondTimeSpan(1), Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTimeSpan.MinValue + new NanosecondTimeSpan(-1), Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTimeSpan.MaxValue - new NanosecondTimeSpan(-1), Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTimeSpan.MinValue - new NanosecondTimeSpan(1), Throws.TypeOf<OverflowException>());
      // The lowest value has no positive counterpart
      Assert.That(() => -NanosecondTimeSpan.MinValue, Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTimeSpan.MinValue.Negate(), Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTimeSpan.MinValue.Duration(), Throws.TypeOf<OverflowException>());
      Assert.That(-NanosecondTimeSpan.MaxValue, Is.EqualTo(new NanosecondTimeSpan(long.MinValue + 1)));
    }

    [Test]
    public void ComparesByItsCount()
    {
      var shorter = new NanosecondTimeSpan(-100);
      var longer = new NanosecondTimeSpan(100);
      Assert.That(shorter < longer, Is.True);
      Assert.That(shorter <= longer, Is.True);
      Assert.That(longer > shorter, Is.True);
      Assert.That(longer >= shorter, Is.True);
      Assert.That(longer < shorter, Is.False);
      Assert.That(longer <= shorter, Is.False);
      Assert.That(shorter > longer, Is.False);
      Assert.That(shorter >= longer, Is.False);
      Assert.That(shorter != longer, Is.True);
      Assert.That(shorter == new NanosecondTimeSpan(-100), Is.True);
      Assert.That(shorter.CompareTo(longer), Is.LessThan(0));
      Assert.That(longer.CompareTo(shorter), Is.GreaterThan(0));
      Assert.That(shorter.CompareTo(shorter), Is.EqualTo(0));
      Assert.That(shorter.Equals(new NanosecondTimeSpan(-100)), Is.True);
      Assert.That(shorter.Equals((object)new NanosecondTimeSpan(-100)), Is.True);
      Assert.That(shorter.Equals((object)longer), Is.False);
      Assert.That(shorter.Equals("-100 ns"), Is.False);
      Assert.That(shorter.GetHashCode(), Is.EqualTo(new NanosecondTimeSpan(-100).GetHashCode()));
      Assert.That(shorter.GetHashCode(), Is.Not.EqualTo(longer.GetHashCode()));
    }

    [Test]
    public void IsWrittenAsItsCountAndItsUnit()
    {
      Assert.That(new NanosecondTimeSpan(4_166_389).ToString(), Is.EqualTo("4166389 ns"));
      Assert.That(new NanosecondTimeSpan(-389).ToString(), Is.EqualTo("-389 ns"));
      Assert.That(NanosecondTimeSpan.Zero.ToString(), Is.EqualTo("0 ns"));
    }
  }
}
