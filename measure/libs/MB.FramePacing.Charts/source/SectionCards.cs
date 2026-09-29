//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The cards the GUI shows for one section of a run: the Timeline (the report card with the options given) and the four distribution cards,
//* built in parallel. The builders only read the section, so any thread may build them.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Threading;
using System.Threading.Tasks;

namespace MB.FramePacing.Charts
{
  public sealed record SectionCards(
    RunSection Section,
    CardDrawing Timeline,
    CardDrawing ErrorHistogram,
    CardDrawing ErrorPercentiles,
    CardDrawing DisplayTimeStepHistogram,
    CardDrawing Drift
  )
  {
    /// <summary>
    /// Every card of <paramref name="section"/>, <paramref name="width"/> units wide; with <paramref name="wholeRunScales"/> the Timeline keeps
    /// the whole run's scales. A cancelled <paramref name="token"/> skips the cards not started yet and gives null: cancelling is normal (a
    /// newer zoom overtook this one), so nothing throws.
    /// </summary>
    public static SectionCards? Build(
      RunSection section,
      ReportOptions timelineOptions,
      CancellationToken token = default,
      double width = ReportCard.Width,
      bool wholeRunScales = false
    )
    {
      CardDrawing? timeline = null;
      CardDrawing? errorHistogram = null;
      CardDrawing? errorPercentiles = null;
      CardDrawing? displayTimeStepHistogram = null;
      CardDrawing? drift = null;
      Parallel.Invoke(
        () => timeline = token.IsCancellationRequested ? null : ReportCard.Build(section, timelineOptions, wholeRunScales, width),
        () => errorHistogram = token.IsCancellationRequested ? null : DistributionCard.Build(DistributionCard.ErrorHistogram, section, width),
        () => errorPercentiles = token.IsCancellationRequested ? null : DistributionCard.Build(DistributionCard.ErrorPercentiles, section, width),
        () =>
          displayTimeStepHistogram = token.IsCancellationRequested
            ? null
            : DistributionCard.Build(DistributionCard.DisplayTimeStepHistogram, section, width),
        () => drift = token.IsCancellationRequested ? null : DistributionCard.Build(DistributionCard.Drift, section, width)
      );
      if (token.IsCancellationRequested)
        return null;
      return new SectionCards(section, timeline!, errorHistogram!, errorPercentiles!, displayTimeStepHistogram!, drift!);
    }
  }
}
