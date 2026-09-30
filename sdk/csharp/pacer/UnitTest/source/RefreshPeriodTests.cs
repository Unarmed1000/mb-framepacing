//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* RefreshPeriod: exact to 2^-32 tick over an hour, other units, always valid when made by its factories (clamped, as the C++ library does
//* without asserts), default(RefreshPeriod) refused, and refreshes in a time. The same cases as the C++ tests.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using NUnit.Framework;
using static MB.FramePacing.Pacer.UnitTest.TestPacing;

namespace MB.FramePacing.Pacer.UnitTest
{
  [TestFixture]
  public class RefreshPeriodTests
  {
    [Test]
    public void ARationalRate_IsExactOverAnHour()
    {
      Assert.That(Hz60.Ticks, Is.EqualTo(166_667));
      // 60 Hz for an hour is 216 000 refreshes of exactly 1/60 s: no drift, where whole ticks would be 72 ms off
      Assert.That(Hz60.TicksFor(216_000), Is.EqualTo(3_600 * Second));
      Assert.That(Hz60.TicksFor(3), Is.EqualTo(500_000));
      Assert.That(Hz60.TicksFor(1), Is.EqualTo(166_667));
      Assert.That(Hz60.TicksFor(2), Is.EqualTo(333_333));
      // 59.94 Hz as DXGI states it: 60000 refreshes take 1001 s
      var ntsc = RefreshPeriod.FromRate(60_000, 1_001);
      Assert.That(ntsc.TicksFor(60_000), Is.EqualTo(1_001 * Second));
      Assert.That(ntsc.Ticks, Is.EqualTo(166_833));
      // wl_output's mHz
      Assert.That(RefreshPeriod.FromRate(59_940, 1_000).TicksFor(59_940), Is.EqualTo(1_000 * Second));
    }

    [Test]
    public void OtherUnits()
    {
      Assert.That(RefreshPeriod.FromTicks(166_667).Ticks, Is.EqualTo(166_667));
      Assert.That(RefreshPeriod.FromNanoseconds(16_683_350).TicksFor(2), Is.EqualTo(333_667));
      Assert.That(RefreshPeriod.FromNanoseconds(8_333_333).Ticks, Is.EqualTo(83_333));
      // In nanoseconds, as platforms report it
      Assert.That(Hz60.Nanoseconds, Is.EqualTo(16_666_667));
      Assert.That(RefreshPeriod.FromNanoseconds(16_683_350).Nanoseconds, Is.EqualTo(16_683_350));
      Assert.That(RefreshPeriod.FromTicksQ32(Hz60.TicksQ32), Is.EqualTo(Hz60));
    }

    [Test]
    public void ItIsAlwaysValid()
    {
      const long Min = RefreshPeriod.MinTicksQ32;
      const long Max = RefreshPeriod.MaxTicksQ32;
      // The range's ends: 1 tick (10 MHz) and 1 s (1 Hz)
      Assert.That(RefreshPeriod.FromTicks(1).TicksQ32, Is.EqualTo(Min));
      Assert.That(RefreshPeriod.FromRate(10_000_000).TicksQ32, Is.EqualTo(Min));
      Assert.That(RefreshPeriod.FromNanoseconds(100).TicksQ32, Is.EqualTo(Min));
      Assert.That(RefreshPeriod.FromTicks(Second).TicksQ32, Is.EqualTo(Max));
      Assert.That(RefreshPeriod.FromRate(1).TicksQ32, Is.EqualTo(Max));
      Assert.That(RefreshPeriod.FromNanoseconds(1_000_000_000).TicksQ32, Is.EqualTo(Max));
      // A period outside the range is clamped into it
      Assert.That(RefreshPeriod.FromRate(0).TicksQ32, Is.EqualTo(Max));
      Assert.That(RefreshPeriod.FromRate(60, 0).TicksQ32, Is.EqualTo(Min));
      Assert.That(RefreshPeriod.FromRate(1, 2).TicksQ32, Is.EqualTo(Max));
      Assert.That(RefreshPeriod.FromRate(20_000_000).TicksQ32, Is.EqualTo(Min));
      Assert.That(RefreshPeriod.FromTicks(0).TicksQ32, Is.EqualTo(Min));
      Assert.That(RefreshPeriod.FromTicks(long.MaxValue).TicksQ32, Is.EqualTo(Max));
      Assert.That(RefreshPeriod.FromNanoseconds(-1).TicksQ32, Is.EqualTo(Min));
      Assert.That(RefreshPeriod.FromNanoseconds(50).TicksQ32, Is.EqualTo(Min));
      Assert.That(RefreshPeriod.FromNanoseconds(long.MaxValue).TicksQ32, Is.EqualTo(Max));
      Assert.That(RefreshPeriod.FromTicksQ32(0).TicksQ32, Is.EqualTo(Min));
      Assert.That(RefreshPeriod.FromTicksQ32(0).IsDefault, Is.False);
    }

    [Test]
    public void TheDefault_IsNotAPeriod_AndThePacerRefusesIt()
    {
      Assert.That(default(RefreshPeriod).IsDefault, Is.True);
      Assert.That(Hz60.IsDefault, Is.False);
      Assert.That(() => new PacerSettings(default), Throws.ArgumentException);
      Assert.That(() => new AnimationClock(default), Throws.ArgumentException);
      var pacer = new FramePacer(Settings());
      Assert.That(() => pacer.SetRefreshPeriod(default), Throws.ArgumentException);
      Assert.That(() => new SwapIntervalRule(Settings()).AddFrame(0, 0, false, default), Throws.ArgumentException);
      var settings = new PacerSettings(Hz60);
      Assert.That(() => settings.Refresh = default, Throws.ArgumentException);
      Assert.That(settings.Refresh, Is.EqualTo(Hz60));
    }

    [Test]
    public void RefreshesInATime()
    {
      Assert.That(Hz60.FloorRefreshes(166_666), Is.Zero);
      Assert.That(Hz60.FloorRefreshes(166_667), Is.EqualTo(1));
      Assert.That(Hz60.FloorRefreshes(3_600 * Second), Is.EqualTo(216_000));
      Assert.That(Hz60.NearestRefreshes(83_333), Is.Zero);
      Assert.That(Hz60.NearestRefreshes(83_334), Is.EqualTo(1));
      Assert.That(Hz60.NearestRefreshes(250_000), Is.EqualTo(2), "a tie: the later refresh");
      Assert.That(Hz60.NearestRefreshes(-5), Is.Zero);
    }
  }
}
