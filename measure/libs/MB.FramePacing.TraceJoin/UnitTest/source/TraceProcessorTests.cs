//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The join from end to end with Perfetto's trace processor: the stored traces (measure/test-data/trace: made up to fit the golden
//* capture) read, joined and augmented, and the result read back. The per-frame CSV has to be, byte for byte, the one
//* tools/trace_augment.py wrote.
//*
//* These tests need the user's trace_processor_shell: without one they are skipped, and they fail instead where
//* MB_REQUIRE_TRACE_PROCESSOR is set (CI).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.IO;
using System.Linq;
using MB.FramePacing.Capture;
using NUnit.Framework;

namespace MB.FramePacing.TraceJoin.UnitTest
{
  [TestFixture]
  [Category("trace-processor")]
  [NonParallelizable]
  public class TraceProcessorTests
  {
    private string m_processor = string.Empty;

    [OneTimeSetUp]
    public void FindProcessor() => m_processor = TraceTestData.FindProcessorOrIgnore();

    [SetUp]
    public void Forget() => TraceLoader.Forget();

    [Test]
    public void TheStoredTrace_HasWhatItWasMadeWith()
    {
      var digest = TraceTestData.Digest();
      string process = digest.GetProperty("process").GetString()!;
      using var processor = TraceProcessor.Start(m_processor, TraceTestData.File(TraceTestData.SyntheticTrace));

      var info = TraceReader.Inspect(processor, process);

      Assert.That(info.LastNs, Is.GreaterThan(info.FirstNs));
      Assert.That(info.Clocks.Select(clock => clock.ClockId), Does.Contain(3L).And.Contain(6L));
      Assert.That(info.VBlankSources.Select(source => (source.Kind, source.Name)), Is.EqualTo(new[] { ("kernel", "vblank-0") }));
      Assert.That(info.Processes.Select(p => p.Name), Is.EqualTo(new[] { process }));
      Assert.That(info.Processes[0].TimelineFrames, Is.GreaterThan(380));
      Assert.That(info.Layers.Select(layer => layer.Layer), Is.EqualTo(new[] { digest.GetProperty("layer").GetString() }));
      Assert.That(info.Threads.Single(thread => thread.IsMain).Thread, Is.EqualTo("main"));
      Assert.That(info.Errors, Is.Empty);

      // Every process, without a pattern: no threads are listed then
      var all = TraceReader.Inspect(processor);
      Assert.That(all.Processes.Count, Is.GreaterThanOrEqualTo(3));
      Assert.That(all.Threads, Is.Empty);
    }

    [Test]
    public void AQueryThatIsRefused_SaysWhy()
    {
      using var processor = TraceProcessor.Start(m_processor, TraceTestData.File(TraceTestData.SyntheticTrace));

      var error = Assert.Throws<TraceException>(() => processor.Query("SELECT nothing FROM no_such_table"));

      Assert.That(error!.Message, Does.Contain("no_such_table"));
    }

    [Test]
    public void TheJoin_IsTheScriptsFramesCsv()
    {
      var run = TraceTestData.GoldenRun();
      var options = TraceTestData.Options(TraceTestData.SyntheticTrace, withProcess: true);

      var data = TraceLoader.Load(m_processor, options);
      var join = TraceJoiner.Join(run, data, options);

      Assert.That(TraceFramesCsv.ToText(join.Rows), Is.EqualTo(File.ReadAllText(TraceTestData.File("frames.csv"))));
      // The run is placed by the frame record: the made-up application's clock is that far from the record's, and nothing else said so
      Assert.That(join.Alignment.AppClockOffsetNs, Is.EqualTo(TraceTestData.Digest().GetProperty("appClockOffsetNs").GetInt64()));
      // And the application is the process the record is on: without its name the join is the same
      var unnamed = TraceTestData.Options(TraceTestData.SyntheticTrace, withProcess: false);
      var unnamedData = TraceLoader.Load(m_processor, unnamed);
      Assert.That(unnamedData.Notes, Does.Contain("the application's process is the one its frame record is on"));
      Assert.That(TraceFramesCsv.ToText(TraceJoiner.Join(run, unnamedData, unnamed).Rows), Is.EqualTo(TraceFramesCsv.ToText(join.Rows)));
      TraceLoader.Forget();
      data = TraceLoader.Load(m_processor, options);
      var digest = TraceTestData.Digest();
      Assert.That(join.Alignment.Anchor, Is.EqualTo(TraceAnchor.Schedule));
      Assert.That(join.Alignment.CaptureMinusAppNs, Is.EqualTo(digest.GetProperty("captureMinusAppNs").GetInt64()));
      Assert.That(join.Alignment.VBlankShiftNs, Is.EqualTo(digest.GetProperty("vblankShiftNs").GetInt64()));
      Assert.That(join.Fit!.Value.UsedFrames, Is.EqualTo(run.Frames.Count));
      Assert.That(join.Fit!.Value.SpreadNs, Is.Zero);
      Assert.That(
        data.Snapshots,
        Is.EqualTo(new[] { new ClockSnapshot(digest.GetProperty("snapshotTraceNs").GetInt64(), digest.GetProperty("snapshotClockNs").GetInt64()) })
      );
      // Asked for again, what was read is given back
      Assert.That(TraceLoader.Load(m_processor, options), Is.SameAs(data));
    }

