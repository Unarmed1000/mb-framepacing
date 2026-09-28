//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* 'analyze': decode a capture and report animation error per run.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using System.Globalization;
using System.IO;
using MB.FramePacing.Analysis;
using MB.FramePacing.Charts;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal static class AnalyzeCommand
  {
    public static Command Create()
    {
      var directoryArgument = new Argument<string>("capture")
      {
        Description = "The capture directory (contains captures.mbcd, or only frames.mbfc for older captures).",
      };
      var redecodeOption = new Option<bool>("--redecode")
      {
        Description = "Decode the stored frames (frames.mbfc) again and replace the capture data (captures.mbcd), e.g. after a decoder update.",
      };
      var runOption = new Option<uint?>("--run") { Description = "Only analyse this run id." };
      var timeOption = new Option<TimeSource>("--time")
      {
        Description = "Capture clock: Auto (device if available), Device or Host.",
        DefaultValueFactory = _ => TimeSource.Auto,
      };
      var outputOption = new Option<string?>("--output", "-o") { Description = "Report directory (default: <capture>/analysis)." };
      var targetOption = CommonOptions.TargetFps("overrides the one stored in capture.json");
      var nameOption = CommonOptions.Name("overrides the one stored in capture.json");
      var displayOption = CommonOptions.DisplayHz("overrides the one stored in capture.json");
      var chartsOption = CommonOptions.Charts();
      var thresholdOption = new Option<double?>("--error-threshold-ms")
      {
        Description =
          $"The |animation error| above which a frame counts as off, in ms (default {TimelineAnalyzer.DefaultErrorThresholdTicks / (double)TimeSpan.TicksPerMillisecond:0.###}).",
        Validators =
        {
          result =>
          {
            if (result.GetValueOrDefault<double?>() is <= 0)
              result.AddError("--error-threshold-ms must be greater than 0.");
          },
        },
      };

      var command = new Command("analyze", "Report the animation error of a capture (decoding its frames first when it only has frames).")
      {
        directoryArgument,
        redecodeOption,
        runOption,
        timeOption,
        outputOption,
        targetOption,
        nameOption,
        displayOption,
        thresholdOption,
        chartsOption,
      };
      command.SetAction(parseResult =>
      {
        var options = new AnalysisOptions
        {
          TimeSource = parseResult.GetValue(timeOption),
          Redecode = parseResult.GetValue(redecodeOption),
          Name = parseResult.GetValue(nameOption),
          Timeline = new TimelineOptions
          {
            RunId = parseResult.GetValue(runOption),
            TargetFps = parseResult.GetValue(targetOption),
            ExpectedRefreshHz = parseResult.GetValue(displayOption),
            ErrorThresholdTicks = parseResult.GetValue(thresholdOption) is { } ms
              ? (long)Math.Round(ms * TimeSpan.TicksPerMillisecond)
              : TimelineAnalyzer.DefaultErrorThresholdTicks,
          },
          OutputDirectory = parseResult.GetValue(outputOption) is { } output ? Path.GetFullPath(output) : null,
          ToolVersion = Program.VersionString,
        };
        return Run(Path.GetFullPath(parseResult.GetValue(directoryArgument)!), options, charts: parseResult.GetValue(chartsOption));
      });
      return command;
    }

    /// <summary>Analyses a capture, writes the reports (and the chart images when <paramref name="charts"/> is set) and prints the results.</summary>
    public static int Run(string directory, AnalysisOptions options, bool charts)
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
        var chartFiles = charts ? ChartFiles.Write(report!, ChartTheme.Light) : Array.Empty<string>();
        Print(report!);
        if (chartFiles.Count > 0)
          AnsiConsole.MarkupLineInterpolated($"[grey]{chartFiles.Count} chart image(s) written next to them (run-*-timeline.png, ...)[/]");
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
          + ((run.Name ?? run.SequenceId) is { } shown ? $" '{shown}'" : string.Empty)
          + (run.Name != null && run.SequenceId != null ? $" (sequence id {run.SequenceId})" : string.Empty)
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
        AddRow(table, "Display time step", s.DisplayDeltaMs);
        AddRow(table, "Animation time step", s.AnimationDeltaMs);
        AddRow(table, "Animation error", s.AnimationErrorMs);
        AddRow(table, "|Animation error|", s.AbsoluteAnimationErrorMs);
        AddRow(table, "Drift", s.DriftMs);
        AddRow(table, "On screen", s.OnScreenMs);
        // The application side, when the markers carry it
        if (s.FrameTimeMs.Count > 0)
          AddRow(table, "Frametime", s.FrameTimeMs);
        if (s.CpuBusyMs.Count > 0)
          AddRow(table, "CPU busy", s.CpuBusyMs);
        if (s.CpuWaitMs.Count > 0)
          AddRow(table, "CPU wait", s.CpuWaitMs);
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLineInterpolated(
          $"{s.FramesWithAnimationError} frame(s) with |animation error| above {report.ErrorThresholdMs:0.###} ms (the error threshold, --error-threshold-ms)."
        );
        AnsiConsole.MarkupLineInterpolated(
          $"Error per frame {s.ErrorPerFrameMs:0.00} ms (the mean |animation error|), percent error {s.PercentError:0.0} % (all |animation error| over the time on screen), as Gamers Nexus report them."
        );
        if (run.Pacing is { } pacing)
        {
          AnsiConsole.MarkupLineInterpolated(
            $"{pacing.LateFrames} late frame(s) ({pacing.LateShare:P1}; worst {LateShare.WindowSeconds:0} s: {pacing.WorstLateShare:P1}), {(pacing.Source == PacingSource.Schedule ? "shown half a refresh or more after their intended display time" : $"shown a refresh or more after the {pacing.TargetFrameMs:0.##} ms target")} ({TargetReason(pacing)})."
          );
          AnsiConsole.MarkupLineInterpolated($"{RefreshText(pacing)}");
          if (pacing.PacingErrorMs is { } pacingError && pacing.PredictionErrorMs is { } predictionError)
            AnsiConsole.MarkupLineInterpolated(
              $"Against the pacer's schedule: pacing error p95 {pacingError.P95:0.##} ms (max {pacingError.Max:0.##}), prediction error p95 {predictionError.P95:0.##} ms (max {predictionError.Max:0.##}); animation error = prediction - pacing error."
            );
          AnsiConsole.MarkupLineInterpolated($"Cause: {RunHeadline.Cause(pacing)}");
        }
      }
      AnsiConsole.MarkupLineInterpolated($"[grey]Reports written to {report.OutputDirectory}[/]");
    }

    /// <summary>The refresh rate the run was measured with, where it comes from, and how it compares with the expected rate.</summary>
    public static string RefreshText(RunPacing pacing)
    {
      string text = string.Create(
        CultureInfo.InvariantCulture,
        $"Display refresh {pacing.RefreshHz:0.##} Hz ({(pacing.RefreshCalculated ? "calculated from the camera frames" : "the capture rate")})"
      );
      if (pacing.ExpectedRefreshHz is { } expected && pacing.RefreshDeviation is { } deviation)
        text += string.Create(
          CultureInfo.InvariantCulture,
          $", expected {expected:0.##} Hz: {(pacing.MatchesExpectedRefresh == true ? "matches" : $"differs by {deviation:+0.0%;-0.0%}")}"
        );
      return text + ".";
    }

    private static string TargetReason(RunPacing pacing) =>
      pacing.Source switch
      {
        PacingSource.Schedule => "the pacer's schedule in the markers",
        PacingSource.TargetFrameTime => "the pacer's target frame time in the markers",
        PacingSource.GivenTarget => "the given target frame rate",
        _ => "no pacing information: the display's native refresh rate",
      };

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
