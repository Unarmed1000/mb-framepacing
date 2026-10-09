//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL: what a Perfetto trace says of a run's frames, on the report card and under the pointer. The golden analysis
//* (sdk/test-data/data) with the per-frame CSV that was made for it (measure/test-data/trace/frames.csv) next to its frames: the card's
//* description says a trace was joined and what it has for the frames, the hover has the trace's lines for a frame, a section has its own
//* frames' rows, and a run without a trace has none of it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.IO;
using System.Linq;
using MB.FramePacing.Analysis;
using MB.FramePacing.Capture;
using MB.FramePacing.Trace;
using MB.FramePacing.Trace.UnitTest;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class TraceChartTests
  {
    private const string Joined = "A Perfetto trace of the run was joined (experimental): its times are tied to the capture's by the frames only.";

    private TempFolder m_temp = null!;
    private string m_analysis = string.Empty;

    [SetUp]
    public void CopyTheGoldenAnalysis()
    {
      m_temp = new TempFolder();
      m_analysis = m_temp.File("analysis");
      Directory.CreateDirectory(m_analysis);
      string golden = Path.Combine(TraceTestData.GoldenCapture(), "analysis");
      foreach (string file in Directory.GetFiles(golden))
        File.Copy(file, Path.Combine(m_analysis, Path.GetFileName(file)));
    }

    [TearDown]
    public void DeleteTheCopy() => m_temp.Dispose();

    private ChartRun WithTheTrace()
    {
      File.Copy(TraceTestData.File("frames.csv"), Path.Combine(m_analysis, TraceFramesCsv.FileName("run-1")));
      return AnalysisOutput.Read(m_analysis)[0].Chart;
    }

    private static string[] Texts(CardDrawing card) => card.FlatShapes.OfType<TextShape>().Select(text => text.Content).ToArray();

    [Test]
    public void WithoutTheFile_ARunHasNoTrace_AndItsCardSaysNothingOfOne()
    {
      var chart = AnalysisOutput.Read(m_analysis)[0].Chart;

      Assert.That(chart.Trace, Is.Null);
      Assert.That(RunChartData.Of(chart).TraceFrames, Is.Null);
      Assert.That(Texts(ReportCard.Build(RunSection.Whole(chart))), Has.None.Contains("Perfetto"));
    }

    [Test]
    public void WithTheFile_TheDescriptionSaysWhatTheTraceHas()
    {
      var plain = AnalysisOutput.Read(m_analysis)[0].Chart;
      var chart = WithTheTrace();

      var card = ReportCard.Build(RunSection.Whole(chart));

      Assert.That(chart.Trace!.Count, Is.EqualTo(chart.Run.Frames.Count));
      var texts = Texts(card);
      Assert.That(texts, Does.Contain(Joined));
      Assert.That(
        texts,
        Does.Contain(
          "Of these 388 frames the trace has the system's own account of 387, its thread's scheduling for 387 and a vertical blank for 388."
        )
      );
      // Two lines more, and nothing else: the card without the trace is the card it was
      var before = ReportCard.Build(RunSection.Whole(plain));
      Assert.That(card.Height - before.Height, Is.EqualTo(38));
      Assert.That(texts.Except(Texts(before)).Count(), Is.EqualTo(2));
      Assert.That(ReportCard.Render(RunSection.Whole(chart with { Trace = null })), Is.EqualTo(ReportCard.Render(RunSection.Whole(plain))));
      // Nothing of the trace that names a program
      Assert.That(ReportCard.Render(RunSection.Whole(chart)), Does.Not.Contain("com.example"));
    }

    [Test]
    public void TheHover_HasTheTracesLinesForTheFrame()
    {
      var chart = WithTheTrace();
      var section = RunSection.Whole(chart);
      var plot = ReportCard.Build(section).Plots.First(p => p.Id == ReportItem.AnimationError);
      var hover = new CardHover(section);
      double At(int index) => (chart.Run.Frames[index].FirstSeenTime - chart.Run.Frames[0].FirstSeenTime).TotalSeconds + 0.001;

      // A late frame: the made-up system says so too, and blames the application
      int late = Enumerable.Range(0, chart.Run.Frames.Count).First(i => (chart.Run.Frames[i].Flags & PresentedFrameFlags.Late) != 0);
      string text = hover.Describe(plot, At(late), 0)!;
      Assert.That(text, Does.StartWith($"Frame {chart.Run.Frames[late].FrameIndex} at "));
      Assert.That(text, Does.Contain("\ntrace, the system's frame timeline: Late Present, jank App Deadline Missed"));
      Assert.That(text, Does.Contain("\ntrace, display time minus the system's present time +"));
      Assert.That(text, Does.Contain("\ntrace, CPU busy on its thread: running "));

      // A frame that was put aside for 0.2 ms in the middle of its work
      int waited = Enumerable.Range(0, chart.Trace!.Count).First(i => chart.Trace[i].CpuRunnableNs > 0);
      Assert.That(hover.Describe(plot, At(waited), 0), Does.Contain("waiting for a CPU 0.20 ms, neither 0.00 ms"));

      // A frame the trace has only a vertical blank for: no line of the trace
      int bare = Enumerable.Range(0, chart.Trace.Count).First(i => chart.Trace[i].SurfaceFrameToken == null);
      Assert.That(hover.Describe(plot, At(bare), 0), Does.Not.Contain("trace,"));
      // Without the trace the hover is what it was
      var plainHover = new CardHover(RunSection.Whole(chart with { Trace = null }));
      Assert.That(plainHover.Describe(plot, At(late), 0), Is.EqualTo(text[..text.IndexOf("\ntrace,", System.StringComparison.Ordinal)]));
      Assert.That(text, Does.Not.Contain("com.example"));
    }

    [Test]
    public void ASection_HasItsOwnFramesRows()
    {
      var chart = WithTheTrace();

      var section = RunSection.Create(chart, 1.0, 2.0);

      Assert.That(section.FrameCount, Is.GreaterThan(30).And.LessThan(chart.Run.Frames.Count));
      Assert.That(section.Section.Trace!.Select(row => row.FrameIndex), Is.EqualTo(section.Section.Run.Frames.Select(frame => frame.FrameIndex)));
      Assert.That(
        Texts(ReportCard.Build(section)),
        Has.Some.StartWith($"Of these {section.FrameCount} frames the trace has the system's own account of {section.FrameCount},")
      );
      // The section as a run of its own is drawn from its own rows
      Assert.That(RunChartData.Of(section.Section).TraceFrames!.Count, Is.EqualTo(section.FrameCount));
    }

    [Test]
    public void FramesThatAreNotInDisplayOrder_KeepTheirRows()
    {
      var chart = WithTheTrace();
      var shuffled = chart with { Run = chart.Run with { Frames = chart.Run.Frames.Reverse().ToList() }, Trace = chart.Trace!.Reverse().ToList() };

      var data = RunChartData.Of(shuffled);

      Assert.That(data.TraceFrames!.Select(row => row.FrameIndex), Is.EqualTo(data.Frames.Select(frame => frame.FrameIndex)));
      Assert.That(data.Frames[0].FrameIndex, Is.EqualTo(chart.Run.Frames[0].FrameIndex));
    }

    [Test]
    public void AFileOfAnotherAnalysis_IsRefused()
    {
      string path = Path.Combine(m_analysis, TraceFramesCsv.FileName("run-1"));
      var rows = TraceFramesCsv.Read(TraceTestData.File("frames.csv"));
      TraceFramesCsv.Write(path, rows.Take(10).ToList());

      var error = Assert.Throws<InvalidDataException>(() => AnalysisOutput.Read(m_analysis));

      Assert.That(error!.Message, Does.Contain("10 rows where the run has 388 frames"));
      var chart = AnalysisOutput.Read(Path.Combine(TraceTestData.GoldenCapture(), "analysis"))[0].Chart;
      Assert.Throws<System.ArgumentException>(() => RunChartData.Of(chart with { Trace = rows.Take(10).ToList() }));
    }

    [Test]
    [Category("trace-processor")]
    public void TheCardFromTheFiles_IsTheLiveOne_WithATraceToo()
    {
      string processor = TraceTestData.FindProcessorOrIgnore();
      string capture = m_temp.File("capture");
      Directory.CreateDirectory(capture);
      foreach (string name in new[] { CaptureSessionInfo.FileName, CaptureSessionInfo.DataFileName })
        File.Copy(Path.Combine(TraceTestData.GoldenCapture(), name), Path.Combine(capture, name));
      var options = new AnalysisOptions
      {
        Trace = TraceTestData.Options(TraceTestData.SyntheticTrace, withProcess: true),
        TraceProcessorPath = processor,
      };

      var report = CaptureAnalyzer.Analyze(capture, options);
      var live = ChartRun.From(report, report.Timeline.Runs[0]);
      var back = AnalysisOutput.Read(report.OutputDirectory)[0].Chart;

      Assert.That(live.Trace, Is.EqualTo(back.Trace));
      Assert.That(ReportCard.Render(RunSection.Whole(back)), Is.EqualTo(ReportCard.Render(RunSection.Whole(live))));
      Assert.That(ReportCard.Render(RunSection.Whole(live)), Does.Contain("A Perfetto trace of the run was joined"));
      // And the charts that are written with it
      Assert.That(ChartFiles.Write(report).Count, Is.EqualTo(5));
    }
  }
}
