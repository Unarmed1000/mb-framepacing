//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Frames not presented because their present failed, on the report: told apart from the frames the target dropped in the hold kinds, the
//* events, the refresh strip and its key, and the frames dropped tile, on a synthetic 240 Hz run whose frame 300 comes after two frames never
//* shown, the second of which its marker says failed to present.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Linq;
using MB.FramePacing.Analysis;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class PresentFailedChartTests
  {
    private const int After = 300;
    private const long Refresh = 4_166_666;

    private static ChartRun Run()
    {
      var chart = ReportSvgTests.Synthetic(600);
      // Frame 299 stays on screen for the two refreshes the frames never shown were due in, then frame 300 and every frame after it come
      // two refreshes later, two frame indices on
      var hold = new NanosecondTimeSpan(2 * Refresh);
      var frames = chart
        .Run.Frames.Select(
          (f, i) =>
            i < After - 1 ? f
            : i == After - 1 ? f with { CaptureCount = 3, LastSeenTime = f.LastSeenTime + hold, OnScreen = new NanosecondTimeSpan(3 * Refresh) }
            : f with
            {
              FrameIndex = f.FrameIndex + 2,
              FirstSeenTime = f.FirstSeenTime + hold,
              LastSeenTime = f.LastSeenTime + hold,
              SkippedBefore = i == After ? 2UL : 0UL,
              Flags = i == After ? f.Flags | PresentedFrameFlags.PresentFailedBefore : f.Flags,
            }
        )
        .ToList();
      // The counts the analysis gives such a run: one frame the target dropped, one whose present failed
      var counts = chart.Run.Counts with
      {
        SkippedFrameIndices = 2,
        DroppedFrames = 1,
        PresentFailedFrames = 1,
      };
      return chart with { Run = chart.Run with { Frames = frames, Counts = counts } };
    }

    [Test]
    public void TheFrameWhosePresentFailed_IsToldApartFromTheFramesTheTargetDropped()
    {
      var chart = Run();
      var data = RunChartData.Of(chart);
      Assert.That(data.DroppedBeforeFrame[After], Is.EqualTo(1), "the first of the two, dropped by the target");
      Assert.That(data.PresentFailedBeforeFrame[After], Is.EqualTo(1), "the second, whose present failed");
      Assert.That(data.HoldKinds[After - 1], Is.EqualTo(HoldKind.PresentFailed));
      Assert.That(data.Events.Count(RunEventKind.PresentFailed), Is.EqualTo(1));
      Assert.That(data.Events.Count(RunEventKind.FramesDropped), Is.EqualTo(1));

      var section = RunSection.Whole(chart);
      var tile = RunHeadline.Shown(section.Section, ReportOptions.Default).Single(t => t.Id == ReportItem.FramesDropped);
      Assert.That(tile.Value, Is.EqualTo("1"));
      Assert.That(tile.Detail, Does.EndWith(" · 1 present failed"));

      // The strip's refreshes one by one: a section around the frames never shown
      string svg = ReportCard.Render(RunSection.Create(chart, 1.1, 1.4));
      Assert.That(svg, Does.Contain("class=\"strip-failed\""), "the refreshes where it was due");
      Assert.That(svg, Does.Contain("class=\"strip-dropped\""), "and where the dropped frame was due");
      Assert.That(svg, Does.Contain("a frame whose present failed was due (repeat)"));
      Assert.That(svg, Does.Contain("held: a present failed before the next"));
    }
  }
}
