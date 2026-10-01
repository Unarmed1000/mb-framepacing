//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* TimeSpanUtil: seconds to a TimeSpan, truncated toward zero to a tick on every runtime (the C++ core's TimeSpan::FromSeconds cases).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.UnitTest
{
  [TestFixture]
  public class TimeSpanUtilTests
  {
    [Test]
    public void SecondsAreTruncatedTowardZeroToATick()
    {
      Assert.That(TimeSpanUtil.FromSeconds(0).Ticks, Is.EqualTo(0));
      Assert.That(TimeSpanUtil.FromSeconds(1).Ticks, Is.EqualTo(10_000_000));
      // Not a whole millisecond, as .NET Framework's and Mono's TimeSpan.FromSeconds give (17 ms)
      Assert.That(TimeSpanUtil.FromSeconds(1.0 / 60).Ticks, Is.EqualTo(166_666));
      Assert.That(TimeSpanUtil.FromSeconds(0.016_666_79).Ticks, Is.EqualTo(166_667));
      Assert.That(TimeSpanUtil.FromSeconds(-1.0 / 60).Ticks, Is.EqualTo(-166_666));
      Assert.That(TimeSpanUtil.FromSeconds(0.000_000_15).Ticks, Is.EqualTo(1));
      Assert.That(TimeSpanUtil.FromSeconds(-0.000_000_15).Ticks, Is.EqualTo(-1));
      Assert.That(TimeSpanUtil.FromSeconds(3600.5).Ticks, Is.EqualTo(36_005_000_000));
    }

    [Test]
    public void OutsideTheRangeOfATimeSpanItThrows()
    {
      // 2^63 ticks, which is what MaxValue's ticks round to as a double, is MaxValue
      Assert.That(TimeSpanUtil.FromSeconds(922_337_203_685.477_580_8), Is.EqualTo(TimeSpan.MaxValue));
      Assert.That(TimeSpanUtil.FromSeconds(-922_337_203_685.477_580_8), Is.EqualTo(TimeSpan.MinValue));
      Assert.That(() => TimeSpanUtil.FromSeconds(1e12), Throws.TypeOf<OverflowException>());
      Assert.That(() => TimeSpanUtil.FromSeconds(-1e12), Throws.TypeOf<OverflowException>());
      Assert.That(() => TimeSpanUtil.FromSeconds(double.PositiveInfinity), Throws.TypeOf<OverflowException>());
      Assert.That(() => TimeSpanUtil.FromSeconds(double.NegativeInfinity), Throws.TypeOf<OverflowException>());
      Assert.That(() => TimeSpanUtil.FromSeconds(double.NaN), Throws.TypeOf<ArgumentException>());
    }
  }
}
