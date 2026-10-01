//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* TimeSpan32: the marker's 32-bit intervals, converted to and from a TimeSpan exactly (the same cases as the C++ core's tests).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.UnitTest
{
  [TestFixture]
  public class TimeSpan32Tests
  {
    [Test]
    public void HoldsZeroToTheLargestUnsigned32BitTicks()
    {
      Assert.That(default(TimeSpan32).Ticks, Is.EqualTo(0u));
      Assert.That(TimeSpan32.Zero.Ticks, Is.EqualTo(0u));
      Assert.That(TimeSpan32.MaxValue.Ticks, Is.EqualTo(uint.MaxValue));
      Assert.That(new TimeSpan32(166_667u).Ticks, Is.EqualTo(166_667u));
      Assert.That(TimeSpan32.FromTicks(80_000u).Ticks, Is.EqualTo(80_000u));
    }

    [Test]
    public void ConvertsFromATimeSpanExactlyOrThrows()
    {
      Assert.That(TimeSpan32.FromTimeSpan(TimeSpan.Zero).Ticks, Is.EqualTo(0u));
      Assert.That(TimeSpan32.FromTimeSpan(TimeSpan.FromMilliseconds(16)).Ticks, Is.EqualTo(160_000u));
      Assert.That(TimeSpan32.FromTimeSpan(new TimeSpan(4_294_967_295)), Is.EqualTo(TimeSpan32.MaxValue));
      Assert.That(() => TimeSpan32.FromTimeSpan(new TimeSpan(4_294_967_296)), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => TimeSpan32.FromTimeSpan(new TimeSpan(-1)), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(TimeSpan32.MaxValue.ToTimeSpan().Ticks, Is.EqualTo(4_294_967_295L));
      Assert.That(new TimeSpan32(166_667u).ToTimeSpan(), Is.EqualTo(new TimeSpan(166_667)));
    }

    [Test]
    public void ComparesByTicks()
    {
      var shorter = new TimeSpan32(3u);
      var longer = new TimeSpan32(4u);
      Assert.That(shorter == new TimeSpan32(3u), Is.True);
      Assert.That(shorter != longer, Is.True);
      Assert.That(shorter < longer, Is.True);
      Assert.That(longer < shorter, Is.False);
      Assert.That(shorter <= new TimeSpan32(3u), Is.True);
      Assert.That(longer <= shorter, Is.False);
      Assert.That(longer > shorter, Is.True);
      Assert.That(shorter > longer, Is.False);
      Assert.That(longer >= shorter, Is.True);
      Assert.That(shorter >= longer, Is.False);
      Assert.That(shorter.CompareTo(longer), Is.LessThan(0));
      Assert.That(longer.CompareTo(shorter), Is.GreaterThan(0));
      Assert.That(shorter.CompareTo(new TimeSpan32(3u)), Is.EqualTo(0));
    }

    [Test]
    public void ComparesByValue_AndWritesTheSpan()
    {
      var span = new TimeSpan32(166_667u);
      Assert.That(span.Equals(new TimeSpan32(166_667u)), Is.True);
      Assert.That(span.Equals((object)new TimeSpan32(166_667u)), Is.True);
      Assert.That(span.Equals((object)new TimeSpan32(166_668u)), Is.False);
      Assert.That(span.Equals("166667"), Is.False);
      Assert.That(span.GetHashCode(), Is.EqualTo(new TimeSpan32(166_667u).GetHashCode()));
      Assert.That(span.GetHashCode(), Is.Not.EqualTo(new TimeSpan32(166_668u).GetHashCode()));
      Assert.That(span.ToString(), Is.EqualTo("00:00:00.0166667"));
      Assert.That(TimeSpan32.MaxValue.ToString(), Is.EqualTo("00:07:09.4967295"));
    }
  }
}
