//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A run's markers against the frame record of a trace: on frames and slices made up so that each number can be worked out by hand, and
//* on the stored trace of a made-up application's own, which has the record as the SDK defines it (sdk/doc/trace-frame-record.md): on a
//* track named for it under the application's process, with the marker's other times as annotations.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Linq;
using MB.FramePacing.Capture;
using NUnit.Framework;
using static MB.FramePacing.TraceJoin.UnitTest.TraceTestData;

namespace MB.FramePacing.TraceJoin.UnitTest
{
  [TestFixture]
  public class FrameRecordCheckTests
  {
    [Test]
    public void TheNumbers_AreWhatTheMarkersAndTheSlicesGive()
    {
      // The application's clock is 1000 ns behind the clock its slices are on
      var frames = Frames(
        Frame(10, 0, cpuStartNs: 100, cpuBusyNs: 40, intendedDisplayNs: 500, animationNs: 7, markerTargetNs: 16, markerPreferredNs: 16),
        Frame(11, 0, cpuStartNs: 200, cpuBusyNs: 50, intendedDisplayNs: 600, animationNs: 8),
        // No CPU busy time in the marker: its slice has no length
        Frame(12, 0, cpuStartNs: 300),
        // No slice of its own
        Frame(13, 0, cpuStartNs: 400, cpuBusyNs: 10),
        // No CPU start time: no slice is asked of it
        Frame(14, 0)
      );
      var records = new[]
      {
        new FrameRecordSlice(5, 10, 1_100, 1_140)
        {
          AnimationNs = 7,
          MarkerPreferredNs = 17,
          MarkerTargetNs = 16,
          IntendedDisplayNs = 500,
        },
        // Begun 3 ns late and ended on time
        new FrameRecordSlice(5, 11, 1_203, 1_250) { AnimationNs = 8 },
        new FrameRecordSlice(5, 12, 1_300, 1_300),
        // Another run's
        new FrameRecordSlice(6, 13, 1_400, 1_410),
        // A second one of a frame: the first counts
        new FrameRecordSlice(5, 10, 9_000, 9_001),
      };

      var check = FrameRecordCheck.Of(5, frames, records);

      Assert.That((check.Slices, check.NamedFrames, check.WorkedFrames), Is.EqualTo((4, 3, 4)));
      // The slices start 1000, 1003 and 1000 ns after their markers' CPU start times: the median places the run
      Assert.That(check.ClockOffsetNs, Is.EqualTo(1_000));
      Assert.That((check.StartDifferentFrames, check.StartDifferenceMinNs, check.StartDifferenceMaxNs), Is.EqualTo((1, 0L, 3L)));
      Assert.That(
        (check.BusyDifferentFrames, check.BusyDifferenceMinNs, check.BusyDifferenceMedianNs, check.BusyDifferenceMaxNs),
        Is.EqualTo((1, -3L, 0L, 0L))
      );
      Assert.That(
        check.Annotations,
        Is.EqualTo(new[] { ("animationNs", 2, 0), ("markerPreferredNs", 1, 1), ("markerTargetNs", 1, 0), ("intendedDisplayNs", 1, 0) })
      );
      Assert.That(
        check.Line(),
        Is.EqualTo(
          "  The markers as decoded against the application's frame record (mbfp frame <run id> <frame index>), 4 slices of this run: 3 of "
            + "4 frames with a CPU start time have theirs. Slice start minus the marker's CPU start time: median +1000 ns, which the frames "
            + "are placed by; 1 frames differ from it (from 0 to +3 ns). Slice length minus the marker's CPU busy time: 1 frames differ "
            + "(median 0 ns, from -3 to 0 ns). animationNs: 0 of 2 frames differ. markerPreferredNs: 1 of 1 frames differ. markerTargetNs: 0 "
            + "of 1 frames differ. intendedDisplayNs: 0 of 1 frames differ."
        )
      );
    }

    [Test]
    public void WithoutASliceOfTheRun_NothingIsHeldAgainstTheMarkers()
    {
      var frames = Frames(Frame(10, 0, cpuStartNs: 100, cpuBusyNs: 40));

      var check = FrameRecordCheck.Of(5, frames, new[] { new FrameRecordSlice(6, 10, 100, 140) });

      Assert.That((check.Slices, check.NamedFrames, check.WorkedFrames, check.ClockOffsetNs), Is.EqualTo((0, 0, 1, 0L)));
      Assert.That(check.Annotations, Is.Empty);
      Assert.That(check.Line(), Does.Contain("the trace has no slice named 'mbfp frame <run id> <frame index>' for this run"));
    }

