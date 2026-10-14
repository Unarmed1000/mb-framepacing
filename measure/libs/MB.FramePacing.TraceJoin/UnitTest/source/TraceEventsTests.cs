//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A capture's frames as the events of a trace's tracks, on the golden capture and on frames made up for each case: the cases of
//* tools/tests/test_trace_events.py, with the same numbers.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static MB.FramePacing.TraceJoin.UnitTest.TraceTestData;

namespace MB.FramePacing.TraceJoin.UnitTest
{
  [TestFixture]
  public class TraceEventsTests
  {
    private static readonly TraceAlignment g_plain = new TraceAlignment(TraceAnchor.Nothing, 0);

    // The golden capture's run id, as its markers carry it
    private const uint GoldenRunId = 1;

    [Test]
    public void OnScreen_ASlicePerFrameFromItsDisplayTimeForItsTimeOnScreen()
    {
      var made = Frames(
        Frame(7, 1_000, onScreenNs: 500),
        Frame(8, 1_500, onScreenNs: 1_000, displayDeltaNs: 500, animationDeltaNs: 450, animationErrorNs: -50, latenessNs: 20, targetNs: 500),
        // On screen for longer than to the next frame's display time (a gap in the capture): cut there
        Frame(9, 2_000, onScreenNs: 300, skippedBefore: 2, flags: new[] { "SkippedBefore", "Late" }, cpuStartNs: 1_200, cpuBusyNs: 100)
      );

      var slices = TraceEvents.OnScreen(made, 3, new TraceAlignment(TraceAnchor.Given, 100, AppClockOffsetNs: 10, VBlankShiftNs: 1), 500);

      // Named as the application's frame record names the frame
      Assert.That(
        slices.Select(s => (s.StartNs, s.EndNs, s.Name)),
        Is.EqualTo(new[] { (909L, 1_409L, "mbfp frame 3 7"), (1_409L, 1_909L, "mbfp frame 3 8"), (1_909L, 2_209L, "mbfp frame 3 9") })
      );
      Assert.That(slices.All(s => s.Track == TraceTrack.OnScreen && s.Kind == TraceEventKind.Slice), Is.True);
      Assert.That(
        slices[0].Arguments,
        Is.EqualTo(new[] { TraceArgument.Of("frame index", 7), TraceArgument.Of("on screen (ms)", 0.0005), TraceArgument.Of("captures", 1) })
      );
      Assert.That(
        slices[1].Arguments,
        Is.EqualTo(
          new[]
          {
            TraceArgument.Of("frame index", 8),
            TraceArgument.Of("on screen (ms)", 0.001),
            TraceArgument.Of("captures", 1),
            TraceArgument.Of("display time step (ms)", 0.0005),
            TraceArgument.Of("display time step (refreshes)", 1),
            TraceArgument.Of("animation time step (ms)", 0.00045),
            TraceArgument.Of("animation error (ms)", -0.00005),
            TraceArgument.Of("lateness (ms)", 0.00002),
            TraceArgument.Of("target frame time (ms)", 0.0005),
          }
        )
      );
      Assert.That(slices[2].Arguments, Does.Contain(TraceArgument.Of("frame indices skipped before", 2)));
      Assert.That(slices[2].Arguments, Does.Contain(TraceArgument.Of("flags", "SkippedBefore, Late")));
      // Only a frame that has a CPU slice is joined to one
      Assert.That(slices.Select(s => s.Flow), Is.EqualTo(new ulong[] { 0, 0, 10 }));
    }

    [Test]
    public void OnScreen_TheGoldenCapture()
    {
      var golden = GoldenRun().Frames;

      var slices = TraceEvents.OnScreen(golden, GoldenRunId, TraceAlignmentUtil.Align(golden), PeriodNs);

      Assert.That(slices.Count, Is.EqualTo(388));
      Assert.That(slices[0].Name, Is.EqualTo("mbfp frame 1 388"));
      Assert.That(slices[^1].Name, Is.EqualTo("mbfp frame 1 775"));
      for (int i = 0; i + 1 < slices.Count; ++i)
      {
        Assert.That(slices[i].StartNs, Is.LessThanOrEqualTo(slices[i].EndNs));
        Assert.That(slices[i].EndNs, Is.LessThanOrEqualTo(slices[i + 1].StartNs));
      }
    }

