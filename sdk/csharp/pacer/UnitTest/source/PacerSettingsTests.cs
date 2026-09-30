//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* PacerSettings: the refresh is required, the rest are Swappy's defaults, and every value is kept in its range (clamped, as the C++ library does
//* without asserts). The pacer copies the settings it is made with.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using NUnit.Framework;
using static MB.FramePacing.Pacer.UnitTest.TestPacing;

namespace MB.FramePacing.Pacer.UnitTest
{
  [TestFixture]
  public class PacerSettingsTests
  {
    [Test]
    public void TheRefreshIsRequired_AndTheRestAreSwappysDefaults()
    {
      var defaults = new PacerSettings(Hz60);
      Assert.That(defaults.Refresh, Is.EqualTo(Hz60));
      Assert.That((defaults.PreferredSwapInterval, defaults.AutoSwapInterval), Is.EqualTo((1u, true)));
      Assert.That(defaults.SlowDown, Is.EqualTo(SlowDownRule.LateCount));
      Assert.That((defaults.WindowTicks, defaults.SlowDownLatePercent), Is.EqualTo((2 * Second, 10u)));
      Assert.That((defaults.FrameMarginTicks, defaults.SlowestFrameTicks), Is.EqualTo((Ms, 50 * Ms)));
      Assert.That((defaults.PresentLatencyTicks, defaults.WindowCapacity), Is.EqualTo((0L, 0u)));
    }

    [Test]
    public void EveryValue_IsKeptInItsRange()
    {
      var settings = new PacerSettings(Hz60)
      {
        // The range's ends are kept
        PreferredSwapInterval = PacerSettings.MaxSwapInterval,
        WindowTicks = 1,
        SlowDownLatePercent = 0,
        WindowCapacity = PacerSettings.MinWindowCapacity,
      };
      Assert.That(settings.PreferredSwapInterval, Is.EqualTo(PacerSettings.MaxSwapInterval));
      Assert.That(settings.WindowTicks, Is.EqualTo(1));
      Assert.That(settings.SlowDownLatePercent, Is.Zero);
      Assert.That(settings.WindowCapacity, Is.EqualTo(PacerSettings.MinWindowCapacity));
      settings.WindowCapacity = 0;
      Assert.That(settings.WindowCapacity, Is.Zero);
      // A value outside its range is clamped into it
      settings.PreferredSwapInterval = 0;
      Assert.That(settings.PreferredSwapInterval, Is.EqualTo(1u));
      settings.PreferredSwapInterval = PacerSettings.MaxSwapInterval + 1;
      Assert.That(settings.PreferredSwapInterval, Is.EqualTo(PacerSettings.MaxSwapInterval));
      settings.SlowDown = (SlowDownRule)7;
      Assert.That(settings.SlowDown, Is.EqualTo(SlowDownRule.LateCount));
      settings.WindowTicks = 0;
      Assert.That(settings.WindowTicks, Is.EqualTo(1));
      settings.WindowTicks = PacerSettings.MaxWindowTicks + 1;
      Assert.That(settings.WindowTicks, Is.EqualTo(PacerSettings.MaxWindowTicks));
      settings.SlowDownLatePercent = 101;
      Assert.That(settings.SlowDownLatePercent, Is.EqualTo(100u));
      settings.FrameMarginTicks = -1;
      Assert.That(settings.FrameMarginTicks, Is.Zero);
      settings.SlowestFrameTicks = PacerSettings.MaxSlowestFrameTicks + 1;
      Assert.That(settings.SlowestFrameTicks, Is.EqualTo(PacerSettings.MaxSlowestFrameTicks));
      settings.PresentLatencyTicks = -5;
      Assert.That(settings.PresentLatencyTicks, Is.Zero);
      settings.WindowCapacity = 1;
      Assert.That(settings.WindowCapacity, Is.EqualTo(PacerSettings.MinWindowCapacity));
      settings.WindowCapacity = PacerSettings.MaxWindowCapacity + 1;
      Assert.That(settings.WindowCapacity, Is.EqualTo(PacerSettings.MaxWindowCapacity));
    }

    [Test]
    public void ThePacer_CopiesTheSettingsItIsMadeWith()
    {
      var settings = new PacerSettings(Hz60) { PreferredSwapInterval = 2 };
      var pacer = new FramePacer(settings);
      settings.PreferredSwapInterval = 3;
      Assert.That(pacer.SwapInterval, Is.EqualTo(2u));
      Assert.That(pacer.Settings.PreferredSwapInterval, Is.EqualTo(2u));
      pacer.Settings.PreferredSwapInterval = 4;
      Assert.That(pacer.Settings.PreferredSwapInterval, Is.EqualTo(2u), "Settings is a copy");
      Assert.That(pacer.BeginFrame(new FrameInput(Second)).SwapInterval, Is.EqualTo(2u));
    }
  }
}
