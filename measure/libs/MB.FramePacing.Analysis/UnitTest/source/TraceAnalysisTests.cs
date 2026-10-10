//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL: an analysis that joins the capture with a Perfetto trace of the run. The SDK's golden capture analysed with the stored
//* trace that was made to fit it (measure/test-data/trace) writes, byte for byte, the per-frame CSV tools/trace_augment.py wrote; without
//* a trace the analysis output is what it is without the feature; and what does not fit is a warning, but for a trace the analysis itself
//* was given.
//*
//* The tests that read a trace need the user's trace_processor_shell: without one they are skipped, and they fail instead where
//* MB_REQUIRE_TRACE_PROCESSOR is set (CI).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.IO;
using System.Linq;
using MB.FramePacing.Capture;
using MB.FramePacing.TraceJoin;
using MB.FramePacing.TraceJoin.UnitTest;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  [NonParallelizable]
  public class TraceAnalysisTests
  {
    private const string TraceFile = "run-1-trace.csv";

    private TempFolder m_temp = null!;
    private string m_capture = string.Empty;

    [SetUp]
    public void CopyTheGoldenCapture()
    {
      m_temp = new TempFolder();
      m_capture = m_temp.File("capture");
      Directory.CreateDirectory(m_capture);
      foreach (string name in new[] { CaptureSessionInfo.FileName, CaptureSessionInfo.DataFileName })
        File.Copy(Path.Combine(TraceTestData.GoldenCapture(), name), Path.Combine(m_capture, name));
    }

    [TearDown]
    public void DeleteTheCopy() => m_temp.Dispose();

    private static string Text(string folder, string name) => File.ReadAllText(Path.Combine(folder, name)).ReplaceLineEndings("\n");

    private static TraceJoinOptions Stored() => TraceTestData.Options(TraceTestData.SyntheticTrace, withProcess: true);

    [Test]
    public void WithoutATrace_TheOutputIsTheGoldenData_AndNothingOfATrace()
    {
      var report = CaptureAnalyzer.Analyze(m_capture, new AnalysisOptions());

      string golden = Path.Combine(TraceTestData.GoldenCapture(), "analysis");
      Assert.That(Text(report.OutputDirectory, "run-1-frames.csv"), Is.EqualTo(Text(golden, "run-1-frames.csv")));
      Assert.That(Text(report.OutputDirectory, "captures.csv"), Is.EqualTo(Text(golden, "captures.csv")));
      Assert.That(report.TraceJoins, Is.Empty);
      Assert.That(report.TraceJoinOf(0), Is.Null);
      Assert.That(File.Exists(Path.Combine(report.OutputDirectory, TraceFile)), Is.False);
      Assert.That(report.Warnings, Has.None.Contains("Perfetto"));
      Assert.That(Text(report.OutputDirectory, "summary.json"), Does.Not.Contain("trace"));
      Assert.That(CaptureAnalyzer.TraceOf(m_capture, new AnalysisOptions()), Is.Null);
    }

    [Test]
    public void ATraceWithoutATraceProcessor_IsAWarning()
    {
      var report = CaptureAnalyzer.Analyze(m_capture, new AnalysisOptions { Trace = Stored() });

      Assert.That(report.TraceJoins, Is.Empty);
      Assert.That(
        report.Warnings.Single(w => w.Contains("Perfetto")),
        Does.Contain("synthetic.pftrace").And.Contain("trace processor was not found")
      );
      Assert.That(File.Exists(Path.Combine(report.OutputDirectory, TraceFile)), Is.False);
    }

    [Test]
    [Category("trace-processor")]
    public void TheGoldenCaptureWithTheStoredTrace_WritesTheScriptsCsv()
    {
      string processor = TraceTestData.FindProcessorOrIgnore();
      var plain = CaptureAnalyzer.Analyze(m_capture, new AnalysisOptions { OutputDirectory = m_temp.File("plain") });

      var report = CaptureAnalyzer.Analyze(m_capture, new AnalysisOptions { Trace = Stored(), TraceProcessorPath = processor });

      Assert.That(File.ReadAllText(Path.Combine(report.OutputDirectory, TraceFile)), Is.EqualTo(File.ReadAllText(TraceTestData.File("frames.csv"))));
      var join = report.TraceJoinOf(0)!;
      Assert.That(join.Rows.Count, Is.EqualTo(report.Timeline.Runs[0].Frames.Count));
      // How well the two line up is written next to it, as the numbers the join gives
      string check = File.ReadAllText(Path.Combine(report.OutputDirectory, "run-1-trace-check.txt"));
      Assert.That(check, Is.EqualTo(string.Join('\n', join.LineUp!.Lines()) + "\n"));
      Assert.That(check, Does.Contain("379 of 387 frames with a CPU start time have theirs"));
      Assert.That(join.Run.Name, Is.EqualTo("busy-full-rate"));
      Assert.That(report.Warnings, Does.Contain(CaptureAnalyzer.TraceNotice));
      // The rest of the output is what it is without a trace
      foreach (string name in new[] { "run-1-frames.csv", "captures.csv" })
        Assert.That(Text(report.OutputDirectory, name), Is.EqualTo(Text(plain.OutputDirectory, name)), name);
      // The trace itself is nothing the capture remembers: the analysis was given it
      Assert.That(Text(report.OutputDirectory, "summary.json"), Does.Not.Contain("\"trace\""));

      // Analysed again without the trace, the file of the analysis before is gone
      var again = CaptureAnalyzer.Analyze(m_capture, new AnalysisOptions());
      Assert.That(again.OutputDirectory, Is.EqualTo(report.OutputDirectory));
      Assert.That(File.Exists(Path.Combine(again.OutputDirectory, TraceFile)), Is.False);
      Assert.That(File.Exists(Path.Combine(again.OutputDirectory, "run-1-trace-check.txt")), Is.False);
    }

    [Test]
    [Category("trace-processor")]
    public void ATraceOfTheApplicationsOwn_IsHeldAgainstTheMarkers_AloneOrWithATraceToJoin()
    {
      string processor = TraceTestData.FindProcessorOrIgnore();
      string appTrace = TraceTestData.File(TraceTestData.AppTrace);
      const string CheckFile = "run-1-trace-check.txt";

      // Alone: nothing is joined, and the markers are held against the application's record
      var alone = new TraceJoinOptions { AppTracePath = appTrace };
      var report = CaptureAnalyzer.Analyze(m_capture, new AnalysisOptions { Trace = alone, TraceProcessorPath = processor });

      Assert.That(report.TraceJoins, Is.Empty);
      Assert.That(File.Exists(Path.Combine(report.OutputDirectory, TraceFile)), Is.False);
      Assert.That(report.Warnings, Has.None.Contains("Perfetto"));
      var record = report.TraceRecordChecks.Single()!;
      Assert.That((record.Slices, record.NamedFrames, record.StartDifferentFrames, record.BusyDifferentFrames), Is.EqualTo((379, 379, 0, 0)));
      var lines = report.TraceCheckLines(0)!;
      Assert.That(lines.Count, Is.EqualTo(2));
      Assert.That(lines[1], Is.EqualTo(record.Line()).And.Contain("animationNs: 4 of 379 frames differ"));
      Assert.That(File.ReadAllText(Path.Combine(report.OutputDirectory, CheckFile)), Is.EqualTo(string.Join('\n', lines) + "\n"));

      // With a trace to join: the frame record of the application's own trace counts before the one the joined trace has (which is 1500 ns late for
      // every ninth frame), and the frames only the joined trace names are held against its
      var both = Stored() with
      {
        AppTracePath = appTrace,
      };
      var joined = CaptureAnalyzer.Analyze(m_capture, new AnalysisOptions { Trace = both, TraceProcessorPath = processor });

      var lineUp = joined.TraceJoinOf(0)!.LineUp!;
      Assert.That(joined.TraceRecordChecks.Single(), Is.SameAs(lineUp.Record));
      Assert.That((lineUp.Record.Slices, lineUp.Record.NamedFrames, lineUp.Record.WorkedFrames), Is.EqualTo((758, 387, 387)));
      // Of the eight frames the application's trace has no slice of, the joined trace's is late for the one that is a ninth frame
      Assert.That(lineUp.Record.StartDifferentFrames, Is.EqualTo(new[] { 421, 471, 521, 571, 621, 671, 721, 771 }.Count(i => (i % 9) == 0)));
      Assert.That(joined.TraceCheckLines(0), Is.EqualTo(lineUp.Lines()));
      Assert.That(File.ReadAllText(Path.Combine(joined.OutputDirectory, TraceFile)), Is.EqualTo(File.ReadAllText(TraceTestData.File("frames.csv"))));

      // A capture remembers it, and without a trace processor it is a warning
      (CaptureSessionInfo.TryLoad(m_capture)! with { Trace = alone }).Save(m_capture);
      Assert.That(File.ReadAllText(Path.Combine(m_capture, CaptureSessionInfo.FileName)), Does.Contain("\"appTracePath\""));
      var remembered = CaptureAnalyzer.Analyze(m_capture, new AnalysisOptions { TraceProcessorPath = processor });
      Assert.That(remembered.TraceRecordChecks.Single()!.Line(), Is.EqualTo(record.Line()));
      var without = CaptureAnalyzer.Analyze(m_capture, new AnalysisOptions());
      Assert.That(without.TraceRecordChecks, Is.Empty);
      Assert.That(
        without.Warnings.Single(w => w.Contains("Perfetto")),
        Does.Contain("synthetic-app.pftrace").And.Contain("trace processor was not found")
      );
      Assert.That(File.Exists(Path.Combine(without.OutputDirectory, CheckFile)), Is.False);

      // Its trace gone: a warning for a capture's own, and the analysis stops for one it was given
      (CaptureSessionInfo.TryLoad(m_capture)! with { Trace = alone with { AppTracePath = m_temp.File("gone.pftrace") } }).Save(m_capture);
      var gone = CaptureAnalyzer.Analyze(m_capture, new AnalysisOptions { TraceProcessorPath = processor });
      Assert.That(gone.TraceRecordChecks, Is.Empty);
      Assert.That(gone.Warnings.Single(w => w.Contains("gone.pftrace")), Does.StartWith("The application's trace").And.Contain("does not exist"));
      var asked = new AnalysisOptions { TraceProcessorPath = processor, Trace = alone with { AppTracePath = m_temp.File("gone.pftrace") } };
      Assert.Throws<TraceException>(() => CaptureAnalyzer.Analyze(m_capture, asked));
    }

    [Test]
    [Category("trace-processor")]
    public void TheAugmentedTrace_IsTheScripts_AndIsWrittenOnlyForARunThatWasJoined()
    {
      string processor = TraceTestData.FindProcessorOrIgnore();
      var plain = CaptureAnalyzer.Analyze(m_capture, new AnalysisOptions());
      Assert.That(AugmentedTraceFiles.Write(plain, processor), Is.Empty);

      var report = CaptureAnalyzer.Analyze(m_capture, new AnalysisOptions { Trace = Stored(), TraceProcessorPath = processor });
      var files = AugmentedTraceFiles.Write(report, processor);

      Assert.That(files.Count, Is.EqualTo(1));
      Assert.That(files[0].Path, Is.EqualTo(Path.Combine(report.OutputDirectory, "run-1-augmented.pftrace")));
      Assert.That(TraceTestData.DigestOf(files[0].Path), Is.EqualTo(TraceTestData.Expected("augmented")));
      Assert.That(files[0].Checked, Is.True);
      Assert.That(files[0].Problems, Is.Empty);
      Assert.That(files[0].Events, Is.GreaterThan(2000));
      // Without a trace processor it is written and not read back
      var unchecked1 = AugmentedTraceFiles.Write(report, null);
      Assert.That(unchecked1[0].Checked, Is.False);
      Assert.That(TraceTestData.DigestOf(unchecked1[0].Path), Is.EqualTo(TraceTestData.Expected("augmented")));
      Assert.That(CaptureAnalyzer.RunFilePrefixes(report.Timeline.Runs), Is.EqualTo(new[] { "run-1" }));
    }

    [Test]
    [Category("trace-processor")]
    public void TheTraceACaptureRemembers_IsJoinedAgain_UnlessToldNotTo()
    {
      string processor = TraceTestData.FindProcessorOrIgnore();
      var session = CaptureSessionInfo.TryLoad(m_capture)!;
      (session with { Trace = Stored() }).Save(m_capture);
      Assert.That(File.ReadAllText(Path.Combine(m_capture, CaptureSessionInfo.FileName)), Does.Contain("\"trace\": {").And.Contain("\"process\""));
      Assert.That(CaptureAnalyzer.TraceOf(m_capture, new AnalysisOptions()), Is.EqualTo(Stored()));

      var report = CaptureAnalyzer.Analyze(m_capture, new AnalysisOptions { TraceProcessorPath = processor });
      Assert.That(report.TraceJoinOf(0), Is.Not.Null);
      Assert.That(File.Exists(Path.Combine(report.OutputDirectory, TraceFile)), Is.True);
      Assert.That(Text(report.OutputDirectory, "summary.json"), Does.Contain("\"trace\": {"));

      var none = new AnalysisOptions { TraceProcessorPath = processor, NoTrace = true };
      Assert.That(CaptureAnalyzer.TraceOf(m_capture, none), Is.Null);
      Assert.That(CaptureAnalyzer.Analyze(m_capture, none).TraceJoins, Is.Empty);
      Assert.That(File.Exists(Path.Combine(report.OutputDirectory, TraceFile)), Is.False);

      // Another trace for this analysis: the options' wins
      var other = Stored() with
      {
        Process = null,
      };
      Assert.That(CaptureAnalyzer.TraceOf(m_capture, new AnalysisOptions { Trace = other }), Is.EqualTo(other));
    }

    [Test]
    [Category("trace-processor")]
    public void WhatDoesNotFit_IsAWarning_ButForATraceTheAnalysisWasGiven()
    {
      string processor = TraceTestData.FindProcessorOrIgnore();
      var session = CaptureSessionInfo.TryLoad(m_capture)!;

      // The capture's own trace is gone: the analysis is written without the join
      (session with { Trace = Stored() with { Path = m_temp.File("gone.pftrace") } }).Save(m_capture);
      var gone = CaptureAnalyzer.Analyze(m_capture, new AnalysisOptions { TraceProcessorPath = processor });
      Assert.That(gone.TraceJoins, Is.Empty);
      Assert.That(gone.Warnings.Single(w => w.Contains("Perfetto")), Does.Contain("'gone.pftrace' was not joined").And.Contain("does not exist"));

      // The same trace given to the analysis itself stops it
      var asked = new AnalysisOptions { TraceProcessorPath = processor, Trace = Stored() with { Path = m_temp.File("gone.pftrace") } };
      Assert.Throws<TraceException>(() => CaptureAnalyzer.Analyze(m_capture, asked));

      // A trace without the frame record of the run (here the capture alone as a trace: nothing an application logged): a warning
      // for the run, and nothing is joined
      string bare = m_temp.File("bare.pftrace");
      TraceAugmenter.WriteCaptureAlone(bare, TraceTestData.GoldenRun());
      var off = new AnalysisOptions
      {
        TraceProcessorPath = processor,
        Trace = new TraceJoinOptions { Path = bare },
      };
      var report = CaptureAnalyzer.Analyze(m_capture, off);
      Assert.That(report.TraceJoins, Is.EqualTo(new TraceRunJoin?[] { null }));
      Assert.That(report.TraceRecordChecks, Is.EqualTo(new FrameRecordCheck?[] { null }));
      Assert.That(
        report.Warnings.Single(w => w.Contains("Perfetto")),
        Does.StartWith("Run 1: the Perfetto trace 'bare.pftrace' was not joined: The trace has no frame record of this run")
      );
      Assert.That(report.Warnings, Has.None.EqualTo(CaptureAnalyzer.TraceNotice));
      Assert.That(File.Exists(Path.Combine(report.OutputDirectory, TraceFile)), Is.False);
    }
  }
}
