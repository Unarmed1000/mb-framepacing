//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The display time step panel's hold kinds from the analysis of capture rows: frames the target dropped are told apart from frames a capture
//* gap may have hidden, and from older frames shown out of order.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Analysis;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class HoldKindTests
  {
    private const long Period = TimeSpan.TicksPerSecond / 60;

    /// <summary>
    /// Capture rows of a 60 Hz capture card: each entry is a frame index shown for one capture, or null for a capture not recorded. The pacer
    /// intends frame n for refresh n (its schedule in the markers), so a hold is only late when its next frame came after its intended time.
    /// </summary>
    private static RunChartData Data(params ulong?[] shown) => Data(schedule: true, shown);

    private static RunChartData Data(bool schedule, params ulong?[] shown)
    {
      var rows = new List<CaptureRow>();
      void Add(CaptureStatus status, MarkerPayload payload) =>
        rows.Add(new CaptureRow(rows.Count, status == CaptureStatus.NotRecorded ? 0 : rows.Count * Period, status, payload));
      for (int i = 0; i < 3; ++i)
        Add(CaptureStatus.Decoded, new MarkerPayload(MarkerKind.SequenceStart, 1, 0, MB.FrameMarker.MarkerFlags.None, 0));
      foreach (var index in shown)
      {
        if (index is { } frame)
          Add(
            CaptureStatus.Decoded,
            new MarkerPayload(
              MarkerKind.Frame,
              1,
              frame,
              MB.FrameMarker.MarkerFlags.None,
              (long)frame * Period,
              IntendedDisplayTicks: schedule ? 1_000_000 + ((long)frame * Period) : 0
            )
          );
        else
          Add(CaptureStatus.NotRecorded, default);
      }
      for (int i = 0; i < 3; ++i)
        Add(CaptureStatus.Decoded, new MarkerPayload(MarkerKind.SequenceEnd, 1, 999, MB.FrameMarker.MarkerFlags.None, 0));
      var result = TimelineAnalyzer.Analyze(rows);
      return RunChartData.Of(new ChartRun(result.Runs.Single(), result.CapturePeriodTicks, result.ErrorThresholdTicks, Camera: false));
    }

    /// <summary>Frame 4 never shown while every refresh was captured: the target dropped it, and frame 3's hold says so.</summary>
    [Test]
    public void SkippedFrame_WithoutACaptureGap_IsDroppedByTheTarget()
    {
      var data = Data(1, 2, 3, 3, 5, 6, 7);

      Assert.That(data.Frames.Select(f => f.FrameIndex), Is.EqualTo(new ulong[] { 1, 2, 3, 5, 6, 7 }));
      Assert.That(data.DroppedBeforeFrame[3], Is.EqualTo(1), "frame 4 was never shown before frame 5");
      Assert.That(data.HoldKinds[2], Is.EqualTo(HoldKind.FramesDropped));
      Assert.That(data.HoldKinds.Where((_, i) => i != 2 && i < 5), Is.All.EqualTo(HoldKind.AsPlanned));
    }

    /// <summary>
    /// Without the pacer's schedule the frame after a dropped one is measured against two frame times (one refresh each): it is not late, and
    /// the hold before it says frames were dropped, as with a schedule.
    /// </summary>
    [Test]
    public void SkippedFrame_WithoutASchedule_IsDroppedNotLate()
    {
      var data = Data(schedule: false, 1, 2, 3, 3, 5, 6, 7);

      Assert.That(data.Run.Run.Pacing!.Source, Is.EqualTo(PacingSource.NativeRefresh));
      Assert.That(data.Frames[3].TargetTicks, Is.EqualTo(2 * Period), "frame 5 is due two refreshes after frame 3");
      Assert.That(data.Frames.Select(f => f.Flags.HasFlag(PresentedFrameFlags.Late)), Is.All.False);
      Assert.That(data.HoldKinds[2], Is.EqualTo(HoldKind.FramesDropped));
    }

    /// <summary>
    /// The same frames, but the refresh after frame 3 was not recorded: frame 4 may have been on screen in it, so nothing is called dropped and
    /// the steps around the gap are not known.
    /// </summary>
    [Test]
    public void SkippedFrame_AcrossACaptureGap_IsNotCalledDropped()
    {
      var data = Data(1, 2, 3, null, 5, 6, 7);

      Assert.That(data.DroppedBeforeFrame, Is.All.Zero, "the capture may have missed frame 4");
      Assert.That(data.HoldKinds[2], Is.EqualTo(HoldKind.Unknown), "frame 3's hold ends at frame 5, whose first sighting is uncertain");
      Assert.That(data.HoldKinds[3], Is.EqualTo(HoldKind.Unknown), "frame 5's hold starts at that uncertain sighting");
      Assert.That(data.UncertainSteps.CountIn(0, data.Frames.Count), Is.EqualTo(2));
    }

    /// <summary>Frame 4 shown after frame 5, out of order: not dropped; frame 5's hold says an older frame came back.</summary>
    [Test]
    public void SkippedFrame_ShownLaterOutOfOrder_IsNotDropped()
    {
      var data = Data(1, 2, 3, 5, 4, 6, 7);

      Assert.That(data.DroppedBeforeFrame, Is.All.Zero, "frame 4 came back out of order");
      Assert.That(data.HoldKinds[3], Is.EqualTo(HoldKind.OlderFrameBack));
      Assert.That(data.Frames[3].OlderFrames!.Select(o => o.FrameIndex), Is.EqualTo(new ulong[] { 4 }));
    }
  }
}
