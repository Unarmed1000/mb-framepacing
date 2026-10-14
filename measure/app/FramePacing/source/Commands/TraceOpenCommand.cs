//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL 'trace-open' (measure/doc/perfetto.md): a run's augmented trace opened in Perfetto's viewer at ui.perfetto.dev. The trace
//* is served once from 127.0.0.1:9001, the one place on this machine the viewer loads a trace from; the viewer runs in the browser and
//* does not upload it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MB.FramePacing.Data;
using MB.FramePacing.TraceJoin;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal static class TraceOpenCommand
  {
    /// <summary>How long the browser has to ask for the trace.</summary>
    private static readonly TimeSpan g_timeout = TimeSpan.FromMinutes(2);

    public static Command Create()
    {
      var pathArgument = new Argument<string>("path")
      {
        Description =
          "A capture folder or its analysis folder (its run's augmented trace, written by 'analyze --trace ... --charts'), or an augmented trace.",
      };
      var runOption = new Option<uint?>("--run")
      {
        Description = "The run whose augmented trace to open, where the analysis has several.",
        HelpName = "id",
      };
      var noBrowserOption = new Option<bool>("--no-browser") { Description = "Print the link instead of opening it in the browser." };
      var processorOption = TraceInput.ProcessorOption();
      var command = new Command(
        "trace-open",
        "(experimental) Open a run's augmented trace in Perfetto's viewer at ui.perfetto.dev, at the run's stretch of time with the capture's "
          + "tracks pinned. The trace is served once from 127.0.0.1:9001; the viewer runs in the browser and does not upload it."
      )
      {
        pathArgument,
        runOption,
        noBrowserOption,
        processorOption,
      };
      command.SetAction(parseResult =>
      {
        try
        {
          TraceInput.PrintNotice();
          string path = AugmentedTrace(Path.GetFullPath(parseResult.GetValue(pathArgument)!), parseResult.GetValue(runOption));
          var visible = VisibleRange(path, parseResult, processorOption);
          using var server = PerfettoTraceServer.Start(path);
          string link = PerfettoUi.Link(server.TraceUrl, visible);
          AnsiConsole.MarkupLineInterpolated($"Serving {Path.GetFileName(path)} once from {server.TraceUrl} for ui.perfetto.dev:");
          // One line as it is, never wrapped: it is copied as a whole
          Console.Out.WriteLine(link);
          if (!parseResult.GetValue(noBrowserOption))
            OpenInBrowser(link);
          if (!server.ServeAsync(g_timeout).GetAwaiter().GetResult())
          {
            AnsiConsole.MarkupLineInterpolated(
              $"[yellow]warning:[/] nothing asked for the trace within {g_timeout.TotalMinutes:0} minutes: the link no longer works. Run the command again."
            );
            return Program.ResultError;
          }
          AnsiConsole.MarkupLine("Sent: the trace is in the viewer, and the link no longer works.");
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

    /// <summary>The augmented trace <paramref name="path"/> names: the file itself, or the one of a capture's analysis (of the run asked for).</summary>
    private static string AugmentedTrace(string path, uint? run)
    {
      if (File.Exists(path))
        return path;
      string analysis =
        (Directory.Exists(path) ? AnalysisFiles.Find(path) : null)
        ?? throw new TraceException($"{path} is neither a trace nor a capture folder with an analysis.");
      var traces = Directory.GetFiles(analysis, "*" + TraceAugmenter.FileSuffix).OrderBy(file => file, StringComparer.Ordinal).ToList();
      if (run is { } id)
      {
        string wanted = Path.Combine(analysis, TraceAugmenter.FileName(AnalysisFiles.RunFilePrefix(id, 0)));
        return traces.Contains(wanted) ? wanted : throw new TraceException($"The analysis has no augmented trace of run {id}.");
      }
      return traces.Count switch
      {
        1 => traces[0],
        0 => throw new TraceException(
          $"The analysis in {analysis} has no augmented trace: 'analyze --experimental --trace <trace> --charts' writes it with the charts."
        ),
        _ => throw new TraceException(
          $"The analysis has {traces.Count} augmented traces ({string.Join(", ", traces.Select(Path.GetFileName))}): say which run with --run."
        ),
      };
    }

    /// <summary>Where the capture's tracks are, read with the trace processor; null without one (the viewer then shows the whole trace).</summary>
    private static (long FirstNs, long LastNs)? VisibleRange(string path, ParseResult parseResult, Option<string?> processorOption)
    {
      string processor;
      try
      {
        processor = TraceInput.FindProcessor(parseResult, processorOption);
      }
      catch (TraceException ex)
      {
        AnsiConsole.MarkupLineInterpolated($"The whole trace is shown: {ex.Message}");
        return null;
      }
      return PerfettoUi.AddedRangeOf(path, processor);
    }

    /// <summary>The link handed to the desktop's browser.</summary>
    private static void OpenInBrowser(string link)
    {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      {
        Process.Start(new ProcessStartInfo(link) { UseShellExecute = true })?.Dispose();
        return;
      }
      var startInfo = new ProcessStartInfo(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open") { UseShellExecute = false };
      startInfo.ArgumentList.Add(link);
      Process.Start(startInfo)?.Dispose();
    }
  }
}
