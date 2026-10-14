//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a trace says of a frame, on frames and trace contents made up for each case: which of the system's frames a frame is, a thread's
//* time, the vertical blank nearest a display time, and a run that can not be placed.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Capture;
using MB.FramePacing.Data;
using NUnit.Framework;
using static MB.FramePacing.TraceJoin.UnitTest.TraceTestData;

namespace MB.FramePacing.TraceJoin.UnitTest
{
  [TestFixture]
  public class TraceJoinTests
  {
    private static SurfaceFrame Surface(long start, long end, long token, long? display = null, string? present = "On-time Present") =>
      new SurfaceFrame(start, end, token, display, "layer", present, "None", null, true, false);

    [Test]
    public void SurfaceFrameAt_IsTheOneThatBeganLastAndHasNotEnded()
    {
      var frames = new[] { Surface(100, 250, 1), Surface(200, 400, 2), Surface(300, 320, 3) };
      var starts = frames.Select(f => f.StartNs).ToArray();

      Assert.That(TraceJoin.SurfaceFrameAt(frames, starts, 99), Is.Null);
      Assert.That(TraceJoin.SurfaceFrameAt(frames, starts, 100)!.SurfaceFrameToken, Is.EqualTo(1));
      Assert.That(TraceJoin.SurfaceFrameAt(frames, starts, 250)!.SurfaceFrameToken, Is.EqualTo(2));
      Assert.That(TraceJoin.SurfaceFrameAt(frames, starts, 310)!.SurfaceFrameToken, Is.EqualTo(3));
      // The newest has ended: the one before it that has not
      Assert.That(TraceJoin.SurfaceFrameAt(frames, starts, 350)!.SurfaceFrameToken, Is.EqualTo(2));
      Assert.That(TraceJoin.SurfaceFrameAt(frames, starts, 401), Is.Null);
      Assert.That(TraceJoin.SurfaceFrameAt(Array.Empty<SurfaceFrame>(), Array.Empty<long>(), 5), Is.Null);
    }

    [Test]
    public void SurfaceFrameAt_LooksNoFurtherBackThanAFewFrames()
    {
      // One long frame, then many short ones that have ended
      var frames = new List<SurfaceFrame> { Surface(0, 10_000, 1) };
      for (int i = 1; i <= TraceJoin.OverlappingFrames; ++i)
        frames.Add(Surface(i * 10, (i * 10) + 5, i + 1));
      var starts = frames.Select(f => f.StartNs).ToArray();

      Assert.That(TraceJoin.SurfaceFrameAt(frames, starts, 9_000), Is.Null);
      Assert.That(TraceJoin.SurfaceFrameAt(frames, starts, (TraceJoin.OverlappingFrames - 1) * 10 + 7)!.SurfaceFrameToken, Is.EqualTo(1));
    }

    [Test]
    public void ThreadTime_IsRunningWaitingForACpuAndNeither()
    {
      var states = new[]
      {
        new ThreadState(0, 100, "S"),
        new ThreadState(100, 160, "Running"),
        new ThreadState(160, 180, "R"),
        new ThreadState(180, 190, "R+"),
        new ThreadState(190, 300, "Running"),
        new ThreadState(300, 500, "D"),
      };
      var starts = states.Select(s => s.StartNs).ToArray();

      Assert.That(TraceJoin.ThreadTime(states, starts, 100, 300), Is.EqualTo((170L, 30L, 0L)));
      // A stretch that begins and ends inside a state
      Assert.That(TraceJoin.ThreadTime(states, starts, 50, 350), Is.EqualTo((170L, 30L, 100L)));
      Assert.That(TraceJoin.ThreadTime(states, starts, 170, 175), Is.EqualTo((0L, 5L, 0L)));
      // Nothing of the thread in the stretch
      Assert.That(TraceJoin.ThreadTime(states, starts, 600, 700), Is.Null);
      Assert.That(TraceJoin.ThreadTime(Array.Empty<ThreadState>(), Array.Empty<long>(), 0, 10), Is.Null);
    }

