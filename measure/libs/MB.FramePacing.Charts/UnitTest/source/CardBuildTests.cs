//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Building the GUI's cards off its thread: only the latest request's cards are kept (a request a newer one overtook gets nothing, and its
//* build is cancelled), and the parallel build draws exactly what the card builders draw one by one.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
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
  }
}
