//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* TimeDuration: a TimeSpan that is never negative. A negative span becomes zero, two durations added are a duration and their difference a
//* TimeSpan, a duration times or divided by a count stays a duration, and overflow is TimeSpan's own.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.UnitTest
{
  [TestFixture]
  public class TimeDurationTests
  {
    [Test]
    public void HoldsZeroToTheLongestTimeSpan()
    {
      Assert.That(default(TimeDuration).Ticks, Is.EqualTo(0L));
      Assert.That(TimeDuration.Zero.Value, Is.EqualTo(TimeSpan.Zero));
      Assert.That(TimeDuration.MaxValue.Value, Is.EqualTo(TimeSpan.MaxValue));
      Assert.That(TimeDuration.MaxValue.Ticks, Is.EqualTo(long.MaxValue));
      Assert.That(new TimeDuration(new TimeSpan(166_667)).Ticks, Is.EqualTo(166_667L));
      Assert.That(new TimeDuration(TimeSpan.FromMilliseconds(16)).Value, Is.EqualTo(TimeSpan.FromMilliseconds(16)));
    }

    [Test]
    public void ANegativeSpanBecomesZero()
    {
      Assert.That(new TimeDuration(new TimeSpan(-1)), Is.EqualTo(TimeDuration.Zero));
      Assert.That(new TimeDuration(TimeSpan.MinValue).Ticks, Is.EqualTo(0L));
      Assert.That(new TimeDuration(TimeSpan.Zero).Ticks, Is.EqualTo(0L));
      Assert.That(TimeDuration.FromTicks(-166_667).Ticks, Is.EqualTo(0L));
      Assert.That(TimeDuration.FromTicks(long.MinValue), Is.EqualTo(TimeDuration.Zero));
      Assert.That(TimeDuration.FromTicks(0).Ticks, Is.EqualTo(0L));
      Assert.That(TimeDuration.FromTicks(80_000).Ticks, Is.EqualTo(80_000L));
      Assert.That(TimeDuration.FromTicks(long.MaxValue), Is.EqualTo(TimeDuration.MaxValue));
    }

    [Test]
    public void UncheckedCreateTakesASpanThatIsNotNegativeAsItIs()
    {
      Assert.That(TimeDuration.UncheckedCreate(TimeSpan.Zero), Is.EqualTo(TimeDuration.Zero));
      Assert.That(TimeDuration.UncheckedCreate(new TimeSpan(166_667)).Ticks, Is.EqualTo(166_667L));
      Assert.That(TimeDuration.UncheckedCreate(TimeSpan.MaxValue), Is.EqualTo(TimeDuration.MaxValue));
    }

    [Test]
    public void WidensFromTheMarkers32BitSpan()
    {
      Assert.That(TimeDuration.From(TimeSpan32.Zero), Is.EqualTo(TimeDuration.Zero));
      Assert.That(TimeDuration.From(new TimeSpan32(166_667u)).Ticks, Is.EqualTo(166_667L));
      Assert.That(TimeDuration.From(TimeSpan32.MaxValue).Ticks, Is.EqualTo(4_294_967_295L));
    }

    [Test]
    public void IsATimeSpanWhereverOneIsAskedFor()
    {
      TimeSpan span = new TimeDuration(new TimeSpan(166_667));
      Assert.That(span, Is.EqualTo(new TimeSpan(166_667)));

      // A TimeSpan's own operators take it: a point in time plus a duration, a span minus a duration
      Assert.That(new TimeSpan(1_000) - new TimeDuration(new TimeSpan(1_500)), Is.EqualTo(new TimeSpan(-500)));
      Assert.That(new TickCount64(10_000) + new TimeDuration(new TimeSpan(250)), Is.EqualTo(new TickCount64(10_250)));
    }

    [Test]
    public void TwoDurationsAddedAreADuration()
    {
      TimeDuration sum = new TimeDuration(new TimeSpan(166_667)) + new TimeDuration(new TimeSpan(83_333));
      Assert.That(sum.Ticks, Is.EqualTo(250_000L));
      Assert.That(TimeDuration.Zero + TimeDuration.MaxValue, Is.EqualTo(TimeDuration.MaxValue));
      Assert.That(() => TimeDuration.MaxValue + TimeDuration.FromTicks(1), Throws.TypeOf<OverflowException>(), "as TimeSpan's own addition");
    }

    [Test]
    public void ADurationMinusAnotherIsATimeSpan()
    {
      TimeSpan shorter = new TimeDuration(new TimeSpan(100)) - new TimeDuration(new TimeSpan(250));
      Assert.That(shorter, Is.EqualTo(new TimeSpan(-150)), "it can be negative");
      Assert.That(new TimeDuration(new TimeSpan(250)) - new TimeDuration(new TimeSpan(100)), Is.EqualTo(new TimeSpan(150)));
      Assert.That(TimeDuration.Zero - TimeDuration.MaxValue, Is.EqualTo(new TimeSpan(-long.MaxValue)));
    }

    [Test]
    public void ADurationTimesOrDividedByACountIsADuration()
    {
      TimeDuration frame = TimeDuration.FromTicks(166_667);
      Assert.That((frame * 3u).Ticks, Is.EqualTo(500_001L));
      Assert.That((frame * 0u), Is.EqualTo(TimeDuration.Zero));
      Assert.That((TimeDuration.FromTicks(1) * uint.MaxValue).Ticks, Is.EqualTo(4_294_967_295L));
      Assert.That(() => TimeDuration.MaxValue * 2u, Throws.TypeOf<OverflowException>());

      Assert.That((frame / 2u).Ticks, Is.EqualTo(83_333L), "in whole ticks, truncated");
      Assert.That((frame / uint.MaxValue), Is.EqualTo(TimeDuration.Zero));
      Assert.That((TimeDuration.MaxValue / 1u), Is.EqualTo(TimeDuration.MaxValue));
      Assert.That(() => frame / 0u, Throws.TypeOf<DivideByZeroException>());
    }

    [Test]
    public void MinAndMaxAreOneOfTheTwo()
    {
      TimeDuration shorter = TimeDuration.FromTicks(3);
      TimeDuration longer = TimeDuration.FromTicks(4);
      Assert.That(TimeDuration.Min(shorter, longer), Is.EqualTo(shorter));
      Assert.That(TimeDuration.Min(longer, shorter), Is.EqualTo(shorter));
      Assert.That(TimeDuration.Max(shorter, longer), Is.EqualTo(longer));
      Assert.That(TimeDuration.Max(longer, shorter), Is.EqualTo(longer));
      Assert.That(TimeDuration.Min(shorter, shorter), Is.EqualTo(shorter));
      Assert.That(TimeDuration.Max(longer, longer), Is.EqualTo(longer));
    }

    [Test]
    public void ComparesByTicks()
    {
      TimeDuration shorter = TimeDuration.FromTicks(3);
      TimeDuration longer = TimeDuration.FromTicks(4);
      Assert.That(shorter == TimeDuration.FromTicks(3), Is.True);
      Assert.That(shorter == longer, Is.False);
      Assert.That(shorter != longer, Is.True);
      Assert.That(shorter != TimeDuration.FromTicks(3), Is.False);
      Assert.That(shorter < longer, Is.True);
      Assert.That(longer < shorter, Is.False);
      Assert.That(shorter <= TimeDuration.FromTicks(3), Is.True);
      Assert.That(longer <= shorter, Is.False);
      Assert.That(longer > shorter, Is.True);
      Assert.That(shorter > longer, Is.False);
      Assert.That(longer >= shorter, Is.True);
      Assert.That(shorter >= longer, Is.False);
      Assert.That(shorter.CompareTo(longer), Is.LessThan(0));
      Assert.That(longer.CompareTo(shorter), Is.GreaterThan(0));
      Assert.That(shorter.CompareTo(TimeDuration.FromTicks(3)), Is.EqualTo(0));
    }

    [Test]
    public void ComparesByValue_AndWritesTheSpan()
    {
      TimeDuration duration = TimeDuration.FromTicks(166_667);
      Assert.That(duration.Equals(TimeDuration.FromTicks(166_667)), Is.True);
      Assert.That(duration.Equals(TimeDuration.FromTicks(166_668)), Is.False);
      Assert.That(duration.Equals((object)TimeDuration.FromTicks(166_667)), Is.True);
      Assert.That(duration.Equals((object)TimeDuration.FromTicks(166_668)), Is.False);
      Assert.That(duration.Equals((object)new TimeSpan(166_667)), Is.False, "a TimeSpan is not a duration");
      Assert.That(duration.Equals(null), Is.False);
      Assert.That(duration.GetHashCode(), Is.EqualTo(TimeDuration.FromTicks(166_667).GetHashCode()));
      Assert.That(duration.GetHashCode(), Is.Not.EqualTo(TimeDuration.FromTicks(166_668).GetHashCode()));
      Assert.That(duration.ToString(), Is.EqualTo("00:00:00.0166667"));
      Assert.That(TimeDuration.MaxValue.ToString(), Is.EqualTo("10675199.02:48:05.4775807"));
    }
  }
}
