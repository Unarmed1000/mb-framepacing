//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* 'render': draw a run of an analysis, or a section of it, as an SVG report and distribution cards (and PNG through a headless Edge or
//* Chrome) from the analysis output (summary.json and the run's frames CSV), without the capture.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
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
      var timelineOption = new Option<bool>("--timeline")
      {
        Description =
          $"Draw the frame timeline of --from to --to instead (default --to: {ReportFiles.TimelineSeconds} s later): every frame's CPU start and CPU busy, its present, what each refresh showed, at most {FrameTimelineCard.MaxFrames} frames.",
      };
      var nameOption = new Option<string?>("--name") { Description = "A name for the runs in these reports, instead of the analysis's." };
      var outputOption = new Option<string?>("--output", "-o") { Description = "Where the reports go (default: the analysis folder)." };
      string items = string.Join(", ", ReportItem.All.Select(i => i.Id));
      var hideOption = new Option<string?>("--hide") { Description = $"Leave these items out of the card, comma separated: {items}." };
      var onlyOption = new Option<string?>("--only")
      {
        Description =
          "Show only these items (comma separated, the same ids as --hide; naming a tile keeps the tiles row for it, naming an overlay its panel).",
      };
      string overlays = string.Join(", ", ReportItem.Overlays.Keys);
      string autoTiles = string.Join(", ", ReportItem.AutoTiles);
      var showOption = new Option<string?>("--show")
      {
        Description =
          $"Also draw these opt-in items, comma separated: the overlays {overlays} (on the display time step panel), and the tiles "
          + $"{autoTiles}, which are otherwise left out when the run has no dropped or out-of-order frames.",
      };
      var titleOption = new Option<string?>("--title")
      {
        Description = "The report's title instead of 'Run N  name' (every run gets it; a section's time range is still added).",
      };
      var stripSecondsOption = new Option<double?>("--strip-seconds")
      {
        Description = "The refresh strip shows only the first this many seconds of the section, with its own time axis (e.g. 1).",
      };
      var hideEmptyOption = new Option<bool>("--hide-empty")
      {
        Description = "Leave out the tiles without a value (too few frames, no pacing) and the late share when no frame is late.",
      };
      var tilesPerRowOption = new Option<int?>("--tiles-per-row")
      {
        Description =
          $"How many headline tiles go in a row (1 to {ReportOptions.MaxTilesPerRow}). Default: two rows, half the tiles each (at least "
          + $"{ReportOptions.MinAutoTilesPerRow}).",
      };
      string cardIds = string.Join(", ", DistributionCard.All.Select(c => c.Id));
      var cardsOption = new Option<string>("--cards")
      {
        Description = $"The distribution cards to draw next to the report, comma separated: {cardIds}; 'all' (the default) or 'none'.",
        DefaultValueFactory = _ => "all",
      };

      var command = new Command("render", "Draw an analysed run, or a section of it, as an SVG report and distribution cards (and PNG).")
      {
        folderArgument,
        runOption,
        fromOption,
        toOption,
        detailsOption,
        pngOption,
        timelineOption,
        nameOption,
        outputOption,
        hideOption,
        onlyOption,
        showOption,
        titleOption,
        stripSecondsOption,
        hideEmptyOption,
        tilesPerRowOption,
        cardsOption,
      };
      command.SetAction(parseResult =>
      {
        try
        {
          string folder = Path.GetFullPath(parseResult.GetValue(folderArgument)!);
          uint? runId = parseResult.GetValue(runOption);
          string? name = parseResult.GetValue(nameOption);
          var runs = AnalysisOutput
            .Read(folder)
            .Where(r => runId == null || r.Chart.Run.RunId == runId)
            .Select(r => name == null ? r : r with { Chart = r.Chart with { Run = r.Chart.Run with { Name = name } } })
            .ToList();
          if (runs.Count == 0)
            throw new InvalidOperationException(runId != null ? $"The analysis has no run {runId}" : "The analysis has no runs");
          string output = parseResult.GetValue(outputOption) is { } path ? Path.GetFullPath(path) : AnalysisOutput.Directory(folder);
          var options = parseResult.GetValue(onlyOption) is { } only ? ReportOptions.ShowOnly(ReportOptions.ParseIds(only)) : ReportOptions.Default;
          if (parseResult.GetValue(showOption) is { } show)
            options = options.Show(ReportOptions.ParseIds(show));
          if (parseResult.GetValue(hideOption) is { } hide)
            options = options.Hide(ReportOptions.ParseIds(hide));
          options = options with
          {
            Title = parseResult.GetValue(titleOption),
            StripSeconds = parseResult.GetValue(stripSecondsOption),
            HideEmpty = parseResult.GetValue(hideEmptyOption),
            TilesPerRow = parseResult.GetValue(tilesPerRowOption),
          };
          var cards = ParseCards(parseResult.GetValue(cardsOption)!);
          Directory.CreateDirectory(output);
          foreach (var run in runs)
          {
            if (parseResult.GetValue(timelineOption))
            {
              double from =
                parseResult.GetValue(fromOption)
                ?? throw new InvalidOperationException("--timeline needs --from (seconds since the run's first frame)");
              double to = parseResult.GetValue(toOption) ?? from + ReportFiles.TimelineSeconds;
              foreach (var file in ReportFiles.WriteTimeline(run.Chart, run.FilePrefix, output, from, to, parseResult.GetValue(pngOption)))
                AnsiConsole.MarkupLineInterpolated($"[grey]{file}[/]");
              continue;
            }
            var files = ReportFiles.Write(
              run.Chart,
              run.FilePrefix,
              output,
              parseResult.GetValue(fromOption),
              parseResult.GetValue(toOption),
              parseResult.GetValue(detailsOption),
              parseResult.GetValue(pngOption),
              options
            );
            files = files
              .Concat(
                ReportFiles.WriteCards(
                  run.Chart,
                  run.FilePrefix,
                  output,
                  cards,
                  parseResult.GetValue(fromOption),
                  parseResult.GetValue(toOption),
                  parseResult.GetValue(pngOption)
                )
              )
              .ToList();
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

    /// <summary>The --cards ids: 'all', 'none', or known card ids.</summary>
    private static IReadOnlyList<string> ParseCards(string text)
    {
      var ids = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      if (ids is ["all"])
        return DistributionCard.All.Select(c => c.Id).ToList();
      if (ids is ["none"])
        return Array.Empty<string>();
      foreach (string id in ids)
      {
        if (!DistributionCard.IsKnown(id))
          throw new InvalidOperationException(
            $"Unknown card '{id}'. Known: {string.Join(", ", DistributionCard.All.Select(c => c.Id))}, or 'all' or 'none'."
          );
      }
      return ids;
    }
  }
}
