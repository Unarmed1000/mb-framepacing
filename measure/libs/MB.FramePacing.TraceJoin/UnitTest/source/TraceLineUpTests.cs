//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How well a run's markers and a trace line up, as numbers: on frames and trace contents made up so that each number can be worked out
//* by hand, and on the stored trace, whose made-up application logs the frame record into it.
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
  public class TraceLineUpTests
  {
    [Test]
    public void TheNumbers_AreWhatTheFramesAndTheTraceGive()
    {
      // Three frames of 1000 ns apart: busy for 400, 600 and 200 ns. The thread ran for each frame's busy time but slept through half of
      // the second's; the system says the first two were presented 50 ns before they were measured, the third 700 ns before
      var frames = Frames(
        Frame(10, 1_500, cpuStartNs: 1_000, cpuBusyNs: 400),
        Frame(11, 2_500, cpuStartNs: 2_000, cpuBusyNs: 600),
        Frame(12, 3_500, cpuStartNs: 3_000, cpuBusyNs: 200),
        Frame(13, 4_500)
      );
      var rows = new[]
      {
        new TraceFrame(10, 1_500, 1_000, 1_400, DisplayMinusSystemPresentNs: 50, CpuRunningNs: 300, CpuRunnableNs: 100, CpuSleepingNs: 0),
        new TraceFrame(11, 2_500, 2_000, 2_600, DisplayMinusSystemPresentNs: 50, CpuRunningNs: 300, CpuRunnableNs: 0, CpuSleepingNs: 300),
        new TraceFrame(12, 3_500, 3_000, 3_200, DisplayMinusSystemPresentNs: 700, CpuRunningNs: 200, CpuRunnableNs: 0, CpuSleepingNs: 0),
        new TraceFrame(13, 4_500),
      };
      var states = new[]
      {
        new ThreadState(0, 1_000, "S"),
        new ThreadState(1_000, 1_300, "Running"),
        new ThreadState(1_300, 1_400, "R"),
        new ThreadState(1_400, 2_000, "S"),
        new ThreadState(2_000, 2_300, "Running"),
        new ThreadState(2_300, 3_000, "S"),
        new ThreadState(3_000, 3_200, "Running"),
        new ThreadState(3_200, 9_000, "S"),
      };
      var records = new[]
      {
        new FrameRecordSlice(1, 10, 1_000, 1_400),
        // Begun 30 ns after the marker's CPU start time and ended with it: 30 ns shorter than its CPU busy time
        new FrameRecordSlice(1, 11, 2_030, 2_600),
        // Another run's, and a second one of a frame: neither is this run's frame's
        new FrameRecordSlice(2, 12, 3_000, 3_200),
        new FrameRecordSlice(1, 10, 5_000, 5_400),
      };
      var record = FrameRecordCheck.Of(1, frames, records);

      var lineUp = TraceLineUpCheck.Check(rows, Data(states), periodNs: 1_000, record);

      Assert.That((lineUp.ScheduledFrames, lineUp.BusyNs, lineUp.AwakeNs, lineUp.MostlyAsleepFrames), Is.EqualTo((3, 1_200L, 900L, 0)));
      // A frame earlier: the second frame's 600 ns from 1000 (400 awake), the third's 200 ns from 2000 (200). A frame later: the first's
      // 400 ns from 2000 (300), the second's 600 ns from 3000 (200)
      Assert.That((lineUp.AwakeEarlierNs, lineUp.AwakeLaterNs), Is.EqualTo((600L, 500L)));
      Assert.That(
        (lineUp.PresentedFrames, lineUp.PresentDifferenceMedianNs, lineUp.PresentDifferenceMinNs, lineUp.PresentDifferenceMaxNs),
        Is.EqualTo((3, 50L, 50L, 700L))
      );
      Assert.That(lineUp.PresentDifferenceOffFrames, Is.EqualTo(1));
      Assert.That(lineUp.Record, Is.SameAs(record));
      Assert.That((record.Slices, record.NamedFrames, record.WorkedFrames), Is.EqualTo((3, 2, 3)));
      Assert.That(
        (record.ClockOffsetNs, record.StartDifferentFrames, record.StartDifferenceMinNs, record.StartDifferenceMaxNs),
        Is.EqualTo((0L, 1, 0L, 30L))
      );
      Assert.That(
        (record.BusyDifferentFrames, record.BusyDifferenceMinNs, record.BusyDifferenceMedianNs, record.BusyDifferenceMaxNs),
        Is.EqualTo((1, -30L, -30L, 0L))
      );

      var lines = lineUp.Lines();
      Assert.That(lines.Count, Is.EqualTo(4));
      Assert.That(lines[0], Does.Contain("numbers only").And.Contain("a delay the whole run shares"));
      Assert.That(
        lines[1],
        Does.Contain("3 frames").And.Contain("75.0 % of the markers' CPU busy time").And.Contain("earlier 50.0 %").And.Contain("later 41.7 %")
      );
      Assert.That(lines[2], Does.Contain("1 frames more than half a refresh from the median"));
      Assert.That(lines[3], Is.EqualTo(record.Line()).And.Contain("2 of 3 frames with a CPU start time have theirs"));
    }

    [Test]
    public void ATraceWithoutAPart_SaysSo()
    {
      var frames = Frames(Frame(1, 100), Frame(2, 200));
      var rows = new[] { new TraceFrame(1, 100), new TraceFrame(2, 200) };
      var record = FrameRecordCheck.Of(1, frames, Array.Empty<FrameRecordSlice>());

      var lineUp = TraceLineUpCheck.Check(rows, Data(Array.Empty<ThreadState>()), 100, record);

      Assert.That((lineUp.ScheduledFrames, lineUp.PresentedFrames, record.Slices, record.WorkedFrames), Is.EqualTo((0, 0, 0, 0)));
      var lines = lineUp.Lines();
      Assert.That(lines[1], Does.Contain("no scheduling of the application's thread"));
      Assert.That(lines[2], Does.Contain("no present time of the system's"));
      Assert.That(lines[3], Does.Contain("the trace has no slice named 'mbfp frame <run id> <frame index>'"));
    }

    [Test]
    [Category("trace-processor")]
    public void TheStoredTrace_LinesUpAsItWasMade()
    {
      string processor = FindProcessorOrIgnore();
      TraceLoader.Forget();
      var run = GoldenRun();
      var options = Options(SyntheticTrace, withProcess: true);

      var data = TraceLoader.Load(processor, options);
      var lineUp = TraceJoiner.Join(run, data, options).LineUp!;

      // The made-up application's thread runs for exactly each marker's CPU busy time (a frame in ten put aside for a while in it)
      Assert.That(lineUp.ScheduledFrames, Is.EqualTo(387));
      Assert.That(lineUp.AwakeNs, Is.EqualTo(lineUp.BusyNs));
      Assert.That(lineUp.AwakeEarlierNs, Is.LessThan(lineUp.BusyNs));
      Assert.That(lineUp.AwakeLaterNs, Is.LessThan(lineUp.BusyNs));
      Assert.That(lineUp.MostlyAsleepFrames, Is.Zero);
      // It logs the frame record as sections on its thread, for all frames but one in fifty, and begins every ninth 1500 ns late. Its
      // clock counts from an epoch of its own: the record says how far that is from the record's clock, and the run is placed by it
      var record = lineUp.Record;
      int late = data.FrameRecords.Count(slice => (slice.FrameIndex % 9) == 0);
      Assert.That(data.FrameRecords.Count, Is.EqualTo(379));
      Assert.That(data.FrameRecords.Select(slice => slice.Process).Distinct(), Is.EqualTo(new[] { Digest().GetProperty("process").GetString() }));
      Assert.That((record.Slices, record.NamedFrames, record.WorkedFrames), Is.EqualTo((379, 379, 387)));
      Assert.That(record.ClockOffsetNs, Is.EqualTo(Digest().GetProperty("appClockOffsetNs").GetInt64()));
      Assert.That((record.StartDifferentFrames, record.StartDifferenceMinNs, record.StartDifferenceMaxNs), Is.EqualTo((late, 0L, 1_500L)));
      Assert.That(
        (record.BusyDifferentFrames, record.BusyDifferenceMinNs, record.BusyDifferenceMedianNs, record.BusyDifferenceMaxNs),
        Is.EqualTo((late, -1_500L, 0L, 0L))
      );
      // Sections have no annotations
      Assert.That(record.Annotations, Is.Empty);
      // The made-up system says every frame was presented at its intended time. The capture measured the frames that are behind their plan
      // (a lateness of half a 60 Hz refresh or more: the late ones and those that stay behind after them) a refresh later: those frames are
      // off, and the number says how many
      const long HalfRefreshNs = 8_333_333;
      Assert.That(lineUp.PresentDifferenceMedianNs, Is.EqualTo(-3_000_000));
      Assert.That(
        lineUp.PresentDifferenceOffFrames,
        Is.EqualTo(run.Frames.Count(frame => frame.Lateness is { } lateness && lateness.Nanoseconds >= HalfRefreshNs))
      );
      Assert.That(lineUp.PresentDifferenceOffFrames, Is.EqualTo(92));
    }

    private static TraceData Data(IReadOnlyList<ThreadState> states) =>
      new TraceData(
        0,
        10_000,
        Array.Empty<ClockSnapshot>(),
        new Dictionary<string, long>(),
        null,
        Array.Empty<long>(),
        Array.Empty<SurfaceFrame>(),
        new Dictionary<long, long>(),
        states,
        Array.Empty<string>()
      );
  }
}
