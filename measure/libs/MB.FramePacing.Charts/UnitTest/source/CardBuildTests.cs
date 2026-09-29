//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Building the GUI's cards off its thread: only the latest request's cards are kept (a request a newer one overtook gets nothing, and its
//* build is cancelled), the parallel build draws exactly what the card builders draw one by one, and the Timeline's sliding window shows
//* its visible range and draws the same shapes as its neighbours at one zoom.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class CardBuildTests
  {
    /// <summary>A slow request overtaken by a newer one returns nothing and sees its token cancelled; the newer one's result counts.</summary>
    [Test]
    public async Task LatestRequest_ANewerRequestWins()
    {
      var requests = new LatestRequest<string>();
      using var started = new ManualResetEventSlim();
      using var release = new ManualResetEventSlim();
      bool cancelled = false;
      var older = requests.Run(token =>
      {
        started.Set();
        release.Wait(TimeSpan.FromSeconds(10));
        cancelled = token.IsCancellationRequested;
        return "older";
      });
      Assert.That(started.Wait(TimeSpan.FromSeconds(10)), "the older request runs");
      var newer = requests.Run(_ => "newer");
      Assert.That(await newer, Is.EqualTo("newer"));
      release.Set();
      Assert.That(await older, Is.Null, "a request a newer one overtook never replaces its result");
      Assert.That(cancelled, "the overtaken build is told to stop");
    }

    [Test]
    public async Task LatestRequest_Cancel_DropsTheRunningRequest()
    {
      var requests = new LatestRequest<string>();
      using var release = new ManualResetEventSlim();
      var running = requests.Run(_ =>
      {
        release.Wait(TimeSpan.FromSeconds(10));
        return "done";
      });
      requests.Cancel();
      release.Set();
      Assert.That(await running, Is.Null);
      Assert.That(await requests.Run(_ => "again"), Is.EqualTo("again"), "later requests run as before");
    }

    [Test]
    public void LatestRequest_PassesOnOtherErrors()
    {
      var requests = new LatestRequest<string>();
      Assert.ThrowsAsync<InvalidOperationException>(() => requests.Run(_ => throw new InvalidOperationException("broken")));
    }

    /// <summary>A section's histograms from the wavelet matrix are exactly RunHistograms' of the section's frames: bins, widths and counts.</summary>
    [Test]
    public void SectionHistograms_AreRunHistograms()
    {
      var run = ReportSvgTests.Synthetic(240 * 60);
      foreach (var section in new[] { RunSection.Whole(run), RunSection.Create(run, 10, 12.5), RunSection.Create(run, 30, 30.01) })
      {
        var expected = MB.FramePacing.Analysis.RunHistograms.Create(section.Section.Run);
        foreach (
          var (actual, wanted) in new[]
          {
            (SectionHistograms.AnimationErrorMs(section), expected.AnimationErrorMs),
            (SectionHistograms.DisplayDeltaMs(section), expected.DisplayDeltaMs),
          }
        )
        {
          Assert.That((actual.BinWidthMs, actual.Total), Is.EqualTo((wanted.BinWidthMs, wanted.Total)));
          Assert.That(actual.Bins, Is.EqualTo(wanted.Bins));
        }
      }
    }

    /// <summary>The cards built in parallel (and the report card's panels in parallel) write exactly the SVG the builders write.</summary>
    [Test]
    public void SectionCards_AreTheBuildersCards()
    {
      var run = ReportSvgTests.Synthetic(240 * 10);
      var options = ReportOptions.ShowOnly(
        new[] { ReportItem.AnimationError, ReportItem.DisplayTimeStep, ReportItem.FrameTime, ReportItem.LateShare, ReportItem.RefreshStrip }
      );
      foreach (var section in new[] { RunSection.Whole(run), RunSection.Create(run, 2, 3) })
      {
        var cards = SectionCards.Build(section, options)!;
        Assert.That(cards.Section, Is.SameAs(section));
        Assert.That(SvgCardWriter.Write(cards.Timeline), Is.EqualTo(ReportCard.Render(section, options)));
        Assert.That(SvgCardWriter.Write(cards.ErrorHistogram), Is.EqualTo(DistributionCard.Render(DistributionCard.ErrorHistogram, section)));
        Assert.That(SvgCardWriter.Write(cards.ErrorPercentiles), Is.EqualTo(DistributionCard.Render(DistributionCard.ErrorPercentiles, section)));
        Assert.That(
          SvgCardWriter.Write(cards.DisplayTimeStepHistogram),
          Is.EqualTo(DistributionCard.Render(DistributionCard.DisplayTimeStepHistogram, section))
        );
        Assert.That(SvgCardWriter.Write(cards.Drift), Is.EqualTo(DistributionCard.Render(DistributionCard.Drift, section)));
      }
      using var cancelled = new CancellationTokenSource();
      cancelled.Cancel();
      Assert.That(SectionCards.Build(RunSection.Whole(run), options, cancelled.Token), Is.Null, "cancelled: nothing, and no exception");
    }

    private static readonly ReportOptions g_panels = ReportOptions.ShowOnly(
      new[] { ReportItem.AnimationError, ReportItem.DisplayTimeStep, ReportItem.FrameTime, ReportItem.LateShare, ReportItem.RefreshStrip }
    );

    /// <summary>A card whose visible range is its whole section is the plain card.</summary>
    [Test]
    public void SlidingWindow_OverItsOwnRange_IsThePlainCard()
    {
      var run = ReportSvgTests.Synthetic(240 * 10);
      var section = RunSection.Create(run, 2, 4);
      Assert.That(
        SvgCardWriter.Write(ReportCard.Build(section, g_panels, wholeRunScales: true, visible: (2, 4))),
        Is.EqualTo(SvgCardWriter.Write(ReportCard.Build(section, g_panels, wholeRunScales: true)))
      );
    }

    /// <summary>
    /// The sliding window: the plots show the visible range, what moves with time is in scrolling layers that reach beyond them to the
    /// window's ends, and nothing else leaves the card.
    /// </summary>
    [Test]
    public void SlidingWindow_PlotsShowTheVisibleRange_TheLayersReachBeyond()
    {
      var run = ReportSvgTests.Synthetic(240 * 60);
      var card = ReportCard.Build(RunSection.Create(run, 18, 24), g_panels, wholeRunScales: true, visible: (20, 22));
      Assert.That(card.Plots, Is.Not.Empty);
      foreach (var plot in card.Plots)
        Assert.That((plot.XFrom, plot.XTo), Is.EqualTo((20.0, 22.0)), plot.Id);

      var layers = card.Shapes.OfType<ScrollShape>().ToList();
      Assert.That(layers, Is.Not.Empty);
      var moving = layers.SelectMany(l => Xs(l.Children)).ToList();
      double plotWidth = ReportCard.PlotWidth(card.Width);
      Assert.That(moving.Min(), Is.LessThan(ReportCard.PlotX0 - (plotWidth * 0.9)), "a screen of the window to the left");
      Assert.That(moving.Max(), Is.GreaterThan(ReportCard.PlotX0 + (plotWidth * 1.9)), "and to the right");
      Assert.That(Xs(card.Shapes.Where(s => s is not ScrollShape)).All(x => x >= 0 && x <= card.Width), "the rest stays on the card");
    }

    /// <summary>The animation time step overlay moves with time: its line is in the scrolling layers, as the display time step's holds are.</summary>
    [Test]
    public void SlidingWindow_TheAnimationTimeStepScrolls()
    {
      var run = ReportSvgTests.Synthetic(240 * 60);
      var card = ReportCard.Build(
        RunSection.Create(run, 18, 24),
        g_panels.Show(new[] { ReportItem.AnimationTimeStep }),
        wholeRunScales: true,
        visible: (20, 22)
      );
      var scrolling = card.Shapes.OfType<ScrollShape>().SelectMany(l => l.Children).OfType<PathShape>().ToList();
      Assert.That(scrolling.Any(p => p.Class == "step-line"), "in a scrolling layer");
      Assert.That(scrolling.Any(p => p.Class == "held"), "with the holds");
      Assert.That(card.Shapes.OfType<PathShape>().Any(p => p.Class == "step-line"), Is.False, "not fixed on the card");
    }

    /// <summary>
    /// Two windows at one zoom whose visible ranges start a whole number of pixels apart draw the same shapes, moved by those pixels: the
    /// GUI swaps in the next window without a visible change.
    /// </summary>
    [Test]
    public void SlidingWindow_WindowsAtOneZoom_DrawTheSameShapes()
    {
      var run = ReportSvgTests.Synthetic(240 * 60);
      double length = 2;
      double pixelsPerSecond = ReportCard.PlotWidth(ReportCard.Width) / length;
      const int Shift = 137;
      double fromA = Math.Round(20.3 * pixelsPerSecond) / pixelsPerSecond;
      double fromB = fromA + (Shift / pixelsPerSecond);
      var a = ReportCard.Build(RunSection.Create(run, fromA - length, fromA + (2 * length)), g_panels, true, visible: (fromA, fromA + length));
      var b = ReportCard.Build(RunSection.Create(run, fromB - length, fromB + (2 * length)), g_panels, true, visible: (fromB, fromB + length));
      // The bars of the frames both windows show, visible in either
      static List<double> Bars(CardDrawing card, double from, double to) =>
        card
          .Shapes.OfType<ScrollShape>()
          .SelectMany(l => l.Children)
          .OfType<RectShape>()
          .Where(r => r.Class == "bar")
          .Select(r => r.X.Value)
          .Where(x => x >= from && x < to)
          .ToList();
      var inA = Bars(a, ReportCard.PlotX0 + Shift, ReportCard.PlotX1).Select(x => x - Shift).ToList();
      var inB = Bars(b, ReportCard.PlotX0, ReportCard.PlotX1 - Shift);
      Assert.That(inA, Has.Count.GreaterThan(40));
      Assert.That(inB, Is.EqualTo(inA).Within(0.011), "the same frames at the same pixels, moved by the shift");
    }

    private static IEnumerable<double> Xs(IEnumerable<CardShape> shapes)
    {
      foreach (var shape in shapes)
      {
        switch (shape)
        {
          case RectShape r:
            yield return r.X.Value;
            yield return r.X.Value + r.Width.Value;
            break;
          case LineShape l:
            yield return l.X1.Value;
            yield return l.X2.Value;
            break;
          case TextShape t:
            yield return t.X;
            break;
          case ScrollShape s:
            foreach (double x in Xs(s.Children))
              yield return x;
            break;
          case GroupShape g:
            foreach (double x in Xs(g.Children))
              yield return x;
            break;
        }
      }
    }
  }
}
