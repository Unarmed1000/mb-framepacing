//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Which of the tracks of one name a slice goes on (TrackLanes), held to the golden data every language reads
//* (test-data/trace/track-lanes.csv: sequences of slices, each on lanes of its own, with the lane, the lanes taken and the overflows after
//* each slice), and the record's lane from FrameRecord.TryCreate.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Trace.UnitTest
{
  [TestFixture]
  public class TrackLanesTests
  {
    private const string Header = "sequence,startNs,endNs,lane,laneCount,overflows";

    private static NanosecondTickCount At(long nanoseconds) => NanosecondTickCount.FromNanoseconds(nanoseconds);

    private static long Whole(string text) => long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static string GoldenFile()
    {
      var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
      while (directory != null && !File.Exists(Path.Combine(directory.FullName, "test-data", "trace", "track-lanes.csv")))
        directory = directory.Parent;
      return Path.Combine(
        directory?.FullName ?? throw new DirectoryNotFoundException("test-data/trace not found above the test directory"),
        "test-data",
        "trace",
        "track-lanes.csv"
      );
    }

    // A frame marker's payload whose record is the slice from cpuStart for cpuBusy
    private static Payload Frame(ulong frameIndex, long cpuStart, long cpuBusy) =>
      new Payload(
        MarkerKind.Frame,
        7,
        frameIndex,
        MarkerFlags.NoFlags,
        NanosecondTimeSpan.Zero,
        cpuStartTime: At(cpuStart),
        cpuBusy: NanosecondTimeDuration.FromNanoseconds(cpuBusy)
      );

    [Test]
    public void EverySequence_GetsTheLanesOfItsRows()
    {
      var lines = File.ReadAllLines(GoldenFile());
      Assert.That(lines[0], Is.EqualTo(Header));
      var lanes = new TrackLanes();
      string sequence = string.Empty;
      int rows = 0;
      int overflows = 0;
      foreach (var cells in lines.Skip(1).Where(line => line.Length > 0).Select(line => line.Split(',')))
      {
        string row = string.Join(',', cells);
        Assert.That(cells.Length, Is.EqualTo(6), row);
        if (cells[0] != sequence)
        {
          // Every sequence on lanes of its own
          lanes.Clear();
          sequence = cells[0];
        }
        ++rows;
        Assert.That(lanes.Assign(At(Whole(cells[1])), At(Whole(cells[2]))), Is.EqualTo(Whole(cells[3])), row);
        Assert.That(lanes.LaneCount, Is.EqualTo(Whole(cells[4])), row);
        Assert.That(lanes.Overflows, Is.EqualTo(Whole(cells[5])), row);
        overflows += lanes.Overflows;
      }
      Assert.That(rows, Is.GreaterThanOrEqualTo(50));
      Assert.That(overflows, Is.GreaterThan(0), "the file has the overflows too");
    }

    [Test]
    public void ASequentialLoop_UsesOneTrack_AndFramesInFlightOneEach()
    {
      var lanes = new TrackLanes();
      Assert.That(TrackLanes.MaxLaneCount, Is.EqualTo(16));
      Assert.That(lanes.LaneCount, Is.Zero);
      for (long frame = 0; frame < 10; ++frame)
        Assert.That(lanes.Assign(At(frame * 100), At((frame + 1) * 100)), Is.Zero);
      Assert.That(lanes.LaneCount, Is.EqualTo(1));

      lanes.Clear();
      Assert.That(lanes.Assign(At(0), At(150)), Is.EqualTo(0));
      Assert.That(lanes.Assign(At(100), At(250)), Is.EqualTo(1));
      Assert.That(lanes.Assign(At(200), At(350)), Is.EqualTo(0));
      Assert.That(lanes.Assign(At(400), At(450)), Is.EqualTo(0));
      Assert.That((lanes.LaneCount, lanes.Overflows), Is.EqualTo((2, 0)));
    }

    [Test]
    public void WithEveryLaneTaken_TheOneFreeFirst_IsCounted()
    {
      var lanes = new TrackLanes();
      for (int lane = 0; lane < TrackLanes.MaxLaneCount; ++lane)
        Assert.That(lanes.Assign(At(lane), At(lane == 5 ? 500 : 1000)), Is.EqualTo(lane));
      Assert.That(lanes.Assign(At(20), At(30)), Is.EqualTo(5));
      Assert.That(lanes.Overflows, Is.EqualTo(1));
      // The shorter slice did not free the lane early: at 400 the lane's slice to 500 is still open
      Assert.That(lanes.Assign(At(400), At(410)), Is.EqualTo(5));
      Assert.That(lanes.Overflows, Is.EqualTo(2));
      Assert.That(lanes.Assign(At(600), At(700)), Is.EqualTo(5), "free again");
      Assert.That(lanes.Overflows, Is.EqualTo(2));

      lanes.Clear();
      Assert.That((lanes.LaneCount, lanes.Overflows), Is.EqualTo((0, 0)));
      Assert.That(lanes.Assign(At(0), At(1)), Is.Zero, "nothing left of before");
    }

    [Test]
    public void ASliceThatEndsBeforeItStarts_IsRefused()
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new TrackLanes().Assign(At(10), At(9)));
    }

    [Test]
    public void TheRecordsOfFramesInFlight_GetTheirOwnLanes()
    {
      var lanes = new TrackLanes();
      // Frame 2 starts before frame 1 is presented, frame 3 after
      Assert.That(FrameRecord.TryCreate(Frame(1, 1_000, 1_500), lanes, out var first), Is.True);
      Assert.That(first.Lane, Is.Zero);
      Assert.That(FrameRecord.TryCreate(Frame(2, 2_000, 1_500), lanes, out var second), Is.True);
      Assert.That(second.Lane, Is.EqualTo(1));
      Assert.That(FrameRecord.TryCreate(Frame(3, 2_600, 100), lanes, out var third), Is.True);
      Assert.That((third.Lane, third.Name), Is.EqualTo((0, "mbfp frame 7 3")));

      // A marker that gets no record takes no lane
      Assert.That(FrameRecord.TryCreate(Frame(4, 0, 100), lanes, out var none), Is.False);
      Assert.That(none, Is.EqualTo(default(FrameRecord)));
      Assert.That(FrameRecord.TryCreate(Frame(4, 2_700, 100).WithKind(MarkerKind.Sync), lanes, out _), Is.False);
      Assert.That(lanes.LaneCount, Is.EqualTo(2));

      // Without lanes every record is on lane 0, and the lane is part of what a record is
      Assert.That(FrameRecord.TryCreate(Frame(2, 2_000, 1_500), out var unlaned), Is.True);
      Assert.That(unlaned.Lane, Is.Zero);
      Assert.That(unlaned == second, Is.False);
      Assert.That(unlaned.GetHashCode(), Is.Not.EqualTo(second.GetHashCode()));
      Assert.Throws<ArgumentNullException>(() => FrameRecord.TryCreate(Frame(5, 3_000, 100), null, out _));
    }

    [Test]
    public void LanesInAFrameLoop_AllocateNothing()
    {
      var lanes = new TrackLanes();
      var payload = Frame(1, 1_000, 400);
      // Once before counting: what the runtime sets up on a first call is not the frame loop's
      FrameRecord.TryCreate(payload, lanes, out _);
      long before = GC.GetAllocatedBytesForCurrentThread();

      int sum = 0;
      for (int i = 0; i < 1_000; ++i)
      {
        FrameRecord.TryCreate(Frame((ulong)i, 1_000 + (i * 300L), 400), lanes, out var record);
        sum += record.Lane + 1;
      }

      Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
      Assert.That(sum, Is.GreaterThan(0));
    }
  }
}
