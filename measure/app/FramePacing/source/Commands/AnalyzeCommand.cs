//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* 'analyze': decode a capture and report animation error per run.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using System.IO;
using MB.FramePacing.Analysis;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal static class AnalyzeCommand
  {
    public static Command Create()
    {
      var directoryArgument = new Argument<string>("capture") { Description = "The capture directory (contains frames.mbfc)." };
      var runOption = new Option<uint?>("--run") { Description = "Only analyse this run id." };
      var timeOption = new Option<TimeSource>("--time")
      {
        Description = "Capture clock: Auto (device if available), Device or Host.",
        DefaultValueFactory = _ => TimeSource.Auto,
      };
      var outputOption = new Option<string?>("--output", "-o") { Description = "Report directory (default: <capture>/analysis)." };

      var command = new Command("analyze", "Decode the markers of a capture and report animation error.")
      {
        directoryArgument,
        runOption,
        timeOption,
        outputOption,
      };
      command.SetAction(parseResult =>
      {
        var options = new AnalysisOptions
        {
          TimeSource = parseResult.GetValue(timeOption),
          Timeline = new TimelineOptions { RunId = parseResult.GetValue(runOption) },
          OutputDirectory = parseResult.GetValue(outputOption) is { } output ? Path.GetFullPath(output) : null,
          ToolVersion = Program.VersionString,
        };
        return Run(Path.GetFullPath(parseResult.GetValue(directoryArgument)!), options);
      });
      return command;
    }

    public static int Run(string directory, AnalysisOptions options)
    {
      try
      {
        AnalysisReport? report = null;
        AnsiConsole
          .Progress()
          .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new ElapsedTimeColumn())
          .Start(context =>
          {
            var task = context.AddTask("Decoding markers", maxValue: 1.0);
            report = CaptureAnalyzer.Analyze(directory, options, new Progress<double>(value => task.Value = value));
            task.Value = 1.0;
          });
        Print(report!);
        return Program.ResultSuccess;
      }
      catch (Exception ex)
      {
        Program.ReportError(ex);
        return Program.ResultError;
      }
    }

    public static void Print(AnalysisReport report)
    {
      var layout = report.Capture.Layout;
      AnsiConsole.MarkupLineInterpolated(
        $"Capture {report.Capture.Header.Width}x{report.Capture.Header.Height}, {report.Capture.Rows.Count} captures, period {report.CapturePeriodMs:0.###} ms ({report.Capture.TimeSource} clock), {layout.Locks.Count} marker(s) at {layout.ModuleSizePx:0.0} px/module"
      );
      foreach (var warning in report.Warnings)
        AnsiConsole.MarkupLineInterpolated($"[yellow]warning:[/] {warning}");

      foreach (var run in report.Timeline.Runs)
      {
        AnsiConsole.WriteLine();
        var title =
          $"Run {run.RunId}"
          + (run.Name != null ? $" '{run.Name}'" : string.Empty)
          + (run.StartTimeUtc is { } start ? $" started {start:yyyy-MM-dd HH:mm:ss} UTC" : string.Empty);
        AnsiConsole.Write(new Rule(Markup.Escape(title)).LeftJustified());
        foreach (var warning in run.Warnings)
          AnsiConsole.MarkupLineInterpolated($"[yellow]warning:[/] {warning}");

        var c = run.Counts;
        AnsiConsole.MarkupLineInterpolated(
          $"{c.PresentedFrames} presented frames from {c.Captures} captures: {c.Decoded} decoded, {c.Undecodable} undecodable, {c.Torn} torn, {c.NotRecorded} not recorded; {c.SkippedFrameIndices} frame indices never seen, {c.Segments} segment(s)"
        );

        var s = run.Statistics;
        var table = new Table()
          .AddColumn("ms")
          .AddColumn(new TableColumn("min").RightAligned())
          .AddColumn(new TableColumn("mean").RightAligned())
          .AddColumn(new TableColumn("p50").RightAligned())
          .AddColumn(new TableColumn("p95").RightAligned())
          .AddColumn(new TableColumn("p99").RightAligned())
          .AddColumn(new TableColumn("max").RightAligned())
          .AddColumn(new TableColumn("stddev").RightAligned());
        AddRow(table, "Display delta", s.DisplayDeltaMs);
        AddRow(table, "Animation delta", s.AnimationDeltaMs);
        AddRow(table, "Animation error", s.AnimationErrorMs);
        AddRow(table, "|Animation error|", s.AbsoluteAnimationErrorMs);
        AddRow(table, "Drift", s.DriftMs);
        AddRow(table, "On screen", s.OnScreenMs);
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLineInterpolated(
          $"{s.FramesWithAnimationError} frame(s) with |animation error| above the {report.CapturePeriodMs:0.###} ms measurement resolution."
        );
      }
      AnsiConsole.MarkupLineInterpolated($"[grey]Reports written to {report.OutputDirectory}[/]");
    }

    private static void AddRow(Table table, string name, Statistics stats)
    {
      table.AddRow(
        name,
        stats.Min.ToString("0.00"),
        stats.Mean.ToString("0.00"),
        stats.P50.ToString("0.00"),
        stats.P95.ToString("0.00"),
        stats.P99.ToString("0.00"),
        stats.Max.ToString("0.00"),
        stats.StdDev.ToString("0.00")
      );
    }
  }
}
