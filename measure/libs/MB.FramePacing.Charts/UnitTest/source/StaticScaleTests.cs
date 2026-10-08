//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* ReportOptions.ClampStatic: the display time step and frametime scales follow the frames that animate, and an idle second (a static frame's
//* hold, frametime, CPU busy and aim) stops at the edge with its value; switched off, those values set the scales too.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Analysis;
using MB.FramePacing.MarkerDecoding;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class StaticScaleTests
  {
    // 1/60 s cut to the nanosecond
    private const long Period = 16_666_666;
    private const long Idle = NanosecondTimeSpan.NanosecondsPerSecond;
    private const int IdleFrames = 4;

    /// <summary>
    /// A 60 Hz run: 60 frames that animate, <paramref name="idle"/> idle frames held a second each (static, aiming for 1 fps, and waiting for
    /// input inside the frame: a CPU busy of the whole second), then 60 frames that animate again. Every frame has a CPU start and CPU busy.
    /// </summary>
    private static ChartRun Run(int idle = IdleFrames)
    {
      var rows = new List<CaptureRow>();
      void Add(MarkerPayload payload, int captures)
      {
        for (int k = 0; k < captures; ++k)
          rows.Add(new CaptureRow(rows.Count, new NanosecondTickCount(rows.Count * Period), CaptureStatus.Decoded, payload));
      }
      for (int i = 0; i < 3; ++i)
        Add(new MarkerPayload(MarkerKind.SequenceStart, 1, 0, MB.FramePacing.Marker.MarkerFlags.NoFlags, new NanosecondTimeSpan(0)), 1);
      ulong index = 1;
      long animation = 0;
      void Frame(bool isIdle)
      {
        int captures = isIdle ? 60 : 1;
        long aim = isIdle ? Idle : Period;
        Add(
          new MarkerPayload(
            MarkerKind.Frame,
            1,
            index++,
            isIdle ? MB.FramePacing.Marker.MarkerFlags.StaticAfter : MB.FramePacing.Marker.MarkerFlags.NoFlags,
            new NanosecondTimeSpan(animation),
            PreferredFrameTime: NanosecondTimeDuration.FromNanoseconds(aim),
            TargetFrameTime: NanosecondTimeDuration.FromNanoseconds(aim),
            CpuStartTime: new NanosecondTickCount(100_000_000 + (rows.Count * Period)),
            CpuBusy: NanosecondTimeDuration.FromNanoseconds(isIdle ? Idle - 5_000_000 : 8_000_000)
          ),
          captures
        );
        animation += captures * Period;
      }
      for (int i = 0; i < 60; ++i)
        Frame(isIdle: false);
      for (int i = 0; i < idle; ++i)
        Frame(isIdle: true);
      for (int i = 0; i < 60; ++i)
        Frame(isIdle: false);
      for (int i = 0; i < 3; ++i)
        Add(new MarkerPayload(MarkerKind.SequenceEnd, 1, 999, MB.FramePacing.Marker.MarkerFlags.NoFlags, new NanosecondTimeSpan(0)), 1);
      var result = TimelineAnalyzer.Analyze(rows);
      return new ChartRun(result.Runs.Single(), result.CapturePeriod, result.ErrorThreshold, Camera: false);
    }

    private static (double Step, double FrameTime) Tops(CardDrawing card) =>
      (card.Plots.Single(p => p.Id == ReportItem.DisplayTimeStep).YTo, card.Plots.Single(p => p.Id == ReportItem.FrameTime).YTo);

    /// <summary>
    /// The idle seconds are more than 1 % of the frames, so the hitch rule leaves them in: only the switch keeps them out of the scales. On,
    /// the scales follow the frames that animate (a few refreshes) and each idle second is marked at the edge with its value.
    /// </summary>
    [Test]
    public void ClampStatic_ScalesToTheFramesThatAnimate_AndMarksTheIdleSeconds()
    {
      var section = RunSection.Whole(Run());
      Assert.That(section.Data.StaticStretches, Has.Count.EqualTo(1), "one idle stretch");

      var clamped = ReportCard.Build(section);
      var (step, frameTime) = Tops(clamped);
      Assert.That(step, Is.LessThan(100), "the display time step scale: the frames that animate");
      Assert.That(frameTime, Is.LessThan(100), "the frametime scale: the frames that animate, not an idle frame's frametime or CPU busy");
      var labels = clamped.FlatShapes.OfType<TextShape>().Select(t => t.Content).ToList();
      Assert.That(labels.Count(l => l.StartsWith("1000", StringComparison.Ordinal)), Is.GreaterThan(0), "the idle seconds marked at the edge");

      var full = ReportCard.Build(section, ReportOptions.Default with { ClampStatic = false });
      var (fullStep, fullFrameTime) = Tops(full);
      Assert.That(fullStep, Is.GreaterThanOrEqualTo(1000), "switched off, the idle holds and aims set the scale");
      Assert.That(fullFrameTime, Is.GreaterThanOrEqualTo(1000), "switched off, the idle frametimes and CPU busy set the scale");
    }

    /// <summary>
    /// A hold's reference line is the aim of the frame that ends it: the last hold before the idle stretch ends at the first idle frame, whose
    /// aim is 1 s. It is an idle value, so the clamped scale leaves it out; the unclamped one keeps it.
    /// </summary>
    [Test]
    public void ReferenceLine_EndingAtAnIdleFrame_StaysOutOfTheClampedScale()
    {
      var data = RunChartData.Of(Run());
      int lastAnimating = data.StaticStretches[0].Start - 1;
      long idle = FrameTimeRounding.WholeRefreshes(new NanosecondTimeSpan(Idle), new NanosecondTimeSpan(Period)).Nanoseconds;

      Assert.That(data.AnimatingStepReferences.Frames[lastAnimating], Is.False, "its line comes from an idle frame");
      Assert.That(data.AllStepReferences.Frames[lastAnimating], Is.True);
      Assert.That(data.AllStepReferences.Values.KthSmallest(0, data.AllStepReferences.Count, data.AllStepReferences.Count - 1), Is.EqualTo(idle));
      Assert.That(
        data.AnimatingStepReferences.Values.KthSmallest(0, data.AnimatingStepReferences.Count, data.AnimatingStepReferences.Count - 1),
        Is.LessThan(idle)
      );
    }

    /// <summary>A run without static frames draws the same card either way.</summary>
    [Test]
    public void WithoutStaticFrames_TheSwitchChangesNothing()
    {
      var section = RunSection.Whole(Run(idle: 0));
      Assert.That(ReportCard.Render(section, ReportOptions.Default with { ClampStatic = false }), Is.EqualTo(ReportCard.Render(section)));
    }
  }
}