    [Test]
    public void Intended_SlicesRunToTheNextFramesIntendedTime()
    {
      var made = Frames(
        Frame(1, 0, intendedDisplayNs: 1_000, targetNs: 400),
        Frame(2, 0),
        Frame(3, 0, intendedDisplayNs: 1_500, targetNs: 400),
        // Before the one before it: left out, and the one before runs to the next that is not
        Frame(4, 0, intendedDisplayNs: 1_200, targetNs: 400),
        Frame(5, 0, intendedDisplayNs: 2_100, targetNs: 400)
      );

      var slices = TraceEvents.Intended(made, 2, new TraceAlignment(TraceAnchor.Nothing, 0, AppClockOffsetNs: 5));

      Assert.That(
        slices.Select(s => (s.Name, s.StartNs, s.EndNs)),
        Is.EqualTo(new[] { ("mbfp frame 2 1", 1_005L, 1_505L), ("mbfp frame 2 3", 1_505L, 2_105L), ("mbfp frame 2 5", 2_105L, 2_505L) })
      );
      Assert.That(TraceEvents.Intended(Frames(Frame(1, 0)), 2, g_plain), Is.Empty);
    }

    [Test]
    public void Cpu_SlicesAreCutWhereTheNextBeginsAndStayInTimeOrder()
    {
      var made = Frames(
        Frame(1, 0, cpuStartNs: 1_000, cpuBusyNs: 300),
        // Busy for longer than to the next frame's start
        Frame(2, 0, cpuStartNs: 2_000, cpuBusyNs: 1_500),
        Frame(3, 0, cpuStartNs: 3_000, cpuBusyNs: 200),
        // A start before the one before it: left out, and it cuts nothing
        Frame(4, 0, cpuStartNs: 2_500, cpuBusyNs: 200),
        Frame(5, 0, cpuBusyNs: 200),
        Frame(6, 0, cpuStartNs: 4_000, cpuBusyNs: 0)
      );

      var slices = TraceEvents.Cpu(made, uint.MaxValue, g_plain);

      Assert.That(
        slices.Select(s => (s.Name, s.StartNs, s.EndNs, s.Flow)),
        Is.EqualTo(
          new[]
          {
            ("mbfp frame 4294967295 1", 1_000L, 1_300L, 2UL),
            ("mbfp frame 4294967295 2", 2_000L, 3_000L, 3UL),
            ("mbfp frame 4294967295 3", 3_000L, 3_200L, 4UL),
            ("mbfp frame 4294967295 6", 4_000L, 4_000L, 7UL),
          }
        )
      );
      Assert.That(slices[1].Arguments, Is.EqualTo(new[] { TraceArgument.Of("frame index", 2), TraceArgument.Of("CPU busy (ms)", 0.0015) }));
    }

    [Test]
    public void TheGoldenCapture_HasBothForNearlyEveryFrame()
    {
      var golden = GoldenRun().Frames;
      var alignment = TraceAlignmentUtil.Align(golden);

      Assert.That(TraceEvents.Intended(golden, GoldenRunId, alignment).Count, Is.EqualTo(golden.Count(f => f.IntendedDisplayTime != null)));
      var cpuSlices = TraceEvents.Cpu(golden, GoldenRunId, alignment);
      Assert.That(cpuSlices.Count, Is.EqualTo(golden.Count(f => f.CpuStartTime != null && f.CpuBusy != null)));
      var shown = TraceEvents.OnScreen(golden, GoldenRunId, alignment, PeriodNs).Where(s => s.Flow != 0).ToDictionary(s => s.Flow);
      foreach (var work in cpuSlices)
      {
        // A frame's CPU work is done before the frame is on screen
        Assert.That(work.EndNs, Is.LessThanOrEqualTo(shown[work.Flow].StartNs));
      }
    }

    [Test]
    public void Counters_AValuePerFrameThatHasOne()
    {
      var made = Frames(
        Frame(1, 1_000),
        Frame(2, 2_000, displayDeltaNs: 1_000_000, animationErrorNs: -250_000, latenessNs: 0),
        Frame(3, 3_000, displayDeltaNs: 2_000_000)
      );

      Assert.That(
        TraceEvents.Counters(TraceTrack.AnimationError, made, g_plain).Select(v => (v.StartNs, v.Value)),
        Is.EqualTo(new[] { (2_000L, -0.25) })
      );
      Assert.That(
        TraceEvents.Counters(TraceTrack.DisplayStep, made, g_plain).Select(v => (v.StartNs, v.Value)),
        Is.EqualTo(new[] { (2_000L, 1.0), (3_000L, 2.0) })
      );
      Assert.That(TraceEvents.Counters(TraceTrack.Lateness, made, g_plain).Select(v => (v.StartNs, v.Value)), Is.EqualTo(new[] { (2_000L, 0.0) }));
      Assert.That(TraceEvents.Milliseconds(16_666_667), Is.EqualTo(16.666667));
      Assert.Throws<ArgumentOutOfRangeException>(() => TraceEvents.Counters(TraceTrack.Cpu, made, g_plain));
    }

