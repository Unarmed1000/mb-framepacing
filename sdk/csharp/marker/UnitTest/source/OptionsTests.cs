//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options: always valid (a value outside its range is clamped, as the C++ library does without asserts), and default(Options) is the default.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class OptionsTests
  {
    [Test]
    public void TheDefault_IsSixPixelModulesAndTheRecommendedQuietZone()
    {
      Assert.That((Options.Default.ModuleSizePx, Options.Default.QuietZoneModules), Is.EqualTo((6, Options.RecommendedQuietZoneModules)));
      Assert.That(Options.DefaultModuleSizePx, Is.EqualTo(6));
      Assert.That(default(Options), Is.EqualTo(new Options(6, 4)));
      Assert.That(new Options(3).QuietZoneModules, Is.EqualTo(Options.RecommendedQuietZoneModules));
      Assert.That(new Options(3, 4).QuietZonePx, Is.EqualTo(12));
    }

    [Test]
    public void AValueOutsideItsRange_IsClamped()
    {
      Assert.That(new Options(0, 4), Is.EqualTo(new Options(Options.MinModuleSizePx, 4)));
      Assert.That(new Options(-5, 4).ModuleSizePx, Is.EqualTo(Options.MinModuleSizePx));
      Assert.That(new Options(Options.MaxModuleSizePx + 1, 4).ModuleSizePx, Is.EqualTo(Options.MaxModuleSizePx));
      Assert.That(new Options(6, -1).QuietZoneModules, Is.Zero);
      Assert.That(new Options(6, Options.MaxQuietZoneModules + 1).QuietZoneModules, Is.EqualTo(Options.MaxQuietZoneModules));
      // The limits themselves are kept
      Assert.That(new Options(Options.MinModuleSizePx, 0).ModuleSizePx, Is.EqualTo(Options.MinModuleSizePx));
      Assert.That(new Options(Options.MaxModuleSizePx, Options.MaxQuietZoneModules).QuietZoneModules, Is.EqualTo(Options.MaxQuietZoneModules));
    }

    [Test]
    public void Equality_FollowsTheValues()
    {
      Assert.That(new Options(3, 2) == new Options(3, 2), Is.True);
      Assert.That(new Options(3, 2) != new Options(3, 1), Is.True);
      var clamped = new Options(0, 2);
      Assert.That(clamped.GetHashCode(), Is.EqualTo(new Options(1, 2).GetHashCode()));
      Assert.That(new Options(3, 2).ToString(), Is.EqualTo("{module 3 px, quiet zone 2}"));
    }
  }
}
