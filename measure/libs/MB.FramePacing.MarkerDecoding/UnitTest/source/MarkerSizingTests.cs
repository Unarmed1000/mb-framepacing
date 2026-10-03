//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Sizing advice for a capture setup (the 'marker-size' command): module size, marker sizes and origin alignment.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.MarkerDecoding.UnitTest
{
  [TestFixture]
  public class MarkerSizingTests
  {
    [Test]
    public void FourK_StoredAt540p_Uses12PxModules_AlignedToTheRatio()
    {
      var advice = MarkerSizing.Advise(3840, 2160, 540);
      Assert.That(advice.RecommendedModulePx, Is.EqualTo(12), "3 stored pixels per module at a 4:1 downscale");
      Assert.That(advice.MinimumModulePx, Is.EqualTo(8));
      Assert.That(advice.StoredPxPerModule, Is.EqualTo(3.0));
      Assert.That(advice.MarkerPx, Is.EqualTo(49 * 12), "41 modules plus a 4 module quiet zone on each side");
      Assert.That(advice.AlignPx, Is.EqualTo(4));
      Assert.That(advice.OriginX % 4, Is.Zero);
      Assert.That(advice.OriginY % 4, Is.Zero);
      Assert.That(advice.NonIntegerRatio, Is.False);
    }

    [Test]
    public void Mjpeg_NeedsFourStoredPixels_AndANonIntegerRatioIsReported()
    {
      var advice = MarkerSizing.Advise(2560, 1440, 1080, mjpeg: true);
      Assert.That(advice.RecommendedModulePx, Is.EqualTo(6), "4 stored pixels at 1440 -> 1080 need 5.33, rounded up to 6");
      Assert.That(advice.StoredPxPerModule, Is.EqualTo(4.5));
      Assert.That(advice.AlignPx, Is.EqualTo(1));
      Assert.That(advice.NonIntegerRatio, Is.True);
    }

    [Test]
    public void Advice_MatchesTheMarkerLibrary()
    {
      var advice = MarkerSizing.Advise(1920, 1080, 1080);
      Assert.That(advice.RecommendedModulePx, Is.EqualTo(MarkerRenderer.RecommendModuleSizePx(1080, 1080)));
      Assert.That(advice.MinimumModulePx, Is.EqualTo(MarkerRenderer.MinimumModuleSizePx(1080, 1080)));
      Assert.That(advice.MarkerPx, Is.EqualTo(MarkerRenderer.MarkerSizePx(advice.RecommendedModulePx)));
    }

    /// <summary>
    /// WCAG 2.3.1's own numbers: on 1024 x 768 a 10 degree field is 341 x 256 pixels, a quarter of it (21,824 pixels) may flash, and a
    /// fine pattern's squares must be smaller than 0.1 degree.
    /// </summary>
    [Test]
    public void FlashGuideline_IsWcagsEstimate()
    {
      Assert.That(FlashGuideline.AreaLimit * 1024 * 768, Is.EqualTo(21_824).Within(30), "a quarter of 341 x 256 pixels");
      Assert.That(FlashGuideline.ModuleDegrees(341, 1024), Is.EqualTo(10).Within(0.01), "341 of 1024 pixels are 10 degrees");
      Assert.That(FlashGuideline.AreaShare(6, 1920, 1080), Is.EqualTo(246.0 * 246 / (1920 * 1080)).Within(1e-12), "the 41 x 41 symbol");
    }

    [TestCase(1920, 1080, 540, true, true, Description = "the library default, 6 px: a hair over the area, exempt as a fine pattern")]
    [TestCase(3840, 2160, 540, true, true, Description = "12 px on 2160p: the same share of the screen")]
    [TestCase(1920, 1080, 1080, true, true, Description = "3 px: small and fine")]
    [TestCase(1920, 1080, 360, false, false, Description = "9 px on 1080p: too large and too coarse")]
    [TestCase(1280, 720, 360, false, false, Description = "6 px on 720p: too large and too coarse")]
    public void FlashGuideline_ForASetup(int width, int height, int storedHeight, bool finePattern, bool within)
    {
      var advice = MarkerSizing.Advise(width, height, storedHeight);
      Assert.That(advice.FinePattern, Is.EqualTo(finePattern));
      Assert.That(advice.WithinFlashGuideline, Is.EqualTo(within));
    }

    [Test]
    public void FlashGuideline_ASmallMarkerOfCoarseModules_IsWithinIt()
    {
      // A portrait screen: the modules are too coarse for the exemption (the width sets their size in view), the symbol is still
      // under the area that may flash. On a 16:9 screen the area runs out first
      Assert.That(FlashGuideline.ModuleDegrees(5, 1080), Is.GreaterThan(FlashGuideline.FinePatternDegrees));
      Assert.That(FlashGuideline.AreaShare(5, 1080, 1920), Is.LessThan(FlashGuideline.AreaLimit));
      Assert.That(FlashGuideline.AreaShare(5, 1920, 1080), Is.LessThan(FlashGuideline.AreaLimit), "the same area, landscape");
      Assert.That(FlashGuideline.ModuleDegrees(5, 1920), Is.LessThan(FlashGuideline.FinePatternDegrees), "where 5 px are fine");
    }

    [Test]
    public void InvalidSizes_Throw()
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => MarkerSizing.Advise(0, 1080, 540));
      Assert.Throws<ArgumentOutOfRangeException>(() => MarkerSizing.Advise(1920, 1080, 0));
    }
  }
}
