//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A capture's clocks against a trace's, on the golden capture and on frames made up for each case: the cases of
//* tools/tests/test_trace_alignment.py, with the same numbers.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Data;
using NUnit.Framework;
using static MB.FramePacing.Trace.UnitTest.TraceTestData;

namespace MB.FramePacing.Trace.UnitTest
{
  [TestFixture]
  public class TraceAlignmentTests
  {
    private static readonly TraceAlignment g_plain = new TraceAlignment(TraceAnchor.Nothing, 0);

    [Test]
    public void ASchedule_GivesTheAnalysisOwnOffset()
    {
      Assert.That(TraceAlignmentUtil.CaptureMinusApp(GoldenRun().Frames), Is.EqualTo((TraceAnchor.Schedule, CaptureMinusPacerNs)));
    }

    [Test]
    public void FramesThatDisagree_AreRefused()
    {
      var made = Frames(Frame(1, 1_000, intendedDisplayNs: 400, latenessNs: 0), Frame(2, 2_000, intendedDisplayNs: 1_400, latenessNs: 1));

      var error = Assert.Throws<TraceException>(() => TraceAlignmentUtil.CaptureMinusApp(made));

      Assert.That(error!.Message, Does.Contain("2 values"));
    }

    [Test]
    public void WithoutASchedule_NoFrameIsOnScreenBeforeItsCpuWorkEnded()
    {
      // Shown 700, 500 and 900 ns after their CPU work ended: the clocks are lined up by the soonest
      var made = Frames(
        Frame(1, 10_700, cpuStartNs: 9_000, cpuBusyNs: 1_000),
        Frame(2, 20_500, cpuStartNs: 19_000, cpuBusyNs: 1_000),
        Frame(3, 30_900, cpuStartNs: 29_000, cpuBusyNs: 1_000),
        Frame(4, 40_000)
      );

      Assert.That(TraceAlignmentUtil.CaptureMinusApp(made), Is.EqualTo((TraceAnchor.CpuWork, 500L)));
    }

    [Test]
    public void MarkersWithoutTimes_PlaceNothing()
    {
      Assert.That(TraceAlignmentUtil.CaptureMinusApp(Frames(Frame(1, 10_000), Frame(2, 20_000))), Is.EqualTo((TraceAnchor.Nothing, 0L)));
      Assert.That(TraceAlignmentUtil.CaptureMinusApp(Frames()), Is.EqualTo((TraceAnchor.Nothing, 0L)));
    }

    [Test]
    public void AnOffsetThatIsGiven_IsTaken()
    {
      var frames = GoldenRun().Frames;
      Assert.That(TraceAlignmentUtil.Align(frames, 7, 123), Is.EqualTo(new TraceAlignment(TraceAnchor.Given, 123, 7)));
      Assert.That(TraceAlignmentUtil.Align(frames, 7), Is.EqualTo(new TraceAlignment(TraceAnchor.Schedule, CaptureMinusPacerNs, 7)));
    }

    [Test]
    public void Times_GoToTheSystemClock()
    {
      var alignment = new TraceAlignment(TraceAnchor.Schedule, CaptureMinusAppNs: 1_000, AppClockOffsetNs: 50, VBlankShiftNs: 3);
      Assert.That(alignment.MarkerTime(400), Is.EqualTo(450));
      Assert.That(alignment.CaptureTime(5_000), Is.EqualTo(4_050));
      Assert.That(alignment.DisplayTime(5_000), Is.EqualTo(4_047));
    }

    [Test]
    public void AnOnTimeFrame_IsDisplayedAtItsIntendedTime()
    {
      var frames = GoldenRun().Frames;
      var alignment = TraceAlignmentUtil.Align(frames);
      var onTime = frames.Where(f => f.Lateness?.Nanoseconds == 0 && f.IntendedDisplayTime != null).ToList();
      Assert.That(onTime.Count, Is.GreaterThan(100));
      foreach (var frame in onTime)
        Assert.That(
          alignment.DisplayTime(frame.FirstSeenTime.Nanoseconds),
          Is.EqualTo(alignment.MarkerTime(frame.IntendedDisplayTime!.Value.Nanoseconds))
        );
    }

    [Test]
    public void WithoutSnapshots_TheClockIsTheTraces()
    {
      Assert.That(TraceAlignmentUtil.ToTraceNs(123, Array.Empty<ClockSnapshot>()), Is.EqualTo(123));
    }

