//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The report's pacer band (RunHeadline.Pacing): whether a pacer or a frame limiter held the frames, from the markers' paced flag only (a run
//* without it does not say, whatever its other fields carry); a checkbox per pacing field with its share and what it says; and what the
//* frames are measured against. On a synthetic 240 Hz run whose markers carry made-up pacing fields.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Linq;
using MB.FramePacing.Analysis;
using MB.FramePacing.MarkerDecoding;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class PacingSummaryTests
  {
    private const int Frames = 600;
    private static readonly NanosecondTimeDuration g_60 = NanosecondTimeDuration.FromNanoseconds(16_666_667);
    private static readonly NanosecondTimeDuration g_30 = NanosecondTimeDuration.FromNanoseconds(33_333_333);

    /// <summary>The synthetic run, its frames changed by <paramref name="change"/> (the frame and its index).</summary>
    private static ChartRun Run(Func<PresentedFrame, int, PresentedFrame> change)
    {
      var chart = ReportSvgTests.Synthetic(Frames);
      return chart with { Run = chart.Run with { Frames = chart.Run.Frames.Select(change).ToList() } };
    }

    private static PacingSummary Whole(Func<PresentedFrame, int, PresentedFrame> change) => RunHeadline.Pacing(RunSection.Whole(Run(change)));

    private static string[] Marks(PacingSummary pacing) => pacing.Rows.Select(r => r.Mark).ToArray();

    [Test]
    public void NoPacingFields_IsNotSaid_AndEveryBoxIsEmpty()
    {
      var pacing = Whole((f, _) => f);
      Assert.That(pacing.Answer, Is.EqualTo(PacedAnswer.NotSaid));
      Assert.That(pacing.Line, Is.EqualTo("pacer or limiter: not said (no frame carries the paced flag)"));
      Assert.That(Marks(pacing), Is.EqualTo(new[] { "–", "–", "–", "–" }));
      Assert.That(
        pacing.Rows.Select(r => r.Name),
        Is.EqualTo(new[] { "paced flag", "schedule (intended display times)", "target frame time", "preferred frame time" })
      );
      Assert.That(pacing.MeasuredAgainst, Is.EqualTo(RunHeadline.TargetSource(PacingSource.NativeRefresh)));
    }

    /// <summary>
    /// Without the paced flag nothing is guessed: a schedule, a target frame time that changes or is slower than the preferred one, a constant
    /// target (a frame limiter writes one, and a loop on vsync that knows its refresh writes a schedule) are listed, never an answer.
    /// </summary>
    [Test]
    public void WithoutThePacedFlag_TheOtherFieldsAreListed_AndNothingIsGuessed()
    {
      var pacing = Whole(
        (f, i) =>
          f with
          {
            IntendedDisplayTime = new NanosecondTickCount(1 + i),
            MarkerTargetFrameTime = i < Frames / 2 ? g_60 : g_30,
            MarkerPreferredFrameTime = g_60,
          }
      );
      Assert.That(pacing.Answer, Is.EqualTo(PacedAnswer.NotSaid));
      Assert.That(
        pacing.Rows.Select(r => r.Text),
        Is.EqualTo(
          new[]
          {
            "– paced flag",
            "✓ schedule (intended display times)",
            "✓ target frame time: 30–60 fps",
            // Slower than preferred in whole refreshes (8 against 4 at 240 Hz) for the second half
            "✓ preferred frame time: 60 fps, target slower in 50 %",
          }
        )
      );
    }

    [Test]
    public void ATargetWithinAWholeRefreshOfPreferred_IsNotSlower()
    {
      // 16.7 ms and 16.8 ms are both 4 refreshes at 240 Hz, as the analysis rounds them: the pacer runs at the preferred rate
      var pacing = Whole(
        (f, _) => f with { MarkerTargetFrameTime = NanosecondTimeDuration.FromNanoseconds(16_800_000), MarkerPreferredFrameTime = g_60 }
      );
      Assert.That(pacing.Rows[3].Detail, Is.EqualTo("60 fps"));
    }

    /// <summary>Some frames carry a field: the share is its mark, never 0 % for some or 100 % for not all.</summary>
    [TestCase(Frames / 2, "50 %")]
    [TestCase(1, "1 %")]
    [TestCase(Frames - 1, "99 %")]
    public void AFieldSomeFramesCarry_IsMarkedWithItsShare(int scheduled, string mark)
    {
      var pacing = Whole((f, i) => i < scheduled ? f with { IntendedDisplayTime = new NanosecondTickCount(1 + i) } : f);
      Assert.That(pacing.Rows[1].Text, Is.EqualTo(mark + " schedule (intended display times)"));
    }

    /// <summary>
    /// The paced flag is the application's word: paced frames are said so with their share, and in a run that carries the flag a section
    /// without it was not paced.
    /// </summary>
    [Test]
    public void ThePacedFlag_IsTheAnswer_InEverySectionOfARunThatCarriesIt()
    {
      var chart = Run((f, i) => i < Frames / 2 ? f with { Flags = f.Flags | PresentedFrameFlags.Paced } : f);
      var whole = RunHeadline.Pacing(RunSection.Whole(chart));
      Assert.That(whole.Answer, Is.EqualTo(PacedAnswer.MarkersSayPaced));
      Assert.That(whole.Line, Is.EqualTo("pacer or limiter: in 50 % of the frames (the markers say so)"));
      Assert.That(Marks(whole), Is.EqualTo(new[] { "50 %", "–", "–", "–" }));

      var paced = RunHeadline.Pacing(RunSection.Create(chart, 0, 1));
      Assert.That(paced.Line, Is.EqualTo("pacer or limiter: yes (the markers say so)"));
      Assert.That(paced.Rows[0].Mark, Is.EqualTo("✓"));

      // The second half of the 2.5 s run: frames 300 on
      var unpaced = RunHeadline.Pacing(RunSection.Create(chart, 1.5, 2.4));
      Assert.That(unpaced.Answer, Is.EqualTo(PacedAnswer.MarkersSayNotPaced));
      Assert.That(unpaced.Line, Is.EqualTo("pacer or limiter: no (the markers say so)"));
      Assert.That(unpaced.Rows[0].Mark, Is.EqualTo("–"));
    }

    [Test]
    public void OnDemand_IsSaid_AndIsNoRate()
    {
      var pacing = Whole((f, i) => f with { MarkerTargetFrameTime = i % 4 == 0 ? MarkerPayload.OnDemandFrameTime : g_60 });
      Assert.That(pacing.Rows[2].Text, Is.EqualTo("✓ target frame time: 60 fps, on demand in 25 %"));

      // Every frame on demand: no rate, and no share to give
      var renderer = Whole(
        (f, _) => f with { MarkerTargetFrameTime = MarkerPayload.OnDemandFrameTime, MarkerPreferredFrameTime = MarkerPayload.OnDemandFrameTime }
      );
      Assert.That(renderer.Rows[2].Text, Is.EqualTo("✓ target frame time: on demand"));
      Assert.That(renderer.Rows[3].Text, Is.EqualTo("✓ preferred frame time: on demand"));
    }

    /// <summary>The band is under the report's panels, and hiding it makes the card shorter.</summary>
    [Test]
    public void TheCard_HasTheBandUnderThePanels_UnlessHidden()
    {
      var run = Run((f, _) => f with { Flags = f.Flags | PresentedFrameFlags.Paced, MarkerTargetFrameTime = g_60 });
      var shown = ReportCard.Build(RunSection.Whole(run));
      var band = shown.FlatShapes.OfType<TextRunsShape>().Single(t => t.Runs[0].Text == "PACER");
      Assert.That(string.Concat(band.Runs.Select(r => r.Text)), Is.EqualTo("PACER   pacer or limiter: yes   the markers say so"));
      Assert.That(shown.Plots.Max(p => p.Bottom), Is.LessThan(band.Y), "under every panel");
      var texts = shown.FlatShapes.OfType<TextRunsShape>().Select(t => string.Concat(t.Runs.Select(r => r.Text))).ToList();
      Assert.That(texts, Has.Some.Contains("✓ target frame time: 60 fps"));
      Assert.That(
        shown.FlatShapes.OfType<TextShape>().Select(t => t.Content),
        Has.Some.EqualTo("measured against " + RunHeadline.TargetSource(PacingSource.NativeRefresh))
      );

      var hidden = ReportCard.Build(RunSection.Whole(run), ReportOptions.Default.Hide(new[] { ReportItem.Pacing }));
      Assert.That(hidden.FlatShapes.OfType<TextRunsShape>().Select(t => t.Runs[0].Text), Has.None.EqualTo("PACER"));
      Assert.That(hidden.Height, Is.LessThan(shown.Height));
    }
  }
}