    [Test]
    public void Counters_TheGoldenCapture()
    {
      var golden = GoldenRun().Frames;
      var alignment = TraceAlignmentUtil.Align(golden);

      Assert.That(TraceEvents.Counters(TraceTrack.Lateness, golden, alignment).Count, Is.EqualTo(golden.Count(f => f.Lateness != null)));
      Assert.That(TraceEvents.Counters(TraceTrack.DisplayStep, golden, alignment).Count, Is.EqualTo(387));
    }

    [Test]
    public void FrameEvents_SayWhatTheCaptureFound()
    {
      var made = Frames(
        Frame(10, 1_000, flags: new[] { "Late" }, latenessNs: 17_000_000),
        Frame(13, 2_000, skippedBefore: 2, flags: new[] { "SkippedBefore" }),
        Frame(14, 3_000, skippedBefore: 1, flags: new[] { "SkippedBefore", "Torn", "Late" }),
        Frame(15, 4_000, olderFrames: new[] { (12UL, 4_500L), (11UL, 4_200L) }),
        Frame(16, 5_000, flags: new[] { "StaticAfter" })
      );

      var events = TraceEvents.FrameEvents(made, new TraceAlignment(TraceAnchor.Given, 100));

      Assert.That(
        events.Select(e => (e.StartNs, e.Name)),
        Is.EqualTo(
          new[]
          {
            (900L, "late #10"),
            (1_900L, "2 frame indices skipped before #13"),
            (2_900L, "1 frame index skipped before #14"),
            (2_900L, "late #14"),
            (2_900L, "torn #14"),
            (4_100L, "out of order: #11 while #15 was the newest"),
            (4_400L, "out of order: #12 while #15 was the newest"),
          }
        )
      );
      Assert.That(events[0].Arguments, Is.EqualTo(new[] { TraceArgument.Of("frame index", 10), TraceArgument.Of("lateness (ms)", 17.0) }));
      Assert.That(events[3].Arguments, Is.EqualTo(new[] { TraceArgument.Of("frame index", 14) }));
      Assert.That(events.All(e => e.Kind == TraceEventKind.Instant), Is.True);
    }

    [Test]
    public void TheGoldenCapture_HasItsLateFrames()
    {
      var run = GoldenRun();
      var alignment = TraceAlignmentUtil.Align(run.Frames);

      var events = TraceEvents.FrameEvents(run.Frames, alignment);

      Assert.That(events.Count, Is.EqualTo(33));
      Assert.That(events.All(e => e.Name.StartsWith("late ", StringComparison.Ordinal)), Is.True);
      Assert.That(TraceEvents.CaptureEvents(run.Captures, alignment, TraceEvents.CaptureRangeOf(run.Frames)), Is.Empty);
    }

    [Test]
    public void CaptureEvents_AreTheCapturesOwnGaps()
    {
      var made = new[]
      {
        MadeCapture(1, 500, sourceDropsBefore: 3),
        MadeCapture(2, 1_000),
        MadeCapture(3, null, "NotRecorded"),
        MadeCapture(4, null, "NotRecorded"),
        MadeCapture(5, 2_000, "Undecodable", missedBefore: 1),
        MadeCapture(6, 3_000, sourceDropsBefore: 2),
        MadeCapture(7, null, "NotRecorded"),
        MadeCapture(8, 9_000),
      };

      var events = TraceEvents.CaptureEvents(made, new TraceAlignment(TraceAnchor.Given, 100), (1_000, 3_000));

      Assert.That(
        events.Select(e => (e.StartNs, e.Name, e.Arguments.Single().Whole)),
        Is.EqualTo(
          new[]
          {
            (1_900L, "not recorded: 2 before this capture", 5L),
            (1_900L, "missed: 1 before this capture", 5L),
            (1_900L, "not decoded", 5L),
            (2_900L, "dropped by the source: 2 before this capture", 6L),
          }
        )
      );
      Assert.That(TraceEvents.CaptureRangeOf(Frames()), Is.EqualTo((0L, -1L)));
      Assert.That(
        TraceEvents.CaptureRangeOf(Frames(Frame(1, 1_000, onScreenNs: 700), Frame(2, 1_700, onScreenNs: -5))),
        Is.EqualTo((1_000L, 1_700L))
      );
    }

