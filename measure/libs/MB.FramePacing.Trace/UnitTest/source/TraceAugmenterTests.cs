//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The traces this library writes are, byte for byte, the ones tools/trace_augment.py writes for the same capture and trace
//* (measure/test-data/trace/digest.json has their length and SHA-256): the capture alone, and each stored trace with the capture's tracks.
//* No trace processor is needed: the clocks of the made-up trace are in the digest.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using MB.FramePacing.Capture;
using NUnit.Framework;

namespace MB.FramePacing.Trace.UnitTest
{
  [TestFixture]
  public class TraceAugmenterTests
  {
    [Test]
    public void TheCaptureAlone_IsTheScriptsTrace()
    {
      using var temp = new TempFolder();
      string output = temp.File("capture-alone.pftrace");
      var run = TraceTestData.GoldenRun();

      var written = TraceAugmenter.WriteCaptureAlone(output, run);

      Assert.That(TraceTestData.DigestOf(output), Is.EqualTo(TraceTestData.Expected("captureAlone")));
      Assert.That(written["slices " + TraceTrackText.NameOf(TraceTrack.OnScreen)], Is.EqualTo(run.Frames.Count));
      Assert.That(written["values " + TraceTrackText.NameOf(TraceTrack.DisplayStep)], Is.EqualTo(run.Frames.Count - 1));
      Assert.That(File.Exists(output + ".partial"), Is.False);
    }

    [TestCase(TraceTestData.SyntheticTrace, "augmented")]
    [TestCase(TraceTestData.PiecesTrace, "augmentedPieces")]
    public void ATraceWithTheCapturesTracks_IsTheScriptsTrace(string trace, string digest)
    {
      using var temp = new TempFolder();
      string output = temp.File("augmented.pftrace");

      TraceAugmenter.Write(output, StoredJoin(TraceTestData.File(trace)));

      Assert.That(TraceTestData.DigestOf(output), Is.EqualTo(TraceTestData.Expected(digest)));
    }

    [Test]
    public void TheSameInputs_GiveTheSameBytes_AndAGzipTraceIsUnpacked()
    {
      using var temp = new TempFolder();
      string packed = temp.File("synthetic.pftrace.gz");
      using (var file = File.Create(packed))
      using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        gzip.Write(File.ReadAllBytes(TraceTestData.File(TraceTestData.SyntheticTrace)));

      string output = temp.File("from-gzip.pftrace");
      TraceAugmenter.Write(output, StoredJoin(packed));

      Assert.That(TraceTestData.DigestOf(output), Is.EqualTo(TraceTestData.Expected("augmented")));
    }

    [Test]
    public void AFileThatIsNoTrace_IsRefused_AndNothingIsLeftBehind()
    {
      using var temp = new TempFolder();
      string notes = temp.File("notes.txt");
      File.WriteAllText(notes, "not a trace");
      string zstd = temp.File("trace.zst");
      File.WriteAllBytes(zstd, new byte[] { 0x28, 0xb5, 0x2f, 0xfd, 0 });
      string empty = temp.File("empty.pftrace");
      File.WriteAllBytes(empty, Array.Empty<byte>());
      string output = temp.File("refused.pftrace");

      Assert.That(
        Assert.Throws<TraceException>(() => TraceAugmenter.Write(output, StoredJoin(notes)))!.Message,
        Does.Contain("not a Perfetto protobuf trace")
      );
      Assert.That(Assert.Throws<TraceException>(() => TraceAugmenter.Write(output, StoredJoin(zstd)))!.Message, Does.Contain("zstd"));
      Assert.Throws<TraceException>(() => TraceAugmenter.Write(output, StoredJoin(empty)));
      Assert.That(Directory.GetFiles(temp.Path, "refused*"), Is.Empty);
    }

    [Test]
    public void ATimeBeforeTheClocksZero_IsRefused()
    {
      using var temp = new TempFolder();
      var run = TraceTestData.GoldenRun();
      // The capture's clock a second ahead of the application's, and no offset to a system clock: the first frames are before zero
      var join = StoredJoin(TraceTestData.File(TraceTestData.SyntheticTrace)) with
      {
        Alignment = new TraceAlignment(TraceAnchor.Given, run.Frames[^1].FirstSeenTime.Nanoseconds + 1),
        Data = EmptyData(new List<ClockSnapshot>()),
      };

      var error = Assert.Throws<TraceException>(() => TraceAugmenter.Write(temp.File("refused.pftrace"), join));

      Assert.That(error!.Message, Does.Contain("application clock offset"));
    }

    [Test]
    public void Check_SaysWhatDoesNotReadBackAsWritten()
    {
      var written = new Dictionary<string, long> { ["slices A"] = 3, ["values B"] = 2 };
      var fine = new StubQueries(slices: new() { ["A"] = 3 }, values: new() { ["B"] = 2 }, errors: new() { ["old"] = 4 });
      Assert.That(TraceAugmenter.Check(fine, "run", written, new Dictionary<string, long> { ["old"] = 4 }), Is.Empty);

      var short1 = new StubQueries(slices: new() { ["A"] = 2 }, values: new(), errors: new() { ["old"] = 5, ["new"] = 1 });
      var problems = TraceAugmenter.Check(short1, "run", written, new Dictionary<string, long> { ["old"] = 4 });
      Assert.That(
        problems,
        Is.EqualTo(
          new[]
          {
            "the track 'A' has 2 of the 3 slices that were written",
            "the track 'B' has 0 of the 2 values that were written",
            "the trace processor counts 1 'new' where the recorded trace had 0",
            "the trace processor counts 5 'old' where the recorded trace had 4",
          }
        )
      );
      Assert.That(TraceAugmenter.Total(written), Is.EqualTo(5));
    }

    /// <summary>The golden run joined with a stored trace, as the script placed it: by the numbers of digest.json.</summary>
    private static TraceRunJoin StoredJoin(string tracePath)
    {
      var digest = TraceTestData.Digest();
      var run = TraceTestData.GoldenRun();
      long shift = digest.GetProperty("vblankShiftNs").GetInt64();
      var options = new TraceJoinOptions { Path = tracePath, AppClockOffsetNs = digest.GetProperty("appClockOffsetNs").GetInt64() };
      var alignment = TraceAlignmentUtil.Align(run.Frames, options.AppClockOffsetNs!.Value) with { VBlankShiftNs = shift };
      var snapshots = new[] { new ClockSnapshot(digest.GetProperty("snapshotTraceNs").GetInt64(), digest.GetProperty("snapshotClockNs").GetInt64()) };
      string note = $"{digest.GetProperty("vblanks").GetString()}, display times moved by {-shift} ns";
      return new TraceRunJoin(run, options, EmptyData(snapshots), alignment, null, note, Array.Empty<TraceFrame>(), Array.Empty<string>());
    }

    private static TraceData EmptyData(IReadOnlyList<ClockSnapshot> snapshots) =>
      new TraceData(
        0,
        0,
        snapshots,
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
