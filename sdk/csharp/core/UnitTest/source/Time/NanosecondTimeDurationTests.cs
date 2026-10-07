//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* NanosecondTimeDuration: a NanosecondTimeSpan that is never negative. A negative span becomes zero, two durations added are a duration,
//* their difference and what a NanosecondTimeSpan makes of one are a NanosecondTimeSpan, and overflow is NanosecondTimeSpan's own. From a
//* TimeDuration (ticks of 100 ns) it is exact, and to one truncated to the tick. The cases of the C++ core's tests.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.UnitTest
{
  [TestFixture]
  public class NanosecondTimeDurationTests
  {
    [Test]
    public void HoldsZeroToTheLongestNanosecondTimeSpan()
    {
      Assert.That(default(NanosecondTimeDuration).Nanoseconds, Is.EqualTo(0L));
      Assert.That(NanosecondTimeDuration.Zero.Value, Is.EqualTo(NanosecondTimeSpan.Zero));
      Assert.That(NanosecondTimeDuration.MaxValue.Value, Is.EqualTo(NanosecondTimeSpan.MaxValue));
      Assert.That(NanosecondTimeDuration.MaxValue.Nanoseconds, Is.EqualTo(long.MaxValue));
      Assert.That(new NanosecondTimeDuration(new NanosecondTimeSpan(4_166_389)).Nanoseconds, Is.EqualTo(4_166_389L));
      Assert.That(new NanosecondTimeDuration(NanosecondTimeSpan.FromMilliseconds(16)).Value, Is.EqualTo(NanosecondTimeSpan.FromMilliseconds(16)));
    }

    [Test]
    public void ANegativeSpanBecomesZero()
    {
      Assert.That(new NanosecondTimeDuration(new NanosecondTimeSpan(-1)), Is.EqualTo(NanosecondTimeDuration.Zero));
      Assert.That(new NanosecondTimeDuration(NanosecondTimeSpan.MinValue).Nanoseconds, Is.EqualTo(0L));
      Assert.That(new NanosecondTimeDuration(NanosecondTimeSpan.Zero).Nanoseconds, Is.EqualTo(0L));
      Assert.That(new NanosecondTimeDuration(NanosecondTimeSpan.MaxValue), Is.EqualTo(NanosecondTimeDuration.MaxValue));
      Assert.That(NanosecondTimeDuration.FromNanoseconds(-80_000).Nanoseconds, Is.EqualTo(0L));
      Assert.That(NanosecondTimeDuration.FromNanoseconds(long.MinValue), Is.EqualTo(NanosecondTimeDuration.Zero));
      Assert.That(NanosecondTimeDuration.FromNanoseconds(0).Nanoseconds, Is.EqualTo(0L));
      Assert.That(NanosecondTimeDuration.FromNanoseconds(80_000).Nanoseconds, Is.EqualTo(80_000L));
      Assert.That(NanosecondTimeDuration.FromNanoseconds(long.MaxValue), Is.EqualTo(NanosecondTimeDuration.MaxValue));
    }

    [Test]
    public void UncheckedCreateTakesASpanThatIsNotNegativeAsItIs()
    {
      Assert.That(NanosecondTimeDuration.UncheckedCreate(NanosecondTimeSpan.Zero), Is.EqualTo(NanosecondTimeDuration.Zero));
      Assert.That(NanosecondTimeDuration.UncheckedCreate(new NanosecondTimeSpan(7)).Nanoseconds, Is.EqualTo(7L));
      Assert.That(NanosecondTimeDuration.UncheckedCreate(NanosecondTimeSpan.MaxValue), Is.EqualTo(NanosecondTimeDuration.MaxValue));
    }

    [Test]
    public void GivesItsValueAsASpanAndAsNanoseconds()
    {
      NanosecondTimeDuration duration = NanosecondTimeDuration.FromNanoseconds(166_667);
      Assert.That(duration.Value, Is.EqualTo(new NanosecondTimeSpan(166_667)));
      Assert.That(duration.Nanoseconds, Is.EqualTo(166_667L));
      Assert.That(duration.UnsignedNanoseconds, Is.EqualTo(166_667UL));
      Assert.That(NanosecondTimeDuration.MaxValue.UnsignedNanoseconds, Is.EqualTo(9_223_372_036_854_775_807UL));
      Assert.That(NanosecondTimeDuration.Zero.UnsignedNanoseconds, Is.EqualTo(0UL));
    }

    [Test]
    public void IsExactFromTicksAndTruncatedToThem()
    {
      // A tick is 100 ns
      Assert.That(NanosecondTimeDuration.FromTimeDuration(TimeDuration.FromTicks(166_667)).Nanoseconds, Is.EqualTo(16_666_700L));
      Assert.That(NanosecondTimeDuration.FromTimeDuration(TimeDuration.Zero), Is.EqualTo(NanosecondTimeDuration.Zero));
      // A TimeDuration reaches a hundred times as far: more than nanoseconds can hold
      Assert.That(
        NanosecondTimeDuration.FromTimeDuration(TimeDuration.FromTicks(long.MaxValue / 100)).Nanoseconds,
        Is.EqualTo((long.MaxValue / 100) * 100)
      );
      Assert.That(
        () => NanosecondTimeDuration.FromTimeDuration(TimeDuration.FromTicks((long.MaxValue / 100) + 1)),
        Throws.TypeOf<ArgumentOutOfRangeException>().With.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("duration")
      );
      Assert.That(() => NanosecondTimeDuration.FromTimeDuration(TimeDuration.MaxValue), Throws.TypeOf<ArgumentOutOfRangeException>());

      // To ticks: the whole ticks in it. The refresh period of a 240.016 Hz mode is 41,663 ticks and 89 ns that a tick does not hold
      Assert.That(NanosecondTimeDuration.FromNanoseconds(4_166_389).ToTimeDuration(), Is.EqualTo(TimeDuration.FromTicks(41_663)));
      Assert.That(NanosecondTimeDuration.FromNanoseconds(99).ToTimeDuration(), Is.EqualTo(TimeDuration.Zero));
      Assert.That(NanosecondTimeDuration.FromNanoseconds(100).ToTimeDuration(), Is.EqualTo(TimeDuration.FromTicks(1)));
      Assert.That(NanosecondTimeDuration.FromNanoseconds(399).ToTimeDuration(), Is.EqualTo(TimeDuration.FromTicks(3)));
      Assert.That(NanosecondTimeDuration.MaxValue.ToTimeDuration().Ticks, Is.EqualTo(long.MaxValue / 100));
    }

    [Test]
    public void IsANanosecondTimeSpanWhereverOneIsAskedFor()
    {
      NanosecondTimeSpan span = NanosecondTimeDuration.FromNanoseconds(4_166_389);
      Assert.That(span, Is.EqualTo(new NanosecondTimeSpan(4_166_389)));

      // A point in time takes it as the span it is
      Assert.That(new NanosecondTickCount(10_000) + NanosecondTimeDuration.FromNanoseconds(250), Is.EqualTo(new NanosecondTickCount(10_250)));
      Assert.That(new NanosecondTickCount(10_000) - NanosecondTimeDuration.FromNanoseconds(250), Is.EqualTo(new NanosecondTickCount(9_750)));
    }

    [Test]
    public void TwoDurationsAddedAreADuration()
    {
      NanosecondTimeDuration sum = NanosecondTimeDuration.FromNanoseconds(4_166_389) + NanosecondTimeDuration.FromNanoseconds(611);
      Assert.That(sum.Nanoseconds, Is.EqualTo(4_167_000L));
      sum += NanosecondTimeDuration.FromNanoseconds(23);
      Assert.That(sum, Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(4_167_023)));
      Assert.That(NanosecondTimeDuration.Zero + NanosecondTimeDuration.MaxValue, Is.EqualTo(NanosecondTimeDuration.MaxValue));
      Assert.That(
        () => NanosecondTimeDuration.MaxValue + NanosecondTimeDuration.FromNanoseconds(1),
        Throws.TypeOf<OverflowException>(),
        "as NanosecondTimeSpan's own addition"
      );
    }

    [Test]
    public void ADurationMinusAnotherIsANanosecondTimeSpan()
    {
      NanosecondTimeSpan shorter = NanosecondTimeDuration.FromNanoseconds(4) - NanosecondTimeDuration.FromNanoseconds(10);
      Assert.That(shorter, Is.EqualTo(new NanosecondTimeSpan(-6)), "it can be negative");
      Assert.That(NanosecondTimeDuration.FromNanoseconds(10) - NanosecondTimeDuration.FromNanoseconds(4), Is.EqualTo(new NanosecondTimeSpan(6)));
      Assert.That(NanosecondTimeDuration.FromNanoseconds(4) - NanosecondTimeDuration.FromNanoseconds(4), Is.EqualTo(NanosecondTimeSpan.Zero));
      // The whole range fits: the longest duration from none, and none from the longest
      Assert.That(NanosecondTimeDuration.Zero - NanosecondTimeDuration.MaxValue, Is.EqualTo(new NanosecondTimeSpan(-long.MaxValue)));
      Assert.That(NanosecondTimeDuration.MaxValue - NanosecondTimeDuration.Zero, Is.EqualTo(NanosecondTimeSpan.MaxValue));
    }

    [Test]
    public void ADurationAndASpanGiveASpan()
    {
      NanosecondTimeDuration duration = NanosecondTimeDuration.FromNanoseconds(10);
      NanosecondTimeSpan sum = duration + new NanosecondTimeSpan(5);
      Assert.That(sum, Is.EqualTo(new NanosecondTimeSpan(15)));
      Assert.That(duration + new NanosecondTimeSpan(-25), Is.EqualTo(new NanosecondTimeSpan(-15)));
      Assert.That(new NanosecondTimeSpan(-25) + duration, Is.EqualTo(new NanosecondTimeSpan(-15)));
      Assert.That(new NanosecondTimeSpan(5) + duration, Is.EqualTo(new NanosecondTimeSpan(15)));
      Assert.That(duration - new NanosecondTimeSpan(25), Is.EqualTo(new NanosecondTimeSpan(-15)));
      Assert.That(duration - new NanosecondTimeSpan(-25), Is.EqualTo(new NanosecondTimeSpan(35)));
      Assert.That(new NanosecondTimeSpan(25) - duration, Is.EqualTo(new NanosecondTimeSpan(15)));
      Assert.That(new NanosecondTimeSpan(-25) - duration, Is.EqualTo(new NanosecondTimeSpan(-35)));
      // Outside the range, as NanosecondTimeSpan's sum and difference
      Assert.That(() => NanosecondTimeDuration.MaxValue + new NanosecondTimeSpan(1), Throws.TypeOf<OverflowException>());
      Assert.That(() => new NanosecondTimeSpan(1) + NanosecondTimeDuration.MaxValue, Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTimeDuration.MaxValue - new NanosecondTimeSpan(-1), Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTimeSpan.MinValue - NanosecondTimeDuration.FromNanoseconds(1), Throws.TypeOf<OverflowException>());
    }

    [Test]
    public void MinAndMaxAreOneOfTheTwo()
    {
      NanosecondTimeDuration shorter = NanosecondTimeDuration.FromNanoseconds(3);
      NanosecondTimeDuration longer = NanosecondTimeDuration.FromNanoseconds(4);
      Assert.That(NanosecondTimeDuration.Min(shorter, longer), Is.EqualTo(shorter));
      Assert.That(NanosecondTimeDuration.Min(longer, shorter), Is.EqualTo(shorter));
      Assert.That(NanosecondTimeDuration.Max(shorter, longer), Is.EqualTo(longer));
      Assert.That(NanosecondTimeDuration.Max(longer, shorter), Is.EqualTo(longer));
      Assert.That(NanosecondTimeDuration.Min(shorter, shorter), Is.EqualTo(shorter));
      Assert.That(NanosecondTimeDuration.Max(longer, longer), Is.EqualTo(longer));
    }

    [Test]
    public void ComparesByNanoseconds()
    {
      NanosecondTimeDuration shorter = NanosecondTimeDuration.FromNanoseconds(3);
      NanosecondTimeDuration longer = NanosecondTimeDuration.FromNanoseconds(4);
      Assert.That(shorter == NanosecondTimeDuration.FromNanoseconds(3), Is.True);
      Assert.That(shorter == longer, Is.False);
      Assert.That(shorter != longer, Is.True);
      Assert.That(shorter != NanosecondTimeDuration.FromNanoseconds(3), Is.False);
      Assert.That(shorter < longer, Is.True);
      Assert.That(longer < shorter, Is.False);
      Assert.That(shorter <= NanosecondTimeDuration.FromNanoseconds(3), Is.True);
      Assert.That(longer <= shorter, Is.False);
      Assert.That(longer > shorter, Is.True);
      Assert.That(shorter > longer, Is.False);
      Assert.That(longer >= shorter, Is.True);
      Assert.That(shorter >= longer, Is.False);
      Assert.That(shorter.CompareTo(longer), Is.LessThan(0));
      Assert.That(longer.CompareTo(shorter), Is.GreaterThan(0));
      Assert.That(shorter.CompareTo(NanosecondTimeDuration.FromNanoseconds(3)), Is.EqualTo(0));
    }

    [Test]
    public void ComparesByValue_AndWritesTheSpan()
    {
      NanosecondTimeDuration duration = NanosecondTimeDuration.FromNanoseconds(4_166_389);
      Assert.That(duration.Equals(NanosecondTimeDuration.FromNanoseconds(4_166_389)), Is.True);
      Assert.That(duration.Equals(NanosecondTimeDuration.FromNanoseconds(4_166_390)), Is.False);
      Assert.That(duration.Equals((object)NanosecondTimeDuration.FromNanoseconds(4_166_389)), Is.True);
      Assert.That(duration.Equals((object)NanosecondTimeDuration.FromNanoseconds(4_166_390)), Is.False);
      Assert.That(duration.Equals((object)new NanosecondTimeSpan(4_166_389)), Is.False, "a NanosecondTimeSpan is not a duration");
      Assert.That(duration.Equals(null), Is.False);
      Assert.That(duration.GetHashCode(), Is.EqualTo(NanosecondTimeDuration.FromNanoseconds(4_166_389).GetHashCode()));
      Assert.That(duration.GetHashCode(), Is.Not.EqualTo(NanosecondTimeDuration.FromNanoseconds(4_166_390).GetHashCode()));
      Assert.That(duration.ToString(), Is.EqualTo("4166389 ns"));
      Assert.That(NanosecondTimeDuration.Zero.ToString(), Is.EqualTo("0 ns"));
      Assert.That(NanosecondTimeDuration.MaxValue.ToString(), Is.EqualTo("9223372036854775807 ns"));
    }
  }
}