    [Test]
    public void TheNewestSnapshotAtOrBeforeATime_ConvertsIt()
    {
      // The system clock is 1000 behind the trace's, and 1010 behind after the second snapshot
      var snapshots = new[] { new ClockSnapshot(TraceNs: 5_000, ClockNs: 4_000), new ClockSnapshot(TraceNs: 9_010, ClockNs: 8_000) };
      Assert.That(TraceAlignmentUtil.ToTraceNs(4_000, snapshots), Is.EqualTo(5_000));
      Assert.That(TraceAlignmentUtil.ToTraceNs(7_999, snapshots), Is.EqualTo(8_999));
      Assert.That(TraceAlignmentUtil.ToTraceNs(8_000, snapshots), Is.EqualTo(9_010));
      Assert.That(TraceAlignmentUtil.ToTraceNs(8_500, snapshots), Is.EqualTo(9_510));
      // Before the first snapshot: by the first
      Assert.That(TraceAlignmentUtil.ToTraceNs(3_000, snapshots), Is.EqualTo(4_000));
    }

    [Test]
    public void CpuStartTimesInsideTheTrace_Pass()
    {
      var made = Enumerable.Range(1, 10).Select(index => Frame((ulong)index, 0, cpuStartNs: 1_000 * index)).ToList();
      var none = Array.Empty<ClockSnapshot>();
      Assert.DoesNotThrow(() => TraceAlignmentUtil.CheckInsideTrace(made, g_plain, none, 0, 20_000));
      // A trace that begins in the middle of the run: half of the frames are inside
      Assert.DoesNotThrow(() => TraceAlignmentUtil.CheckInsideTrace(made, g_plain, none, 6_000, 20_000));
      // No CPU start times: nothing to check
      Assert.DoesNotThrow(() => TraceAlignmentUtil.CheckInsideTrace(Frames(Frame(1, 0)), g_plain, none, 0, 1));
    }

    [Test]
    public void AnotherClockOrAnotherRun_IsRefused()
    {
      var made = Enumerable.Range(1, 10).Select(index => Frame((ulong)index, 0, cpuStartNs: 1_000 * index)).ToList();
      var none = Array.Empty<ClockSnapshot>();

      var late = Assert.Throws<TraceException>(() => TraceAlignmentUtil.CheckInsideTrace(made, g_plain, none, 7_000, 20_000));
      Assert.That(late!.Message, Does.Contain("Only 4 of 10 CPU start times"));

      // The same frames with the application's clock an hour off the system's
      var hourOff = new TraceAlignment(TraceAnchor.Nothing, 0, AppClockOffsetNs: 3_600_000_000_000);
      var off = Assert.Throws<TraceException>(() => TraceAlignmentUtil.CheckInsideTrace(made, hourOff, none, 0, 20_000));
      Assert.That(off!.Message, Does.Contain("application clock"));
    }

    [Test]
    public void Nearest_IsTheEarlierOfTwoThatAreAsNear()
    {
      var times = new long[] { 10, 20, 30 };
      Assert.That(TraceAlignmentUtil.Nearest(Array.Empty<long>(), 5), Is.Null);
      Assert.That(TraceAlignmentUtil.Nearest(times, 1), Is.EqualTo(10));
      Assert.That(TraceAlignmentUtil.Nearest(times, 99), Is.EqualTo(30));
      Assert.That(TraceAlignmentUtil.Nearest(times, 14), Is.EqualTo(10));
      Assert.That(TraceAlignmentUtil.Nearest(times, 15), Is.EqualTo(10));
      Assert.That(TraceAlignmentUtil.Nearest(times, 16), Is.EqualTo(20));
    }

    [Test]
    public void DisplayTimesALittleAfterTheVerticalBlanks()
    {
      var vblanks = Enumerable.Range(0, 100).Select(index => 1_000_000 + (index * PeriodNs)).ToList();
      // Every display time 2 ms after a vertical blank, a few of them a refresh or two late
      var displays = new[] { 3, 4, 5, 7, 8, 9, 12, 13, 14, 15 }.Select(index => vblanks[index] + 2_000_000).ToList();

      Assert.That(TraceAlignmentUtil.FitVBlanks(displays, vblanks, PeriodNs), Is.EqualTo(new VBlankFit(2_000_000, 10, 0)));
    }