    [Test]
    public void Join_ARowPerFrameWithWhatTheTraceHas()
    {
      // The application's clock is the system's, which is 1000 behind the trace's; the capture's clock is 100 ahead of the application's
      var snapshots = new[] { new ClockSnapshot(TraceNs: 1_000, ClockNs: 0) };
      var alignment = new TraceAlignment(TraceAnchor.Given, 100);
      var frames = Frames(
        Frame(1, 10_100, cpuStartNs: 9_000, cpuBusyNs: 500),
        // No CPU times: only the display side
        Frame(2, 20_100),
        // Its system frame has no display frame, and no vertical blank is near
        Frame(3, 35_100, cpuStartNs: 29_000, cpuBusyNs: 500)
      );
      var surface = new[] { Surface(9_900, 10_600, 11, display: 21, present: "Late Present"), Surface(29_900, 30_600, 13) };
      var presents = new Dictionary<long, long> { [21] = 10_950 };
      var states = new[] { new ThreadState(9_000, 10_300, "Running"), new ThreadState(10_300, 10_400, "R"), new ThreadState(10_400, 40_000, "S") };
      var vblanks = new long[] { 10_990, 20_990, 30_990 };

      var rows = TraceJoin.Join(frames, alignment, snapshots, 10_000, vblanks, surface, presents, states);

      Assert.That(
        rows[0],
        Is.EqualTo(
          new TraceFrame(
            FrameIndex: 1,
            DisplayTraceNs: 11_000,
            CpuStartTraceNs: 10_000,
            CpuEndTraceNs: 10_500,
            VBlankTraceNs: 10_990,
            DisplayMinusVBlankNs: 10,
            SurfaceFrameToken: 11,
            DisplayFrameToken: 21,
            LayerName: "layer",
            PresentType: "Late Present",
            JankType: "None",
            JankTag: null,
            OnTimeFinish: true,
            GpuComposition: false,
            SystemPresentTraceNs: 10_950,
            DisplayMinusSystemPresentNs: 50,
            CpuRunningNs: 300,
            CpuRunnableNs: 100,
            CpuSleepingNs: 100
          )
        )
      );
      Assert.That(rows[1], Is.EqualTo(new TraceFrame(2, 21_000, VBlankTraceNs: 20_990, DisplayMinusVBlankNs: 10)));
      Assert.That(rows[2].VBlankTraceNs, Is.Null);
      Assert.That(rows[2].SurfaceFrameToken, Is.EqualTo(13));
      Assert.That(rows[2].DisplayFrameToken, Is.Null);
      Assert.That(rows[2].SystemPresentTraceNs, Is.Null);
      Assert.That(rows[2].DisplayMinusSystemPresentNs, Is.Null);
      Assert.That((rows[2].CpuRunningNs, rows[2].CpuRunnableNs, rows[2].CpuSleepingNs), Is.EqualTo((0L, 0L, 500L)));
    }

    [Test]
    public void ARunThatCanNotBePlaced_IsRefusedWithWhatToDo()
    {
      var options = new TraceJoinOptions { Path = "run.pftrace" };

      // Without the frame record of the run nothing is joined: no record at all, another run's, and one that names none of its frames
      var none = new TraceRun("run", "1/", Frames(Frame(1, 100, cpuStartNs: 50), Frame(2, 200, cpuStartNs: 150)), Array.Empty<CaptureCsvRow>(), 100)
      {
        RunId = 4,
      };
      var bare = EmptyTrace(0, 1_000_000);
      Assert.That(Assert.Throws<TraceException>(() => TraceJoiner.Join(none, bare, options))!.Message, Does.Contain("'mbfp frame 4 <frame index>'"));
      var others = bare with { FrameRecords = new[] { new FrameRecordSlice(5, 1, 50, 60) } };
      Assert.That(Assert.Throws<TraceException>(() => TraceJoiner.Join(none, others, options))!.Message, Does.Contain("no frame record of this run"));
      var unnamed = bare with { FrameRecords = new[] { new FrameRecordSlice(4, 9, 50, 60) } };
      Assert.That(
        Assert.Throws<TraceException>(() => TraceJoiner.Join(none, unnamed, options))!.Message,
        Does.Contain("names none of the capture's frames")
      );

      // With it the frames are where their slices are; these markers have no CPU busy time and no schedule, so nothing ties the
      // capture's clock to the application's
      var data = bare with
      {
        FrameRecords = new[] { new FrameRecordSlice(4, 1, 50, 50), new FrameRecordSlice(4, 2, 150, 150) },
      };
      Assert.That(Assert.Throws<TraceException>(() => TraceJoiner.Join(none, data, options))!.Message, Does.Contain("capture offset"));
      // With the offset given it is placed, and says what by
      var given = TraceJoiner.Join(none, data, options with { CaptureOffsetNs = 40 });
      Assert.That(given.Alignment, Is.EqualTo(new TraceAlignment(TraceAnchor.Given, 40)));
      Assert.That(given.Notes, Does.Contain("frame record minus the markers' CPU start times: 0 ns, by 2 frames"));
      Assert.That(given.Rows.Select(row => row.DisplayTraceNs), Is.EqualTo(new long[] { 60, 160 }));
      Assert.That(given.VBlankNote, Is.EqualTo("none used"));
      Assert.That(given.Notes, Does.Contain("vertical blanks: none used"));

      var empty = none with { Frames = Frames() };
      Assert.Throws<TraceException>(() => TraceJoiner.Join(empty, data, options));

      var outside = none with { Frames = Frames(Frame(1, 100, cpuStartNs: 50, cpuBusyNs: 1), Frame(2, 200, cpuStartNs: 150, cpuBusyNs: 1)) };
      // A record that puts the run outside the trace (a trace of the application's own from another time, say): refused
      var elsewhere = bare with
      {
        FrameRecords = new[] { new FrameRecordSlice(4, 1, 5_000_050, 5_000_051), new FrameRecordSlice(4, 2, 6_000_150, 6_000_151) },
      };
      Assert.That(Assert.Throws<TraceException>(() => TraceJoiner.Join(outside, elsewhere, options))!.Message, Does.Contain("Only 0 of 2"));
    }