    [Test]
    public void Refreshes_ARefreshPerCaptureNamedInTurnWithTheGapsLeftOpen()
    {
      var made = new[]
      {
        MadeCapture(1, 500),
        MadeCapture(2, 1_000),
        MadeCapture(3, 2_000, "Undecodable"),
        // Not recorded: no time, a gap
        MadeCapture(4, null, "NotRecorded"),
        MadeCapture(5, 4_000),
        // Closer than a period to the one before: cut there
        MadeCapture(6, 4_600),
        // A clock that went back: left out, and it cuts nothing
        MadeCapture(7, 4_500),
        MadeCapture(8, 6_000),
        MadeCapture(9, 9_000),
      };

      var slices = TraceEvents.Refreshes(made, new TraceAlignment(TraceAnchor.Given, 100, VBlankShiftNs: 10), (1_000, 6_000), 1_000);

      Assert.That(
        slices.Select(s => (s.StartNs, s.EndNs, s.Name, s.Arguments.Single().Whole)),
        Is.EqualTo(
          new[]
          {
            (890L, 1_890L, "refresh a", 2L),
            (1_890L, 2_890L, "refresh, not decoded", 3L),
            (3_890L, 4_490L, "refresh b", 5L),
            (4_490L, 5_490L, "refresh a", 6L),
            (5_890L, 6_890L, "refresh a", 8L),
          }
        )
      );
      Assert.That(slices.All(s => s.Track == TraceTrack.Refreshes && s.Kind == TraceEventKind.Slice), Is.True);
    }

    [Test]
    public void Refreshes_TheGoldenCaptureHasARefreshPerCaptureAndACameraNone()
    {
      var run = GoldenRun();
      var alignment = TraceAlignmentUtil.Align(run.Frames);

      var slices = TraceEvents.Of(TraceTrack.Refreshes, run, alignment);

      var (first, last) = TraceEvents.CaptureRangeOf(run.Frames);
      Assert.That(
        slices.Count,
        Is.EqualTo(run.Captures.Count(c => c.CaptureTime is { } time && time.Nanoseconds >= first && time.Nanoseconds <= last))
      );
      Assert.That(slices.Select(s => s.Name).Distinct().Order(), Is.EqualTo(new[] { "refresh a", "refresh b" }));
      for (int i = 0; i + 1 < slices.Count; ++i)
      {
        // A refresh long, cut where the next capture begins: the file's times are rounded to the nanosecond
        Assert.That(slices[i].EndNs - slices[i].StartNs, Is.EqualTo(PeriodNs - 1).Or.EqualTo(PeriodNs));
        Assert.That(slices[i].EndNs, Is.LessThanOrEqualTo(slices[i + 1].StartNs));
      }
      // A camera films faster than the display refreshes: its captures are not refreshes
      Assert.That(TraceEvents.Of(TraceTrack.Refreshes, run with { CapturePeriodNs = PeriodNs / 4 }, alignment), Is.Empty);
    }

    [Test]
    public void EveryTrack_HasItsEventsItsNameAndItsDescription()
    {
      var run = GoldenRun();
      var alignment = TraceAlignmentUtil.Align(run.Frames);
      var names = new HashSet<string>();
      foreach (var track in TraceTrackText.All)
      {
        Assert.That(names.Add(TraceTrackText.NameOf(track)), Is.True);
        Assert.That(TraceTrackText.DescriptionOf(track), Is.Not.Empty);
        var events = TraceEvents.Of(track, run, alignment);
        Assert.That(events.All(e => e.Track == track), Is.True, track.ToString());
        if (TraceTrackText.IsCounter(track))
        {
          Assert.That(events.All(e => e.Kind == TraceEventKind.Counter), Is.True);
          Assert.That(events.Count, Is.GreaterThan(380));
        }
        else if (track is TraceTrack.FrameEvents or TraceTrack.CaptureEvents)
        {
          Assert.That(events.All(e => e.Kind == TraceEventKind.Instant), Is.True);
        }
        else
        {
          Assert.That(events.All(e => e.Kind == TraceEventKind.Slice), Is.True);
          Assert.That(events.Count, Is.GreaterThan(380));
        }
      }
      Assert.That(names.Count, Is.EqualTo(9));
    }
  }
}