    [Test]
    public void DisplayTimesALittleBeforeTheVerticalBlanks_MoveForward()
    {
      var vblanks = Enumerable.Range(0, 100).Select(index => 1_000_000 + (index * PeriodNs)).ToList();
      var displays = Enumerable.Range(10, 20).Select(index => vblanks[index] - 1_500_000).ToList();

      var fit = TraceAlignmentUtil.FitVBlanks(displays, vblanks, PeriodNs);

      Assert.That(fit!.Value.ShiftNs, Is.EqualTo(-1_500_000));
      Assert.That(fit.Value.SpreadNs, Is.Zero);
    }

    [Test]
    public void DisplayTimesOnBothSidesOfAVerticalBlank_AreOneGroup()
    {
      // Jitter of 0.3 ms either way around the vertical blanks: the points lie across the end of the refresh on the circle
      var vblanks = Enumerable.Range(1, 99).Select(index => index * PeriodNs).ToList();
      var jitter = new long[] { -300_000, 200_000, -100_000, 0, 300_000, 100_000, -200_000, 250_000, -250_000 };
      var displays = jitter.Select((off, index) => vblanks[10 + index] + off).ToList();

      Assert.That(TraceAlignmentUtil.FitVBlanks(displays, vblanks, PeriodNs), Is.EqualTo(new VBlankFit(0, jitter.Length, 300_000)));
    }

    [Test]
    public void TooFewVerticalBlanks_FitNothing()
    {
      Assert.That(TraceAlignmentUtil.FitVBlanks(new long[] { 10, 20, 30 }, Array.Empty<long>(), PeriodNs), Is.Null);
      // Vertical blanks that end before the run
      var vblanks = Enumerable.Range(0, 10).Select(index => index * PeriodNs).ToList();
      var displays = Enumerable.Range(50, 20).Select(index => (index * PeriodNs) + 5).ToList();
      Assert.That(TraceAlignmentUtil.FitVBlanks(displays, vblanks, PeriodNs), Is.Null);
      Assert.Throws<TraceException>(() => TraceAlignmentUtil.FitVBlanks(displays, vblanks, 0));
    }

    [Test]
    public void TheGoldenCapture_OnVerticalBlanksOfItsOwnRate()
    {
      // The display's vertical blanks are 5 ms before where the analysis put the frames: the clip's captures are a sixtieth of a second
      // apart to the nanosecond, and so are these vertical blanks
      IReadOnlyList<FrameRow> frames = GoldenRun().Frames;
      var alignment = TraceAlignmentUtil.Align(frames);
      var displays = frames.Select(f => alignment.DisplayTime(f.FirstSeenTime.Nanoseconds)).ToList();
      var vblanks = Enumerable.Range(0, 600).Select(index => (((index * 1_000_000_000L) + 30) / 60) - CaptureMinusPacerNs - 5_000_000).ToList();

      Assert.That(TraceAlignmentUtil.FitVBlanks(displays, vblanks, PeriodNs), Is.EqualTo(new VBlankFit(5_000_000, displays.Count, 0)));
    }

    [Test]
    public void TheApplicationClocks_HaveTheirNamesAndPerfettosIds()
    {
      Assert.That(TraceAppClockUtil.Names, Is.EqualTo(Enum.GetValues<TraceAppClock>().Select(TraceAppClockUtil.NameOf)));
      Assert.That(Enum.GetValues<TraceAppClock>().Select(TraceAppClockUtil.ClockId), Is.EqualTo(new[] { 3, 6, 5, 1 }));
      Assert.That(TraceAppClockUtil.TryParse("monotonic-raw", out var clock) && clock == TraceAppClock.MonotonicRaw, Is.True);
      Assert.That(TraceAppClockUtil.TryParse("Monotonic", out _), Is.False);
      Assert.That(TraceAppClockUtil.TryParse(null, out _), Is.False);
      Assert.That(TraceAppClockUtil.NameOfId(6), Is.EqualTo("boottime"));
      Assert.That(TraceAppClockUtil.NameOfId(64), Is.Null);
      Assert.That(new TraceOptions().Clock(), Is.EqualTo(TraceAppClock.Monotonic));
      Assert.That(new TraceOptions { AppClock = "realtime" }.Clock(), Is.EqualTo(TraceAppClock.Realtime));
      Assert.Throws<TraceException>(() => new TraceOptions { AppClock = "wall" }.Clock());
      foreach (var anchor in Enum.GetValues<TraceAnchor>())
        Assert.That(TraceAnchorText.Of(anchor), Is.Not.Empty);
    }
  }
}
