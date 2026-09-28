//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* 'render': draw a run of an analysis, or a section of it, as an SVG report (and PNG through a headless Edge or Chrome) from the analysis
//* output (summary.json and the run's frames CSV), without the capture.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using MB.FramePacing.Charts;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal static class RenderCommand
  {
    public static Command Create()
    {
      var folderArgument = new Argument<string>("folder") { Description = "A capture folder that was analysed, or its analysis folder." };
      var runOption = new Option<uint?>("--run") { Description = "Only this run id (default: every run)." };
      var fromOption = new Option<double?>("--from")
      {
        Description = "Start of the section, in seconds since the run's first frame (the Timeline's axis).",
      };
      var toOption = new Option<double?>("--to") { Description = "End of the section, in seconds since the run's first frame." };
      var detailsOption = new Option<bool>("--details")
      {
        Description =
          $"Also render the worst moments: {ReportFiles.DetailSeconds:0} s around the largest animation error, and the worst 2 s of late frames.",
      };
      var pngOption = new Option<bool>("--png")
      {
        Description =
          $"Also save each report as a PNG at twice its size, through a headless Edge or Chrome ({HeadlessBrowser.EnvironmentVariable}, or found).",
      };
      var outputOption = new Option<string?>("--output", "-o") { Description = "Where the reports go (default: the analysis folder)." };

      var command = new Command("render", "Draw an analysed run, or a section of it, as an SVG report (and PNG).")
      {
        folderArgument,
        runOption,
        fromOption,
        toOption,
        detailsOption,
        pngOption,
        outputOption,
      };
      command.SetAction(parseResult =>
      {
        try
        {
          string folder = Path.GetFullPath(parseResult.GetValue(folderArgument)!);
          uint? runId = parseResult.GetValue(runOption);
          var runs = AnalysisOutput.Read(folder).Where(r => runId == null || r.Chart.Run.RunId == runId).ToList();
          if (runs.Count == 0)
            throw new InvalidOperationException(runId != null ? $"The analysis has no run {runId}" : "The analysis has no runs");
          string output = parseResult.GetValue(outputOption) is { } path ? Path.GetFullPath(path) : AnalysisOutput.Directory(folder);
          Directory.CreateDirectory(output);
          foreach (var run in runs)
          {
            var files = ReportFiles.Write(
              run.Chart,
              run.FilePrefix,
              output,
              parseResult.GetValue(fromOption),
              parseResult.GetValue(toOption),
              parseResult.GetValue(detailsOption),
              parseResult.GetValue(pngOption)
            );
            foreach (var file in files)
              AnsiConsole.MarkupLineInterpolated($"[grey]{file}[/]");
          }
          return Program.ResultSuccess;
        }
        catch (Exception ex)
        {
          Program.ReportError(ex);
          return Program.ResultError;
        }
      });
      return command;
    }
  }
}