    [TestCase(TraceTestData.SyntheticTrace, "augmented")]
    [TestCase(TraceTestData.PiecesTrace, "augmentedPieces")]
    public void TheAugmentedTrace_IsTheScripts_AndReadsBackWhole(string trace, string digest)
    {
      // A recording written to its file in pieces has marks between them, and a reader leaves out what comes after the last piece with a
      // time from an earlier one: the tracks are written before the trace's own packets, and all of them are there
      using var temp = new TempFolder();
      var run = TraceTestData.GoldenRun();
      var options = TraceTestData.Options(trace, withProcess: false);
      var data = TraceLoader.Load(m_processor, options);
      var join = TraceJoiner.Join(run, data, options);
      string output = temp.File("augmented.pftrace");

      var written = TraceAugmenter.Write(output, join);

      Assert.That(TraceTestData.DigestOf(output), Is.EqualTo(TraceTestData.Expected(digest)));
      Assert.That(TraceAugmenter.Check(output, m_processor, run.Name, written, data.Errors), Is.Empty);
      using var processor = TraceProcessor.Start(m_processor, output);
      var (slices, values) = TraceReader.Added(processor, TraceTrackWriter.RootName(run.Name));
      Assert.That(slices[TraceTrackText.NameOf(TraceTrack.OnScreen)], Is.EqualTo(run.Frames.Count));
      Assert.That(values[TraceTrackText.NameOf(TraceTrack.DisplayStep)], Is.EqualTo(run.Frames.Count - 1));
      Assert.That(TraceReader.Errors(processor), Is.Empty);
      Assert.That(TraceReader.TraceRange(processor), Is.EqualTo((data.FirstNs, data.LastNs)));
      // The tools' slices of a frame have the record's name, so that the viewer highlights them with it; they are no record
      using var recorded = TraceProcessor.Start(m_processor, TraceTestData.File(trace));
      var records = TraceReader.FrameRecords(recorded);
      Assert.That(records, Is.Not.Empty);
      Assert.That(TraceReader.FrameRecords(processor), Is.EqualTo(records));
      // Where the capture's tracks are, for the viewer to open at: inside the trace, and nothing in the recorded trace alone
      var added = TraceReader.AddedRange(processor);
      Assert.That(added, Is.Not.Null);
      var whole = TraceReader.TraceRange(processor);
      Assert.That(added!.Value.FirstNs, Is.GreaterThanOrEqualTo(whole.FirstNs).And.LessThan(added.Value.LastNs));
      Assert.That(added.Value.LastNs, Is.LessThanOrEqualTo(whole.LastNs));
      Assert.That(TraceReader.AddedRange(recorded), Is.Null);
    }

    [Test]
    public void WhatDoesNotFit_IsRefusedWithWhatToDo()
    {
      using var temp = new TempFolder();
      var run = TraceTestData.GoldenRun();
      // A trace without the frame record of the run is not joined: here the capture alone as a trace, which has the capture's tracks
      // and nothing an application logged
      string bare = temp.File("capture-alone.pftrace");
      TraceAugmenter.WriteCaptureAlone(bare, run);
      var bareOptions = new TraceJoinOptions { Path = bare };
      var without = TraceLoader.Load(m_processor, bareOptions);
      Assert.That(without.FrameRecords, Is.Empty);
      Assert.That(
        Assert.Throws<TraceException>(() => TraceJoiner.Join(run, without, bareOptions))!.Message,
        Does.Contain("no frame record of this run").And.Contain("'mbfp frame 1 <frame index>'").And.Contain("sdk/doc/trace-frame-record.md")
      );
      // Its record from a trace of the application's own: the capture alone has no snapshot of the record's clock, so nothing ties
      // the two files, and the run is not inside the trace
      var tied = bareOptions with
      {
        AppTracePath = TraceTestData.File(TraceTestData.AppTrace),
      };
      Assert.That(
        Assert.Throws<TraceException>(() => TraceJoiner.Join(run, TraceLoader.Load(m_processor, tied), tied))!.Message,
        Does.Contain("whose clock the trace does not share")
      );
      // Another run's markers: the trace's record is not theirs
      var other = run with
      {
        RunId = 9,
      };
      var stored = TraceTestData.Options(TraceTestData.SyntheticTrace, withProcess: false);
      Assert.That(
        Assert.Throws<TraceException>(() => TraceJoiner.Join(other, TraceLoader.Load(m_processor, stored), stored))!.Message,
        Does.Contain("'mbfp frame 9 <frame index>'")
      );

      var options = TraceTestData.Options(TraceTestData.SyntheticTrace, withProcess: true);
      Assert.That(
        Assert.Throws<TraceException>(() => TraceLoader.Load(m_processor, options with { Thread = "no such thread" }))!.Message,
        Does.Contain("no such thread")
      );
      Assert.That(
        Assert.Throws<TraceException>(() => TraceLoader.Load(m_processor, options with { VBlanks = "no such source" }))!.Message,
        Does.Contain("vblank-0")
      );
      Assert.Throws<TraceException>(() => TraceLoader.Load(m_processor, options with { Path = TraceTestData.File("no-such.pftrace") }));

      // Without vertical blanks the display times stay where the frames put them
      var none = options with
      {
        VBlanks = "none",
      };
      var join = TraceJoiner.Join(run, TraceLoader.Load(m_processor, none), none);
      Assert.That(join.Fit, Is.Null);
      Assert.That(join.Alignment.VBlankShiftNs, Is.Zero);
      Assert.That(join.Rows.All(row => row.VBlankTraceNs == null), Is.True);
      Assert.That(join.Rows.Count(row => row.SurfaceFrameToken != null), Is.GreaterThan(380));
    }

    [Test]
    public void AFileThatIsNoTrace_DoesNotStartAReader()
    {
      using var temp = new TempFolder();
      Assert.Throws<TraceException>(() => TraceProcessor.Start(m_processor, temp.File("missing.pftrace")));
      Assert.Throws<TraceException>(() => TraceProcessor.Start(temp.File("no-such-program"), TraceTestData.File(TraceTestData.SyntheticTrace)));
    }
  }
}
