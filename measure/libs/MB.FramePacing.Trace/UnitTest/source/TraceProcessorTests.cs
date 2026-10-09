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

namespace MB.FramePacing.Trace.UnitTest
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
    }

    [Test]
    public void WhatDoesNotFit_IsRefusedWithWhatToDo()
    {
      var run = TraceTestData.GoldenRun();
      // The markers' clock taken as the system's own, which it is not: the CPU start times are not in the trace
      var plain = new TraceOptions { Path = TraceTestData.File(TraceTestData.SyntheticTrace) };
      var data = TraceLoader.Load(m_processor, plain);
      Assert.That(Assert.Throws<TraceException>(() => TraceJoiner.Join(run, data, plain))!.Message, Does.Contain("application clock"));

      var options = TraceTestData.Options(TraceTestData.SyntheticTrace, withProcess: true);
      Assert.That(
        Assert.Throws<TraceException>(() => TraceLoader.Load(m_processor, options with { Thread = "no such thread" }))!.Message,
        Does.Contain("no such thread")
      );
      Assert.That(
        Assert.Throws<TraceException>(() => TraceLoader.Load(m_processor, options with { VBlanks = "no such source" }))!.Message,
        Does.Contain("vblank-0")
      );
      Assert.Throws<TraceException>(() => TraceLoader.Load(m_processor, options with { AppClock = "wall" }));
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
