//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Sizing advice for a capture setup (the 'marker-size' command): module size, marker sizes and origin alignment.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
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
      Assert.That(advice.FrameMarkerPx, Is.EqualTo(33 * 12), "25 modules plus a 4 module quiet zone on each side");
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
      Assert.That(advice.MaxStartMarkerPx, Is.GreaterThan(advice.FrameMarkerPx));
    }

    [Test]
    public void InvalidSizes_Throw()
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => MarkerSizing.Advise(0, 1080, 540));
      Assert.Throws<ArgumentOutOfRangeException>(() => MarkerSizing.Advise(1920, 1080, 0));
    }
  }
}
