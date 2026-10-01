//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The events of a run from its frames and its capture rows: every kind where it happened and as often as it happened, in the frames lane or
//* the capture lane, with the key and the description naming them. From the analysis of capture rows made for it, one event of each kind.
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
  public class RunEventsTests
  {
    private const long Period = TimeSpan.TicksPerSecond / 60;

    /// <summary>The capture rows of a 60 Hz capture card, one refresh each, with one event of every kind; and when each happened.</summary>
    private static (List<CaptureRow> Rows, Dictionary<RunEventKind, long> At) Capture()
    {
      var rows = new List<CaptureRow>();
      var at = new Dictionary<RunEventKind, long>();
      long time = 0;
      MarkerPayload Frame(ulong index) =>
        new MarkerPayload(MarkerKind.Frame, 1, index, MB.FramePacing.Marker.MarkerFlags.None, new TimeSpan((long)index * Period));
      CaptureRow Add(CaptureStatus status, MarkerPayload payload, uint sourceDrops = 0, MarkerPayload? sync = null, long missed = 0)
      {
        time += missed * Period;
        var row = new CaptureRow(rows.Count, new TickCount64(time), status, payload, null, sourceDrops, sync) { MissedBefore = missed };
        rows.Add(row);
        time += Period;
        return row;
      }
      for (int i = 0; i < 3; ++i)
        Add(CaptureStatus.Decoded, new MarkerPayload(MarkerKind.SequenceStart, 1, 0, MB.FramePacing.Marker.MarkerFlags.None, new TimeSpan(0)));
      Add(CaptureStatus.Decoded, Frame(1));
      Add(CaptureStatus.Decoded, Frame(2));
      at[RunEventKind.NotDecoded] = Add(CaptureStatus.Undecodable, default).CaptureTime.Ticks;
      Add(CaptureStatus.Decoded, Frame(3));
      at[RunEventKind.NotRecorded] = Add(CaptureStatus.NotRecorded, default).CaptureTime.Ticks;
      Add(CaptureStatus.Decoded, Frame(4));
      at[RunEventKind.Torn] = Add(CaptureStatus.Torn, Frame(5), sync: Frame(4)).CaptureTime.Ticks;
      at[RunEventKind.SourceDropped] = Add(CaptureStatus.Decoded, Frame(5), sourceDrops: 2).CaptureTime.Ticks - Period;
      Add(CaptureStatus.Decoded, Frame(5));
      at[RunEventKind.Missed] = Add(CaptureStatus.Decoded, Frame(6), missed: 1).CaptureTime.Ticks - Period;
      Add(CaptureStatus.Decoded, Frame(6));
      // Frame 7 never shown over a capture without gaps: dropped, due in the refresh before frame 8
      at[RunEventKind.FramesDropped] = Add(CaptureStatus.Decoded, Frame(8)).CaptureTime.Ticks - Period;
      Add(CaptureStatus.Decoded, Frame(9));
      at[RunEventKind.OutOfOrder] = Add(CaptureStatus.Decoded, Frame(8)).CaptureTime.Ticks;
      Add(CaptureStatus.Decoded, Frame(10));
      Add(CaptureStatus.Decoded, Frame(10));
      for (int i = 0; i < 3; ++i)
        Add(CaptureStatus.Decoded, new MarkerPayload(MarkerKind.SequenceEnd, 1, 11, MB.FramePacing.Marker.MarkerFlags.None, new TimeSpan(0)));
      return (rows, at);
    }

    private static ChartRun Chart(List<CaptureRow> rows, bool withCaptures = true)
    {
      var result = TimelineAnalyzer.Analyze(rows);
      return new ChartRun(result.Runs.Single(), result.CapturePeriod.Ticks, result.ErrorThreshold.Ticks, Camera: false)
      {
        Captures = withCaptures ? rows.Select(r => r.ToCsvRow()).ToList() : null,
      };
    }

    [Test]
    public void EveryKind_IsWhereAndAsOftenAsItHappened()
    {
      var (rows, at) = Capture();
      var events = RunChartData.Of(Chart(rows)).Events;

      var expected = new Dictionary<RunEventKind, long>
      {
        [RunEventKind.FramesDropped] = 1,
        [RunEventKind.OutOfOrder] = 1,
        [RunEventKind.Torn] = 1,
        [RunEventKind.NotRecorded] = 1,
        [RunEventKind.SourceDropped] = 2,
        [RunEventKind.Missed] = 1,
        [RunEventKind.NotDecoded] = 1,
      };
      foreach (var (kind, count) in expected)
      {
        Assert.That(events.Count(kind, long.MinValue, long.MaxValue), Is.EqualTo(count), $"{kind}: how many");
        var single = events.In(kind, long.MinValue, long.MaxValue).ToArray().Single();
        Assert.That(single.Ticks, Is.EqualTo(at[kind]), $"{kind}: when");
      }
      Assert.That(events.In(RunEventKind.OutOfOrder, long.MinValue, long.MaxValue)[0].FrameIndex, Is.EqualTo(8), "the older frame");
      var torn = events.In(RunEventKind.Torn, long.MinValue, long.MaxValue)[0];
      Assert.That((torn.FrameIndex, torn.OtherFrameIndex), Is.EqualTo(((ulong?)5, (ulong?)4)), "the torn capture's two frames");
      Assert.That(events.CapturesKnown);
    }

    [Test]
    public void Panel_DrawsBothLanes_AndTheKeyAndDescriptionNameEveryKind()
    {
      var (rows, _) = Capture();
      var drawing = ReportCard.Build(RunSection.Whole(Chart(rows)), ReportOptions.ShowOnly(new[] { ReportItem.Events, ReportItem.Description }));
      var marks = drawing.FlatShapes.OfType<RectShape>().Select(r => r.Class).ToHashSet();
      Assert.That(
        marks,
        Is.SupersetOf(new[] { "event-dropped", "event-older", "event-torn", "event-gap", "event-undecoded" }),
        "every kind has a column of its own"
      );
      var words = drawing.FlatShapes.OfType<TextRunsShape>().SelectMany(t => t.Runs).Where(r => r.Class.Length == 0).Select(r => r.Text.Trim());
      Assert.That(
        words.Where(w => w.Length > 0),
        Is.EqualTo(
          new[]
          {
            "frames:",
            "1 dropped",
            "1 out of order",
            "1 torn",
            "capture:",
            "1 not recorded",
            "2 dropped by the source",
            "1 missed",
            "1 not decoded",
          }
        )
      );
      Assert.That(
        drawing.FlatShapes.OfType<TextShape>().Select(t => t.Content),
        Has.Some.StartsWith("Capture: 1 not recorded, 2 dropped by the source, 1 missed, 1 not decoded; ")
      );
    }

    /// <summary>Hovering a lane column lists its events with the frames they name: the torn capture's two frames, the older frame.</summary>
    [Test]
    public void Hover_ListsTheColumnsEvents()
    {
      var (rows, at) = Capture();
      var chart = Chart(rows);
      var section = RunSection.Whole(chart);
      var drawing = ReportCard.Build(section, ReportOptions.ShowOnly(new[] { ReportItem.Events }));
      var plot = drawing.Plots.Single(p => p.Id == ReportItem.Events);
      var hover = new CardHover(section);
      double Seconds(long ticks) => (ticks - section.Data.OriginTicks) / (double)TimeSpan.TicksPerSecond;

      Assert.That(hover.Describe(plot, Seconds(at[RunEventKind.Torn]), 0.5), Does.Contain("torn: frames 5 and 4"));
      Assert.That(hover.Describe(plot, Seconds(at[RunEventKind.OutOfOrder]), 0.5), Does.Contain("an older frame, 8, shown again"));
      Assert.That(hover.Describe(plot, Seconds(at[RunEventKind.SourceDropped]), 0.5), Does.Contain("2 frames dropped by the capture source"));
      Assert.That(hover.Describe(plot, 0.001, 0.5), Is.Null, "nothing happened at the start");
    }

    /// <summary>Without the capture rows (a report drawn without captures.csv) the frames lane still shows; the capture lane is not known.</summary>
    [Test]
    public void WithoutCaptureRows_TheCaptureLaneIsNotKnown()
    {
      var (rows, _) = Capture();
      var chart = Chart(rows, withCaptures: false);
      var events = RunChartData.Of(chart).Events;
      Assert.That(events.CapturesKnown, Is.False);
      Assert.That(RunEvents.CaptureKinds.Sum(k => events.Count(k, long.MinValue, long.MaxValue)), Is.Zero);
      Assert.That(events.Count(RunEventKind.FramesDropped, long.MinValue, long.MaxValue), Is.EqualTo(1));
    }
  }
}
