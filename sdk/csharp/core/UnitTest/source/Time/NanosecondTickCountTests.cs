//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* NanosecondTickCount: a point on a clock that counts in nanoseconds, stored unsigned so it wraps and compares across the wrap, as
//* TickCount64 does in ticks of 100 ns. To a TickCount64 it is the tick the point is in. The same cases as the C++ core's tests.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.UnitTest
{
  [TestFixture]
  public class NanosecondTickCountTests
  {
    [Test]
    public void HoldsACountOfNanosecondsStoredUnsigned()
    {
      Assert.That(default(NanosecondTickCount).Nanoseconds, Is.EqualTo(0L));
      Assert.That(new NanosecondTickCount(4_166_389).Nanoseconds, Is.EqualTo(4_166_389L));
      Assert.That(new NanosecondTickCount(4_166_389).UnsignedNanoseconds, Is.EqualTo(4_166_389UL));
      Assert.That(new NanosecondTickCount(-1).Nanoseconds, Is.EqualTo(-1L));
      Assert.That(new NanosecondTickCount(-1).UnsignedNanoseconds, Is.EqualTo(ulong.MaxValue));
      Assert.That(NanosecondTickCount.FromNanoseconds(-7).Nanoseconds, Is.EqualTo(-7L));
      Assert.That(NanosecondTickCount.FromUnsignedNanoseconds(ulong.MaxValue).Nanoseconds, Is.EqualTo(-1L));
      Assert.That(NanosecondTickCount.FromUnsignedNanoseconds(12), Is.EqualTo(new NanosecondTickCount(12)));
      // The point a span after the clock's epoch, and the span since it
      Assert.That(new NanosecondTickCount(new NanosecondTimeSpan(4_166_389)).Nanoseconds, Is.EqualTo(4_166_389L));
      Assert.That(new NanosecondTickCount(4_166_389).ToNanosecondTimeSpan(), Is.EqualTo(new NanosecondTimeSpan(4_166_389)));
      Assert.That(NanosecondTickCount.NanosecondsPerTick, Is.EqualTo(100L));
      Assert.That(NanosecondTickCount.NanosecondsPerMicrosecond, Is.EqualTo(1_000L));
      Assert.That(NanosecondTickCount.NanosecondsPerMillisecond, Is.EqualTo(1_000_000L));
      Assert.That(NanosecondTickCount.NanosecondsPerSecond, Is.EqualTo(1_000_000_000L));
    }

    [Test]
    public void IsMadeFromWholeUnitsAndThrowsOutsideItsRange()
    {
      Assert.That(NanosecondTickCount.FromSeconds(2).Nanoseconds, Is.EqualTo(2_000_000_000L));
      Assert.That(NanosecondTickCount.FromMilliseconds(-16).Nanoseconds, Is.EqualTo(-16_000_000L));
      Assert.That(NanosecondTickCount.FromMicroseconds(4_166).Nanoseconds, Is.EqualTo(4_166_000L));
      Assert.That(NanosecondTickCount.FromSeconds(NanosecondTickCount.MaxSeconds).Nanoseconds, Is.EqualTo(9_223_372_036_000_000_000L));
      Assert.That(NanosecondTickCount.FromSeconds(NanosecondTickCount.MinSeconds).Nanoseconds, Is.EqualTo(-9_223_372_036_000_000_000L));
      Assert.That(NanosecondTickCount.FromMilliseconds(NanosecondTickCount.MaxMilliseconds).Nanoseconds, Is.EqualTo(9_223_372_036_854_000_000L));
      Assert.That(NanosecondTickCount.FromMicroseconds(NanosecondTickCount.MinMicroseconds).Nanoseconds, Is.EqualTo(-9_223_372_036_854_775_000L));
      Assert.That(() => NanosecondTickCount.FromSeconds(NanosecondTickCount.MaxSeconds + 1), Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTickCount.FromSeconds(NanosecondTickCount.MinSeconds - 1), Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTickCount.FromMilliseconds(NanosecondTickCount.MaxMilliseconds + 1), Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTickCount.FromMicroseconds(NanosecondTickCount.MinMicroseconds - 1), Throws.TypeOf<OverflowException>());
    }

    [Test]
    public void ATickCountIsExactInNanosecondsAndTheWayBackIsTheTickThePointIsIn()
    {
      Assert.That(NanosecondTickCount.FromTickCount64(new TickCount64(41_664)).Nanoseconds, Is.EqualTo(4_166_400L));
      Assert.That(NanosecondTickCount.FromTickCount64(new TickCount64(-1)).Nanoseconds, Is.EqualTo(-100L));
      Assert.That(
        NanosecondTickCount.FromTickCount64(new TickCount64(NanosecondTickCount.MaxTicks)).Nanoseconds,
        Is.EqualTo((long.MaxValue / 100) * 100)
      );
      Assert.That(
        NanosecondTickCount.FromTickCount64(new TickCount64(NanosecondTickCount.MinTicks)).Nanoseconds,
        Is.EqualTo((long.MinValue / 100) * 100)
      );
      // A TickCount64 reaches a hundred times as far
      Assert.That(() => NanosecondTickCount.FromTickCount64(new TickCount64(NanosecondTickCount.MaxTicks + 1)), Throws.TypeOf<OverflowException>());
      Assert.That(() => NanosecondTickCount.FromTickCount64(new TickCount64(NanosecondTickCount.MinTicks - 1)), Throws.TypeOf<OverflowException>());

      // Rounded down to the tick the point is in, before the epoch too
      Assert.That(new NanosecondTickCount(4_166_389).ToTickCount64(), Is.EqualTo(new TickCount64(41_663)));
      Assert.That(new NanosecondTickCount(99).ToTickCount64(), Is.EqualTo(new TickCount64(0)));
      Assert.That(new NanosecondTickCount(100).ToTickCount64(), Is.EqualTo(new TickCount64(1)));
      Assert.That(new NanosecondTickCount(-1).ToTickCount64(), Is.EqualTo(new TickCount64(-1)));
      Assert.That(new NanosecondTickCount(-100).ToTickCount64(), Is.EqualTo(new TickCount64(-1)));
      Assert.That(new NanosecondTickCount(-101).ToTickCount64(), Is.EqualTo(new TickCount64(-2)));
    }

    [Test]
    public void GivesItsTotalInLargerUnits()
    {
      var count = new NanosecondTickCount(4_166_389);
      Assert.That(count.TotalMicroseconds, Is.EqualTo(4_166.389).Within(1e-9));
      Assert.That(count.TotalMilliseconds, Is.EqualTo(4.166389).Within(1e-12));
      Assert.That(count.TotalSeconds, Is.EqualTo(0.004166389).Within(1e-15));
      Assert.That(new NanosecondTickCount(-1_500_000_000).TotalSeconds, Is.EqualTo(-1.5));
    }

    [Test]
    public void ASpanMovesItAndTwoCountsAreASpanApart()
    {
      var count = new NanosecondTickCount(1_000_000_000);
      Assert.That((count + new NanosecondTimeSpan(4_166_389)).Nanoseconds, Is.EqualTo(1_004_166_389L));
      Assert.That((count - new NanosecondTimeSpan(4_166_389)).Nanoseconds, Is.EqualTo(995_833_611L));
      Assert.That((count + new NanosecondTimeSpan(-389)).Nanoseconds, Is.EqualTo(999_999_611L));
      NanosecondTickCount moved = count + new NanosecondTimeSpan(500) - new NanosecondTimeSpan(200);
      Assert.That(moved.Nanoseconds, Is.EqualTo(1_000_000_300L));
      Assert.That(moved - count, Is.EqualTo(new NanosecondTimeSpan(300)));
      Assert.That(count - moved, Is.EqualTo(new NanosecondTimeSpan(-300)));
    }

    [Test]
    public void WrapsAroundAndComparesAcrossTheWrap()
    {
      // One nanosecond past the highest signed value is the lowest: the count wraps
      var last = new NanosecondTickCount(long.MaxValue);
      NanosecondTickCount next = last + new NanosecondTimeSpan(1);
      Assert.That(next.Nanoseconds, Is.EqualTo(long.MinValue));
      Assert.That(next - last, Is.EqualTo(new NanosecondTimeSpan(1)));
      Assert.That(last - next, Is.EqualTo(new NanosecondTimeSpan(-1)));
      Assert.That(last < next, Is.True);
      Assert.That(last <= next, Is.True);
      Assert.That(next > last, Is.True);
      Assert.That(next >= last, Is.True);
      Assert.That(next < last, Is.False);
      Assert.That(last > next, Is.False);
      // And across the unsigned wrap
      NanosecondTickCount before = NanosecondTickCount.FromUnsignedNanoseconds(ulong.MaxValue);
      NanosecondTickCount after = before + new NanosecondTimeSpan(2);
      Assert.That(after.UnsignedNanoseconds, Is.EqualTo(1UL));
      Assert.That(before < after, Is.True);
      Assert.That(after - before, Is.EqualTo(new NanosecondTimeSpan(2)));
      Assert.That(after - new NanosecondTimeSpan(2), Is.EqualTo(before));

      // The plain order of counts close together
      var earlier = new NanosecondTickCount(100);
      var later = new NanosecondTickCount(200);
      Assert.That(earlier < later, Is.True);
      Assert.That(earlier <= later, Is.True);
      Assert.That(earlier <= new NanosecondTickCount(100), Is.True);
      Assert.That(later > earlier, Is.True);
      Assert.That(later >= earlier, Is.True);
      Assert.That(later >= new NanosecondTickCount(200), Is.True);
      Assert.That(earlier != later, Is.True);
      Assert.That(earlier == new NanosecondTickCount(100), Is.True);
      Assert.That(earlier > later, Is.False);
      Assert.That(later <= earlier, Is.False);
    }

    [Test]
    public void EqualsByItsCountAndIsWrittenAsItsCountAndItsUnit()
    {
      var count = new NanosecondTickCount(4_166_389);
      Assert.That(count.Equals(new NanosecondTickCount(4_166_389)), Is.True);
      Assert.That(count.Equals((object)new NanosecondTickCount(4_166_389)), Is.True);
      Assert.That(count.Equals((object)new NanosecondTickCount(1)), Is.False);
      Assert.That(count.Equals("4166389 ns"), Is.False);
      Assert.That(count.GetHashCode(), Is.EqualTo(new NanosecondTickCount(4_166_389).GetHashCode()));
      Assert.That(count.GetHashCode(), Is.Not.EqualTo(new NanosecondTickCount(1).GetHashCode()));
      Assert.That(count.ToString(), Is.EqualTo("4166389 ns"));
      Assert.That(new NanosecondTickCount(-389).ToString(), Is.EqualTo("-389 ns"));
    }
  }
}
