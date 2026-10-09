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
using static MB.FramePacing.Trace.UnitTest.TraceTestData;

namespace MB.FramePacing.Trace.UnitTest
{
  [TestFixture]
  public class TraceEventsTests
  {
    private static readonly TraceAlignment g_plain = new TraceAlignment(TraceAnchor.Nothing, 0);

    [Test]
    public void OnScreen_ASlicePerFrameFromItsDisplayTimeForItsTimeOnScreen()
    {
      var made = Frames(
        Frame(7, 1_000, onScreenNs: 500),
        Frame(8, 1_500, onScreenNs: 1_000, displayDeltaNs: 500, animationDeltaNs: 450, animationErrorNs: -50, latenessNs: 20, targetNs: 500),
        // On screen for longer than to the next frame's display time (a gap in the capture): cut there
        Frame(9, 2_000, onScreenNs: 300, skippedBefore: 2, flags: new[] { "SkippedBefore", "Late" }, cpuStartNs: 1_200, cpuBusyNs: 100)
      );

      var slices = TraceEvents.OnScreen(made, new TraceAlignment(TraceAnchor.Given, 100, AppClockOffsetNs: 10, VBlankShiftNs: 1), 500);

      Assert.That(
        slices.Select(s => (s.StartNs, s.EndNs, s.Name)),
        Is.EqualTo(new[] { (909L, 1_409L, "#7"), (1_409L, 1_909L, "#8"), (1_909L, 2_209L, "#9") })
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

      var slices = TraceEvents.OnScreen(golden, TraceAlignmentUtil.Align(golden), PeriodNs);

      Assert.That(slices.Count, Is.EqualTo(388));
      Assert.That(slices[0].Name, Is.EqualTo("#388"));
      Assert.That(slices[^1].Name, Is.EqualTo("#775"));
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

      var slices = TraceEvents.Intended(made, new TraceAlignment(TraceAnchor.Nothing, 0, AppClockOffsetNs: 5));

      Assert.That(
        slices.Select(s => (s.Name, s.StartNs, s.EndNs)),
        Is.EqualTo(new[] { ("#1", 1_005L, 1_505L), ("#3", 1_505L, 2_105L), ("#5", 2_105L, 2_505L) })
      );
      Assert.That(TraceEvents.Intended(Frames(Frame(1, 0)), g_plain), Is.Empty);
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

      var slices = TraceEvents.Cpu(made, g_plain);

      Assert.That(
        slices.Select(s => (s.Name, s.StartNs, s.EndNs, s.Flow)),
        Is.EqualTo(new[] { ("#1", 1_000L, 1_300L, 2UL), ("#2", 2_000L, 3_000L, 3UL), ("#3", 3_000L, 3_200L, 4UL), ("#6", 4_000L, 4_000L, 7UL) })
      );
      Assert.That(slices[1].Arguments, Is.EqualTo(new[] { TraceArgument.Of("frame index", 2), TraceArgument.Of("CPU busy (ms)", 0.0015) }));
    }

    [Test]
    public void TheGoldenCapture_HasBothForNearlyEveryFrame()
    {
      var golden = GoldenRun().Frames;
      var alignment = TraceAlignmentUtil.Align(golden);

      Assert.That(TraceEvents.Intended(golden, alignment).Count, Is.EqualTo(golden.Count(f => f.IntendedDisplayTime != null)));
      var cpuSlices = TraceEvents.Cpu(golden, alignment);
      Assert.That(cpuSlices.Count, Is.EqualTo(golden.Count(f => f.CpuStartTime != null && f.CpuBusy != null)));
      var shown = TraceEvents.OnScreen(golden, alignment, PeriodNs).Where(s => s.Flow != 0).ToDictionary(s => s.Flow);
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

      Assert.That(events.Count, Is.EqualTo(92));
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
    public void EveryTrack_HasItsEventsItsNameAndItsDescription()
    {
      var run = GoldenRun();
      var alignment = TraceAlignmentUtil.Align(run.Frames);
      var names = new HashSet<string>();
      foreach (var track in TraceTrackText.All)
      {
        Assert.That(names.Add(TraceTrackText.NameOf(track)), Is.True);
        Assert.That(TraceTrackText.DescriptionOf(track), Is.Not.Empty);
        var events = TraceEvents.Of(track, run.Frames, run.Captures, alignment, PeriodNs);
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
      Assert.That(names.Count, Is.EqualTo(8));
    }
  }
}
