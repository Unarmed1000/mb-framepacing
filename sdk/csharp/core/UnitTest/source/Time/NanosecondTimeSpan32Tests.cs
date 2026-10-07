//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* NanosecondTimeSpan32: an interval of 0 to 4.294967295 s in nanoseconds, the form of a 32-bit field that holds one. A longer or a
//* negative span does not fit and throws; nothing is cut. The same cases as the C++ core's tests.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.UnitTest
{
  [TestFixture]
  public class NanosecondTimeSpan32Tests
  {
    [Test]
    public void HoldsAnUnsigned32BitCountOfNanoseconds()
    {
      Assert.That(default(NanosecondTimeSpan32).Nanoseconds, Is.EqualTo(0u));
      Assert.That(NanosecondTimeSpan32.Zero.Nanoseconds, Is.EqualTo(0u));
      Assert.That(new NanosecondTimeSpan32(4_166_389).Nanoseconds, Is.EqualTo(4_166_389u));
      Assert.That(NanosecondTimeSpan32.FromNanoseconds(16_666_667), Is.EqualTo(new NanosecondTimeSpan32(16_666_667)));
      // 4.294967295 s: every refresh period a display has fits with room
      Assert.That(NanosecondTimeSpan32.MaxValue.Nanoseconds, Is.EqualTo(uint.MaxValue));
    }

    [Test]
    public void IsMadeFromASpanThatFitsAndThrowsForOneThatDoesNot()
    {
      Assert.That(NanosecondTimeSpan32.FromNanosecondTimeSpan(new NanosecondTimeSpan(4_166_389)).Nanoseconds, Is.EqualTo(4_166_389u));
      Assert.That(NanosecondTimeSpan32.FromNanosecondTimeSpan(NanosecondTimeSpan.Zero), Is.EqualTo(NanosecondTimeSpan32.Zero));
      Assert.That(NanosecondTimeSpan32.FromNanosecondTimeSpan(new NanosecondTimeSpan(4_294_967_295)), Is.EqualTo(NanosecondTimeSpan32.MaxValue));
      Assert.That(
        () => NanosecondTimeSpan32.FromNanosecondTimeSpan(new NanosecondTimeSpan(4_294_967_296)),
        Throws.TypeOf<ArgumentOutOfRangeException>()
      );
      Assert.That(() => NanosecondTimeSpan32.FromNanosecondTimeSpan(new NanosecondTimeSpan(-1)), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => NanosecondTimeSpan32.FromNanosecondTimeSpan(NanosecondTimeSpan.MaxValue), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => NanosecondTimeSpan32.FromNanosecondTimeSpan(NanosecondTimeSpan.MinValue), Throws.TypeOf<ArgumentOutOfRangeException>());

      // From ticks of 100 ns, exactly: 42,949,672 ticks is the longest that fits
      Assert.That(NanosecondTimeSpan32.FromTimeSpan(new TimeSpan(41_664)).Nanoseconds, Is.EqualTo(4_166_400u));
      Assert.That(NanosecondTimeSpan32.FromTimeSpan(TimeSpan.Zero), Is.EqualTo(NanosecondTimeSpan32.Zero));
      Assert.That(NanosecondTimeSpan32.FromTimeSpan(new TimeSpan(42_949_672)).Nanoseconds, Is.EqualTo(4_294_967_200u));
      Assert.That(() => NanosecondTimeSpan32.FromTimeSpan(new TimeSpan(42_949_673)), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => NanosecondTimeSpan32.FromTimeSpan(new TimeSpan(-1)), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => NanosecondTimeSpan32.FromTimeSpan(TimeSpan.MaxValue), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void WidensToASpanExactlyAndToTicksTruncated()
    {
      Assert.That(new NanosecondTimeSpan32(4_166_389).ToNanosecondTimeSpan(), Is.EqualTo(new NanosecondTimeSpan(4_166_389)));
      Assert.That(NanosecondTimeSpan32.MaxValue.ToNanosecondTimeSpan(), Is.EqualTo(new NanosecondTimeSpan(4_294_967_295)));
      Assert.That(NanosecondTimeSpan32.Zero.ToNanosecondTimeSpan(), Is.EqualTo(NanosecondTimeSpan.Zero));
      Assert.That(new NanosecondTimeSpan32(4_166_389).ToTimeSpan(), Is.EqualTo(new TimeSpan(41_663)));
      Assert.That(new NanosecondTimeSpan32(99).ToTimeSpan(), Is.EqualTo(TimeSpan.Zero));
      Assert.That(NanosecondTimeSpan32.MaxValue.ToTimeSpan(), Is.EqualTo(new TimeSpan(42_949_672)));
    }

    [Test]
    public void ComparesByItsCountAndIsWrittenWithItsUnit()
    {
      var shorter = new NanosecondTimeSpan32(4_166_389);
      var longer = new NanosecondTimeSpan32(16_666_667);
      Assert.That(shorter < longer, Is.True);
      Assert.That(shorter <= longer, Is.True);
      Assert.That(longer > shorter, Is.True);
      Assert.That(longer >= shorter, Is.True);
      Assert.That(longer < shorter, Is.False);
      Assert.That(longer <= shorter, Is.False);
      Assert.That(shorter > longer, Is.False);
      Assert.That(shorter >= longer, Is.False);
      Assert.That(shorter != longer, Is.True);
      Assert.That(shorter == new NanosecondTimeSpan32(4_166_389), Is.True);
      Assert.That(shorter.CompareTo(longer), Is.LessThan(0));
      Assert.That(longer.CompareTo(shorter), Is.GreaterThan(0));
      Assert.That(shorter.CompareTo(shorter), Is.EqualTo(0));
      Assert.That(shorter.Equals(new NanosecondTimeSpan32(4_166_389)), Is.True);
      Assert.That(shorter.Equals((object)new NanosecondTimeSpan32(4_166_389)), Is.True);
      Assert.That(shorter.Equals((object)longer), Is.False);
      Assert.That(shorter.Equals("4166389 ns"), Is.False);
      Assert.That(shorter.GetHashCode(), Is.EqualTo(new NanosecondTimeSpan32(4_166_389).GetHashCode()));
      Assert.That(shorter.GetHashCode(), Is.Not.EqualTo(longer.GetHashCode()));
      Assert.That(shorter.ToString(), Is.EqualTo("4166389 ns"));
      Assert.That(NanosecondTimeSpan32.Zero.ToString(), Is.EqualTo("0 ns"));
    }
  }
}
