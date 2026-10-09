//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL 'trace-info' (measure/doc/perfetto.md): what a Perfetto trace has to go by, for picking the options --trace takes: its
//* clocks, its vertical blank sources, its processes, their layers in the frame timeline and their threads.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using MB.FramePacing.Trace;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal static class TraceInfoCommand
  {
    /// <summary>How many processes, layers and threads are listed at the most.</summary>
    private const int MaxListed = 60;

    public static Command Create()
    {
      var traceArgument = new Argument<string>("trace") { Description = "A Perfetto trace (.pftrace, .perfetto-trace)." };
      var processOption = new Option<string?>("--trace-process")
      {
        Description = "List only the processes that fit this name (* and ? as in a file name), with their threads.",
        HelpName = "pattern",
      };
      var processorOption = TraceInput.ProcessorOption();
      var command = new Command(
        "trace-info",
        "(experimental) List what a Perfetto trace has to go by: its clocks, vertical blank sources, processes, frame timeline layers and threads."
      )
      {
        traceArgument,
        processOption,
        processorOption,
      };
      command.SetAction(parseResult =>
      {
        try
        {
          TraceInput.PrintNotice();
          string path = Path.GetFullPath(parseResult.GetValue(traceArgument)!);
          string processor = TraceInput.FindProcessor(parseResult, processorOption);
          string? pattern = parseResult.GetValue(processOption);
          TraceInfo info;
          using (var trace = TraceProcessor.Start(processor, path))
            info = TraceReader.Inspect(trace, pattern);
          Print(Path.GetFileName(path), info, pattern);
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

    private static void Print(string name, TraceInfo info, string? pattern)
    {
      double seconds = (info.LastNs - info.FirstNs) / (double)NanosecondTimeSpan.NanosecondsPerSecond;
      AnsiConsole.MarkupLineInterpolated($"[bold]{name}[/]: {info.FirstNs} to {info.LastNs} ns on the trace's clock ({seconds:0.000} s)");

      AnsiConsole.MarkupLine("Clocks the trace has snapshots of (--trace-app-clock):");
      foreach (var clock in info.Clocks)
        AnsiConsole.MarkupLineInterpolated(
          $"  {clock.ClockId, 3} {TraceAppClockUtil.NameOfId(clock.ClockId) ?? clock.Name ?? string.Empty} ({clock.Snapshots} snapshots)"
        );

      AnsiConsole.MarkupLine("Vertical blanks (--trace-vblanks):");
      foreach (var source in info.VBlankSources)
        AnsiConsole.MarkupLineInterpolated($"  {source.Name} ({source.Kind}, {source.Count})");
      if (info.VBlankSources.Count == 0)
        AnsiConsole.MarkupLine("  none");

      AnsiConsole.MarkupLineInterpolated($"Processes that fit '{pattern ?? "*"}' (--trace-process): {info.Processes.Count}");
      // Those the frame timeline has frames of first: the application is one of them
      foreach (var process in info.Processes.OrderByDescending(p => p.TimelineFrames).ThenBy(p => p.Name, StringComparer.Ordinal).Take(MaxListed))
        AnsiConsole.MarkupLineInterpolated(
          $"  {process.Name} (pid {process.Pid}){(process.TimelineFrames > 0 ? $", {process.TimelineFrames} frames in the frame timeline" : string.Empty)}"
        );

      AnsiConsole.MarkupLineInterpolated($"Frame timeline layers of those (--trace-layer): {info.Layers.Count}");
      foreach (var layer in info.Layers.Take(MaxListed))
        AnsiConsole.MarkupLineInterpolated($"  {layer.Process}: '{layer.Layer}' ({layer.Frames} frames)");

      if (pattern != null)
      {
        AnsiConsole.MarkupLine("Threads of those (--trace-thread):");
        foreach (var thread in info.Threads.Take(MaxListed))
          AnsiConsole.MarkupLineInterpolated($"  {thread.Process}: '{thread.Thread}'{(thread.IsMain ? " (main)" : string.Empty)}");
      }

      AnsiConsole.MarkupLineInterpolated($"Errors the trace processor counts: {info.Errors.Values.Sum()}");
      foreach (var (error, count) in info.Errors)
        AnsiConsole.MarkupLineInterpolated($"  {error}: {count}");
    }
  }
}
