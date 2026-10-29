//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The present timing notes (PresentTimings, RunHeadline.PresentLines): the pacer's planned time from present to screen, how much later than
//* the run's quickest frame each frame reached the screen after its present, why late frames were late and how often the CPU work took its
//* intended period. On a synthetic 240 Hz run whose markers' CPU times are a regular grid on a clock of their own: the clocks' offset never
//* shows.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Linq;
using MB.FramePacing.Analysis;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class PresentTimingTests
  {
    private const long Refresh = 4_166_666;
    private const long Busy = 1_000_000;

    // The application's clock is 100 s from the capture's: nothing may depend on it
    private const long ClockOffset = 100_000_000_000;

    /// <summary>
    /// A frame every refresh, its CPU work 1 ms, the pacer planning each two refreshes after its present; from <paramref name="queueFrom"/> on
    /// every frame reaches the screen a refresh later (a queue that filled). <paramref name="change"/> changes a frame further.
    /// </summary>
    private static ChartRun Run(int queueFrom = int.MaxValue, Func<PresentedFrame, int, PresentedFrame>? change = null)
    {
      var chart = ReportSvgTests.Synthetic(600);
      var frames = chart
        .Run.Frames.Select(
          (f, i) =>
          {
            long start = ClockOffset + (i * Refresh);
            var frame = f with
            {
              Flags = PresentedFrameFlags.None,
              FirstSeenTime = new NanosecondTickCount((i * Refresh) + (i >= queueFrom ? Refresh : 0)),
              CpuStartTime = new NanosecondTickCount(start),
              CpuBusy = NanosecondTimeDuration.FromNanoseconds(Busy),
              IntendedDisplayTime = new NanosecondTickCount(start + Busy + (2 * Refresh)),
            };
            return change?.Invoke(frame, i) ?? frame;
          }
        )
        .ToList();
      return chart with { Run = chart.Run with { Frames = frames } };
    }

    private static string[] Lines(ChartRun run, string id) =>
      RunHeadline.PresentLines(RunSection.Whole(run)).Where(l => l.Id == id).Select(l => l.Line).ToArray();

    [Test]
    public void ThePlannedLatency_IsTheIntendedDisplayTimeAfterThePresent()
    {
      var run = Run();
      var timings = RunChartData.Of(run).PresentTimings;
      Assert.That(timings.Select(t => t.PlannedLatency?.Nanoseconds), Is.All.EqualTo(2 * Refresh));
      Assert.That(
        Lines(run, ReportItem.PresentLatency)[0],
        Is.EqualTo(
          "Planned time from present to screen: median 8.3 ms (2.0 refreshes), 8.3 ms to 8.3 ms "
            + "(the pacer's intended display time: its plan, not a measurement)."
        )
      );
    }

    /// <summary>The run's quickest frame is the reference: a queue that fills shows as a whole refresh more that stays, whatever the clocks' offset.</summary>
    [Test]
    public void TheExtraDelay_IsAfterThePresent_OverTheRunsQuickestFrame()
    {
      var timings = RunChartData.Of(Run(queueFrom: 300)).PresentTimings;
      Assert.That(timings.Take(300).Select(t => t.ExtraDelay?.Nanoseconds), Is.All.EqualTo(0L));
      Assert.That(timings.Skip(300).Select(t => t.ExtraDelay?.Nanoseconds), Is.All.EqualTo(Refresh));
      Assert.That(
        Lines(Run(queueFrom: 300), ReportItem.PresentLatency)[1],
        Does.StartWith("Present to screen, beyond the run's quickest frame: median 2.1 ms, p95 4.2 ms, max 4.2 ms (1.0 refreshes).")
      );
    }

    /// <summary>
    /// Three late frames: frame 100's CPU work took two refreshes (its present came late, through its work), frame 200 started two refreshes
    /// late (its present came late, its work was short), frame 300's present came on time (it was held after it).
    /// </summary>
    [Test]
    public void LateFrames_AreSubmittedLate_OrHeldAfterAnOnTimePresent()
    {
      var run = Run(
        change: (f, i) =>
          i switch
          {
            100 => f with { Flags = PresentedFrameFlags.Late, CpuBusy = NanosecondTimeDuration.FromNanoseconds(2 * Refresh) },
            200 => f with { Flags = PresentedFrameFlags.Late, CpuStartTime = f.CpuStartTime + new NanosecondTimeSpan(2 * Refresh) },
            300 => f with { Flags = PresentedFrameFlags.Late },
            _ => f,
          }
      );
      var timings = RunChartData.Of(run).PresentTimings;
      Assert.That(timings[100].LateReason, Is.EqualTo(LateReason.SubmittedLateWork));
      Assert.That(timings[200].LateReason, Is.EqualTo(LateReason.SubmittedLateStart));
      Assert.That(timings[300].LateReason, Is.EqualTo(LateReason.ShownLateAfterPresent));
      Assert.That(timings.Where((_, i) => i != 100 && i != 200 && i != 300).Select(t => t.LateReason), Is.All.Null, "only late frames");

      var lines = Lines(run, ReportItem.LateReasons);
      Assert.That(
        lines[0],
        Is.EqualTo(
          "Late frames: 3; 2 submitted late (1 whose CPU work took longer than their target, 1 that started late), 1 submitted on time and "
            + "shown late (held after the present: GPU, queue, compositor or display; the capture can not tell which)."
        )
      );
      // The first frame has no intended period (no step into it): it is not counted
      Assert.That(lines[1], Is.EqualTo("CPU work took its intended period or longer on 1 of 599 frames (1 %)."));
    }

    [Test]
    public void WithoutTheCpuFields_NothingIsSaid()
    {
      var run = ReportSvgTests.Synthetic(600);
      Assert.That(RunChartData.Of(run).PresentTimings, Is.All.EqualTo(PresentTiming.None));
      Assert.That(RunHeadline.PresentLines(RunSection.Whole(run)), Is.Empty);
    }

    [Test]
    public void WithoutASchedule_ThereIsNoPlan_AndTheRestIsSaid()
    {
      var run = Run(queueFrom: 300, change: (f, _) => f with { IntendedDisplayTime = default });
      var lines = RunHeadline.PresentLines(RunSection.Whole(run));
      Assert.That(lines.Select(l => l.Line), Has.None.StartsWith("Planned time"));
      Assert.That(lines.Select(l => l.Line), Has.Some.StartsWith("Present to screen"));
    }

    [Test]
    public void TheNotes_AreOnTheCard_UnderTheirIds()
    {
      var run = Run(queueFrom: 300);
      string Notes(ReportOptions options) =>
        string.Join(
          "\n",
          ReportCard.Build(RunSection.Whole(run), options).FlatShapes.OfType<TextShape>().Where(t => t.Class == "sub").Select(t => t.Content)
        );
      Assert.That(Notes(ReportOptions.Default), Does.Contain("Planned time from present to screen"));
      Assert.That(Notes(ReportOptions.Default.Hide(new[] { ReportItem.PresentLatency })), Does.Not.Contain("Planned time"));
      Assert.That(Notes(ReportOptions.Default.Hide(new[] { ReportItem.PresentLatency })), Does.Contain("CPU work took its intended period"));
    }
  }
}
