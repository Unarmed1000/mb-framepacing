//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The playback page's zoom steps (PlaybackZoom.Steps): every step shorter than the report while the zoomed cards fit the size budget,
//* coarsest first, so a short run gets every step and an hour the coarse one; and the scrolling layers the page moves
//* (SvgCardWriter.Write with scrollLayers), which files never have.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MB.FramePacing.Analysis.UnitTest;
using MB.FramePacing.Charts.Playback;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class PlaybackZoomTests
  {
    private static readonly double g_plotWidth = ReportCard.PlotWidth(ReportCard.Width);

    [TestCase(1.0, 60, new double[0])]
    [TestCase(8.0, 336, new[] { 2.0 })]
    [TestCase(120.0, 7_200, new[] { 60.0, 10.0, 2.0 })]
    [TestCase(600.0, 36_000, new[] { 60.0, 10.0, 2.0 })]
    [TestCase(1800.0, 108_000, new[] { 60.0, 10.0 })]
    [TestCase(3600.0, 216_000, new[] { 60.0 })]
    [TestCase(3600.0, 864_000, new[] { 60.0 })]
    public void Steps_ShorterThanTheReport_WithinTheBudget(double seconds, int frames, double[] expected)
    {
      Assert.That(PlaybackZoom.Steps(seconds, frames, g_plotWidth), Is.EqualTo(expected));
    }

    [Test]
    public void Steps_NeverExceedTheBudget()
    {
      foreach (double seconds in new[] { 30.0, 300.0, 1200.0, 7200.0 })
      {
        foreach (int rate in new[] { 60, 144, 240, 500 })
        {
          int frames = (int)(seconds * rate);
          double estimate = PlaybackZoom
            .Steps(seconds, frames, g_plotWidth)
            .Sum(step => PlaybackZoom.BytesPerElement * System.Math.Min(frames, seconds / step * g_plotWidth));
          Assert.That(estimate, Is.LessThanOrEqualTo(PlaybackZoom.ByteBudget), $"{seconds} s at {rate} Hz");
        }
      }
    }

    [Test]
    public void ScrollLayers_AreKept_OnlyWhenAskedFor()
    {
      string golden = Path.GetFullPath(Path.Combine(VideoClips.Directory(), "..", "..", "..", "sdk", "test-data", "data", "60-busy-full-rate"));
      var section = RunSection.Whole(AnalysisOutput.Read(golden).Single().Chart);
      var card = ReportCard.Build(section, visible: (0, 2));

      string file = SvgCardWriter.Write(card);
      string page = SvgCardWriter.Write(card, null, "pb-card-1");

      Assert.That(file, Does.Not.Contain(SvgCardWriter.ScrollLayerClass).And.Not.Contain("clipPath"), "a file shows exactly its range");
      int layers = card.Shapes.Count(s => s is ScrollShape);
      Assert.That(layers, Is.GreaterThan(0));
      Assert.That(Regex.Matches(page, $"<g class=\"{SvgCardWriter.ScrollLayerClass}\">").Count, Is.EqualTo(layers));
      Assert.That(Regex.Matches(page, "<clipPath id=\"pb-card-1-clip-\\d+\">").Count, Is.EqualTo(layers));
    }
  }
}