    [Test]
    public void VerticalBlanksThatAreTooFew_AreNotUsed()
    {
      var frames = Enumerable
        .Range(0, 20)
        .Select(index => Frame((ulong)index, 1_000 + (index * 100L), cpuStartNs: 900 + (index * 100L), cpuBusyNs: 10))
        .ToList();
      var run = new TraceRun("run", "1/", frames, Array.Empty<CaptureCsvRow>(), 100);
      var source = new VBlankSource(false, "vblank-0", 3);
      var data = EmptyTrace(0, 1_000_000) with
      {
        VBlanksFrom = source,
        VBlanks = new long[] { 5, 105, 205 },
        FrameRecords = frames
          .Select(frame => new FrameRecordSlice(0, frame.FrameIndex, 900 + ((long)frame.FrameIndex * 100), 910 + ((long)frame.FrameIndex * 100)))
          .ToList(),
      };

      var join = TraceJoiner.Join(run, data, new TraceJoinOptions { Path = "run.pftrace" });

      Assert.That(join.Fit, Is.Null);
      Assert.That(join.VBlankNote, Is.EqualTo("none used"));
      Assert.That(join.Notes, Does.Contain("vertical blanks of vblank-0: too few of them lie in the run, none used"));
      Assert.That(join.Rows.All(row => row.VBlankTraceNs == null), Is.True);
    }

    [Test]
    public void PickVBlanks_TheOneNamedOrTheKernelsWithTheMost()
    {
      var sources = new[]
      {
        new VBlankSource(false, "vblank-0", 10),
        new VBlankSource(false, "vblank-1", 30),
        new VBlankSource(true, "VSYNC-app", 99),
      };

      Assert.That(TraceLoader.PickVBlanks(null, sources)!.Name, Is.EqualTo("vblank-1"));
      Assert.That(TraceLoader.PickVBlanks("VSYNC-app", sources)!.IsCounter, Is.True);
      Assert.That(TraceLoader.PickVBlanks("none", sources), Is.Null);
      Assert.That(TraceLoader.PickVBlanks(null, sources.Where(s => s.IsCounter).ToList()), Is.Null);
      Assert.That(Assert.Throws<TraceException>(() => TraceLoader.PickVBlanks("x", sources))!.Message, Does.Contain("vblank-0, vblank-1, VSYNC-app"));
      Assert.That(
        Assert.Throws<TraceException>(() => TraceLoader.PickVBlanks("x", Array.Empty<VBlankSource>()))!.Message,
        Does.Contain("it has: none")
      );
      Assert.That(sources[0].Kind, Is.EqualTo("kernel"));
      Assert.That(sources[2].Kind, Is.EqualTo("counter"));
    }

    [Test]
    public void ARun_IsNamedAsTheScriptNamesIt()
    {
      var frames = Frames();
      var captures = Array.Empty<CaptureCsvRow>();
      Assert.That(
        TraceRun.Create(7, "seq", "named", frames, captures, 1, 1),
        Is.EqualTo(new TraceRun("named", "7/seq", frames, captures, 1) { RunId = 7, CapturePeriodNs = 1 })
      );
      Assert.That(TraceRun.Create(7, "seq", null, frames, captures, 1, 1).Name, Is.EqualTo("seq"));
      Assert.That(TraceRun.Create(7, "seq", string.Empty, frames, captures, 1, 1).Name, Is.EqualTo("seq"));
      Assert.That(
        TraceRun.Create(7, null, null, frames, captures, 1, 1),
        Is.EqualTo(new TraceRun("run 7", "7/", frames, captures, 1) { RunId = 7, CapturePeriodNs = 1 })
      );
      var golden = GoldenRun();
      Assert.That(golden.Name, Is.EqualTo("busy-full-rate"));
      Assert.That(golden.PeriodNs, Is.EqualTo(PeriodNs));
      Assert.That(TraceAugmenter.FileName("run-7"), Is.EqualTo("run-7-augmented.pftrace"));
    }

    private static TraceData EmptyTrace(long firstNs, long lastNs) =>
      new TraceData(
        firstNs,
        lastNs,
        Array.Empty<ClockSnapshot>(),
        new Dictionary<string, long>(),
        null,
        Array.Empty<long>(),
        Array.Empty<SurfaceFrame>(),
        new Dictionary<long, long>(),
        Array.Empty<ThreadState>(),
        Array.Empty<string>()
      );
  }
}
