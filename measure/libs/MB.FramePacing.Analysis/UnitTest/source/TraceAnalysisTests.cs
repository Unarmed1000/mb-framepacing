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
using MB.FramePacing.Trace;
using MB.FramePacing.Trace.UnitTest;
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

    private static TraceOptions Stored() => TraceTestData.Options(TraceTestData.SyntheticTrace, withProcess: true);

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
    }

    [Test]
    [Category("trace-processor")]
    public void TheTraceACaptureRemembers_IsJoinedAgain_UnlessToldNotTo()
    {
      string processor = TraceTestData.FindProcessorOrIgnore();
      var session = CaptureSessionInfo.TryLoad(m_capture)!;
      (session with { Trace = Stored() }).Save(m_capture);
      Assert.That(
        File.ReadAllText(Path.Combine(m_capture, CaptureSessionInfo.FileName)),
        Does.Contain("\"trace\": {").And.Contain("\"appClockOffsetNs\"")
      );
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

      // A run that is not inside the trace (the markers' clock taken as the system's, which it is not): a warning for the run
      var off = new AnalysisOptions { TraceProcessorPath = processor, Trace = Stored() with { AppClockOffsetNs = null } };
      var report = CaptureAnalyzer.Analyze(m_capture, off);
      Assert.That(report.TraceJoins, Is.EqualTo(new TraceRunJoin?[] { null }));
      Assert.That(
        report.Warnings.Single(w => w.Contains("Perfetto")),
        Does.StartWith("Run 1: the Perfetto trace 'synthetic.pftrace' was not joined")
      );
      Assert.That(report.Warnings, Has.None.EqualTo(CaptureAnalyzer.TraceNotice));
      Assert.That(File.Exists(Path.Combine(report.OutputDirectory, TraceFile)), Is.False);
    }
  }
}