    [Test]
    public void TheApplication_IsTheProcessMostOfTheRecordIsOn()
    {
      FrameRecordSlice On(string? process) => new FrameRecordSlice(1, 1, 0, 1) { Process = process };

      Assert.That(TraceLoader.ProcessOf(Array.Empty<FrameRecordSlice>()), Is.Null);
      Assert.That(TraceLoader.ProcessOf(new[] { On(null), On(null) }), Is.Null);
      Assert.That(TraceLoader.ProcessOf(new[] { On(null), On("game") }), Is.EqualTo("game"));
      Assert.That(TraceLoader.ProcessOf(new[] { On("other"), On("game"), On("game") }), Is.EqualTo("game"));
      // As many of each: the first
      Assert.That(TraceLoader.ProcessOf(new[] { On("other"), On("game") }), Is.EqualTo("other"));
      // A name is taken as it is where a pattern is asked for
      Assert.That(TraceReader.LiteralPattern("com.example.game"), Is.EqualTo("com.example.game"));
      Assert.That(TraceReader.LiteralPattern("a*b?c[d]"), Is.EqualTo("a[*]b[?]c[[]d]"));
    }

    [Test]
    [Category("trace-processor")]
    public void TheStoredTraceOfTheApplicationsOwn_HasTheRecordAsItWasMade()
    {
      string processor = FindProcessorOrIgnore();
      TraceLoader.Forget();
      var run = GoldenRun();
      long offset = Digest().GetProperty("appClockOffsetNs").GetInt64();
      var options = new TraceJoinOptions { AppTracePath = File(AppTrace) };

      var records = TraceLoader.LoadAppRecords(processor, options);

      // A slice for every frame with a CPU start time but one in fifty, and one of another run; on a track of the application's process
      Assert.That(records.Count, Is.EqualTo(380));
      Assert.That(records.Count(slice => slice.RunId == run.RunId), Is.EqualTo(379));
      Assert.That(TraceLoader.ProcessOf(records), Is.EqualTo(Digest().GetProperty("process").GetString()));
      Assert.That(TraceLoader.LoadAppRecords(processor, options), Is.SameAs(records));
      // Stamped on the record's clock, which the trace's own is not: its snapshot takes them back, to the nanosecond
      var first = run.Frames.First(frame => frame.CpuStartTime != null);
      Assert.That(records[0].FrameIndex, Is.EqualTo(first.FrameIndex));
      Assert.That(records[0].StartNs, Is.EqualTo(first.CpuStartTime!.Value.Nanoseconds + offset));
      Assert.That(records[0].EndNs - records[0].StartNs, Is.EqualTo(first.CpuBusy!.Value.Nanoseconds));

      var check = FrameRecordCheck.Of(run.RunId, run.Frames, records);

      // Every value is the marker's own: the made-up application's clock is a constant from the record's, and nothing differs but the
      // animation time of the four frames made a nanosecond off
      Assert.That((check.Slices, check.NamedFrames, check.WorkedFrames), Is.EqualTo((379, 379, 387)));
      Assert.That((check.ClockOffsetNs, check.StartDifferentFrames, check.BusyDifferentFrames), Is.EqualTo((offset, 0, 0)));
      Assert.That(
        check.Annotations,
        Is.EqualTo(new[] { ("animationNs", 379, 4), ("markerPreferredNs", 194, 0), ("markerTargetNs", 194, 0), ("intendedDisplayNs", 379, 0) })
      );

      // What a trace has to go by says whether it has the record
      using var trace = TraceProcessor.Start(processor, File(AppTrace));
      Assert.That(TraceReader.Inspect(trace).FrameRecords, Is.EqualTo(380));

      Assert.That(
        Assert.Throws<TraceException>(() => TraceLoader.LoadAppRecords(processor, options with { AppTracePath = File("no-such.pftrace") }))!.Message,
        Does.Contain("does not exist")
      );
    }
  }
}
