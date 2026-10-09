//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL 'frame-stages': draw the stages of each frame of a run of the frame pacer's first integration from its frame log (the
//* sample's own CSV, alone or inside a capture session's zip), as a row per frame or a lane per resource, and write the run as a trace
//* file. Display times in such a log are what the graphics driver reported, not a measurement by the tools.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using System.IO;
using System.Text;
using MB.FramePacing.Charts;
using MB.FramePacing.Charts.Stages;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal static class FrameStagesCommand
  {
    public static Command Create()
    {
      var sourceArgument = new Argument<string>("source") { Description = "A frame log (CSV), or a capture session's zip." };
      var runOption = new Option<string?>("--run") { Description = "The run in a zip, as <folder>/<name>.", HelpName = "folder/name" };
      var listOption = new Option<bool>("--list") { Description = "List the runs of a zip." };
      var fromOption = new Option<long>("--from") { Description = "The first frame of the chart.", DefaultValueFactory = _ => 120 };
      var framesOption = new Option<int>("--frames")
      {
        Description = $"How many frames the chart shows (at most {FrameStagesCard.MaxFrames}).",
        DefaultValueFactory = _ => 12,
      };
      var viewOption = new Option<string>("--view")
      {
        Description = "rows: a row per frame; lanes: a lane per resource and a colour per frame.",
        DefaultValueFactory = _ => "rows",
      };
      viewOption.AcceptOnlyFromAmong("rows", "lanes");
      var outputOption = new Option<string?>("--output", "-o") { Description = "Write the chart (SVG) here." };
      var pngOption = new Option<bool>("--png") { Description = "Also write the chart as PNG next to the SVG (needs Edge or Chrome)." };
      var traceOption = new Option<string?>("--trace")
      {
        Description = "Write the whole run as a trace file (Chrome trace event JSON, which Perfetto's trace viewer opens) here.",
        HelpName = "file",
      };
      var command = new Command(
        "frame-stages",
        "(experimental) Draw the stages of each frame of a frame log of the frame pacer's first integration, or write the run as a trace file."
      )
      {
        sourceArgument,
        runOption,
        listOption,
        fromOption,
        framesOption,
        viewOption,
        outputOption,
        pngOption,
        traceOption,
      };
      command.Validators.Add(result =>
      {
        if (result.GetValue(listOption))
          return;
        if (result.GetValue(outputOption) == null && result.GetValue(traceOption) == null)
          result.AddError("Nothing to write: give -o for the chart, --trace for the trace file, or both.");
        if (result.GetValue(outputOption) != null && result.GetValue(framesOption) is < 1 or > FrameStagesCard.MaxFrames)
          result.AddError(
            $"The chart shows 1 to {FrameStagesCard.MaxFrames} frames: choose a stretch with --from, or write the whole run with --trace."
          );
        if (result.GetValue(pngOption) && result.GetValue(outputOption) == null)
          result.AddError("--png goes with -o.");
      });
      command.SetAction(parseResult =>
      {
        try
        {
          string source = Path.GetFullPath(parseResult.GetValue(sourceArgument)!);
          if (parseResult.GetValue(listOption))
          {
            foreach (string run in FrameLog.RunsIn(source))
              Console.WriteLine(run);
            return Program.ResultSuccess;
          }
          var (name, frames, notes) = FrameLog.ReadSource(source, parseResult.GetValue(runOption) ?? string.Empty);
          foreach (string note in notes)
            AnsiConsole.MarkupLineInterpolated($"{note}");
          if (frames.Count == 0)
            throw new InvalidDataException($"{Path.GetFileName(source)}: no frames with a start time");
          var encoding = new UTF8Encoding(false);
          if (parseResult.GetValue(outputOption) is { } output)
          {
            long first = parseResult.GetValue(fromOption);
            int count = parseResult.GetValue(framesOption);
            string svg =
              parseResult.GetValue(viewOption) == "lanes"
                ? FrameLanesCard.Render(name, frames, first, count, notes)
                : FrameStagesCard.Render(name, frames, first, count, notes);
            string path = Path.GetFullPath(output);
            File.WriteAllText(path, svg, encoding);
            AnsiConsole.MarkupLineInterpolated($"[grey]{path}[/]: {Math.Min(count, frames.Count)} frames of {name}");
            if (parseResult.GetValue(pngOption))
            {
              string png = Path.ChangeExtension(path, ".png");
              HeadlessBrowser.SavePng(path, png);
              AnsiConsole.MarkupLineInterpolated($"[grey]{png}[/]");
            }
          }
          if (parseResult.GetValue(traceOption) is { } trace)
          {
            string path = Path.GetFullPath(trace);
            File.WriteAllText(path, FrameStagesTrace.Write(name, frames), encoding);
            AnsiConsole.MarkupLineInterpolated($"[grey]{path}[/]: {frames.Count} frames of {name}");
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
