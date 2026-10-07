//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* NanosecondTickCount: a point on a clock that counts in nanoseconds, stored unsigned so it wraps and compares across the wrap, as
//* TickCount64 does in ticks of 100 ns. To a TickCount64 it is the tick the point is in. The same cases as the C++ core's tests, and a
//* counter with the cases of TickCount64.FromCounter.
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
    public void ACounterConvertsExactlyAtAnyValue()
    {
      // A 10 MHz counter (Stopwatch.Frequency on current Windows) counts in ticks of 100 ns
      Assert.That(NanosecondTickCount.FromCounter(123_456_789, 10_000_000).Nanoseconds, Is.EqualTo(12_345_678_900L));
      Assert.That(NanosecondTickCount.FromCounter(20_000_000, 10_000_000).Nanoseconds, Is.EqualTo(2_000_000_000L));
      Assert.That(NanosecondTickCount.FromCounter(-1, 10_000_000).Nanoseconds, Is.EqualTo(-100L));
      Assert.That(
        NanosecondTickCount.FromCounter(123_456_789, 10_000_000),
        Is.EqualTo(NanosecondTickCount.FromTickCount64(TickCount64.FromCounter(123_456_789, 10_000_000)))
      );
      // A 1 GHz counter (Stopwatch.Frequency on Linux and macOS) is in nanoseconds already, at every value
      Assert.That(NanosecondTickCount.FromCounter(123_456_789, 1_000_000_000).Nanoseconds, Is.EqualTo(123_456_789L));
      Assert.That(NanosecondTickCount.FromCounter(4_000_000_001, 1_000_000_000).Nanoseconds, Is.EqualTo(4_000_000_001L));
      Assert.That(NanosecondTickCount.FromCounter(0, 1_000_000_000).Nanoseconds, Is.EqualTo(0L));
      Assert.That(NanosecondTickCount.FromCounter(-150, 1_000_000_000).Nanoseconds, Is.EqualTo(-150L));
      Assert.That(NanosecondTickCount.FromCounter(long.MaxValue, 1_000_000_000).Nanoseconds, Is.EqualTo(long.MaxValue));
      Assert.That(NanosecondTickCount.FromCounter(long.MinValue, 1_000_000_000).Nanoseconds, Is.EqualTo(long.MinValue));
      // A counter near its limit does not overflow: 2^63 - 1 at 3 GHz is about 97 years
      const long Frequency = 3_000_000_000;
      const long NanosecondsPerSecond = NanosecondTickCount.NanosecondsPerSecond;
      Assert.That(
        NanosecondTickCount.FromCounter(long.MaxValue, Frequency).Nanoseconds,
        Is.EqualTo(((long.MaxValue / Frequency) * NanosecondsPerSecond) + (((long.MaxValue % Frequency) * NanosecondsPerSecond) / Frequency))
      );
      // The fastest counter it takes, at its limit too
      const long Fastest = NanosecondTickCount.MaxCounterFrequency;
      Assert.That(Fastest, Is.EqualTo(9_223_372_036L));
      Assert.That(NanosecondTickCount.FromCounter(Fastest, Fastest).Nanoseconds, Is.EqualTo(NanosecondsPerSecond));
      Assert.That(
        NanosecondTickCount.FromCounter(long.MaxValue, Fastest).Nanoseconds,
        Is.EqualTo(((long.MaxValue / Fastest) * NanosecondsPerSecond) + (((long.MaxValue % Fastest) * NanosecondsPerSecond) / Fastest))
      );
    }

    [Test]
    public void ACounterIsRoundedDownToTheNanosecondItIsIn()
    {
      // Three counts a second: a third of a second is 333,333,333.33 ns
      Assert.That(NanosecondTickCount.FromCounter(1, 3).Nanoseconds, Is.EqualTo(333_333_333L));
      Assert.That(NanosecondTickCount.FromCounter(2, 3).Nanoseconds, Is.EqualTo(666_666_666L));
      Assert.That(NanosecondTickCount.FromCounter(3, 3).Nanoseconds, Is.EqualTo(1_000_000_000L));
      Assert.That(NanosecondTickCount.FromCounter(4, 3).Nanoseconds, Is.EqualTo(1_333_333_333L));
      // A 3 GHz counter: one second and a third of a nanosecond, then a whole one
      Assert.That(NanosecondTickCount.FromCounter(3_000_000_001, 3_000_000_000).Nanoseconds, Is.EqualTo(1_000_000_000L));
      Assert.That(NanosecondTickCount.FromCounter(3_000_000_002, 3_000_000_000).Nanoseconds, Is.EqualTo(1_000_000_000L));
      Assert.That(NanosecondTickCount.FromCounter(3_000_000_003, 3_000_000_000).Nanoseconds, Is.EqualTo(1_000_000_001L));
      // Before the epoch it is rounded down too, not toward zero
      Assert.That(NanosecondTickCount.FromCounter(-1, 3).Nanoseconds, Is.EqualTo(-333_333_334L));
      Assert.That(NanosecondTickCount.FromCounter(-2, 3).Nanoseconds, Is.EqualTo(-666_666_667L));
      Assert.That(NanosecondTickCount.FromCounter(-3, 3).Nanoseconds, Is.EqualTo(-1_000_000_000L));
      Assert.That(NanosecondTickCount.FromCounter(-4, 3).Nanoseconds, Is.EqualTo(-1_333_333_334L));
      Assert.That(NanosecondTickCount.FromCounter(-1, 3_000_000_000).Nanoseconds, Is.EqualTo(-1L));
      Assert.That(NanosecondTickCount.FromCounter(-3, 3_000_000_000).Nanoseconds, Is.EqualTo(-1L));
      Assert.That(NanosecondTickCount.FromCounter(-4, 3_000_000_000).Nanoseconds, Is.EqualTo(-2L));
    }

    [Test]
    public void ASlowCounterPastTheRangeWrapsAsTheCountDoes()
    {
      // A counter slower than the nanosecond can count more seconds than a NanosecondTickCount holds (2^63 - 1 seconds at 1 Hz): the count
      // wraps, as every NanosecondTickCount does, and the difference of two such counts is still their distance
      long wrapped = unchecked((long)((ulong)long.MaxValue * (ulong)NanosecondTickCount.NanosecondsPerSecond));
      Assert.That(NanosecondTickCount.FromCounter(long.MaxValue, 1).Nanoseconds, Is.EqualTo(wrapped));
      Assert.That(
        NanosecondTickCount.FromCounter(long.MaxValue, 1) - NanosecondTickCount.FromCounter(long.MaxValue - 3, 1),
        Is.EqualTo(NanosecondTimeSpan.FromSeconds(3))
      );
      Assert.That(
        NanosecondTickCount.FromCounter(long.MinValue, 1000) - NanosecondTickCount.FromCounter(long.MinValue + 1, 1000),
        Is.EqualTo(NanosecondTimeSpan.FromMilliseconds(-1))
      );
      // The first counter value at 1 Hz that is past the range: 9,223,372,037 s is 2^63 ns and 145,224,192 more
      Assert.That(NanosecondTickCount.FromCounter(9_223_372_036, 1).Nanoseconds, Is.EqualTo(9_223_372_036_000_000_000L));
      Assert.That(NanosecondTickCount.FromCounter(9_223_372_037, 1).Nanoseconds, Is.EqualTo(long.MinValue + 145_224_192L));
    }

    [Test]
    public void ACounterFrequencyOutsideItsRangeThrows()
    {
      Assert.That(() => NanosecondTickCount.FromCounter(123, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => NanosecondTickCount.FromCounter(123, -1_000_000_000), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => NanosecondTickCount.FromCounter(123, long.MinValue), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(
        () => NanosecondTickCount.FromCounter(123, NanosecondTickCount.MaxCounterFrequency + 1),
        Throws.TypeOf<ArgumentOutOfRangeException>().With.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("frequency")
      );
      Assert.That(() => NanosecondTickCount.FromCounter(123, long.MaxValue), Throws.TypeOf<ArgumentOutOfRangeException>());
      // The ends of the range are taken
      Assert.That(NanosecondTickCount.FromCounter(123, 1).Nanoseconds, Is.EqualTo(123_000_000_000L));
      Assert.That(NanosecondTickCount.FromCounter(123, NanosecondTickCount.MaxCounterFrequency).Nanoseconds, Is.EqualTo(13L));
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
