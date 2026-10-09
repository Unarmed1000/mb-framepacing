//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL (measure/doc/perfetto.md): a Perfetto trace of the run on the command line (import, capture and analyze --trace): the
//* options, the trace they name, and the trace processor that reads it. The join itself is the analysis's, shared with the GUI.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using MB.FramePacing.Analysis;
using MB.FramePacing.Capture;
using MB.FramePacing.Trace;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal sealed class TraceInput
  {
    public const string What = "A capture joined with a Perfetto trace (--trace)";

    /// <summary>What a command that joins a trace prints first.</summary>
    public const string Notice =
      "A capture joined with a Perfetto trace is EXPERIMENTAL: it is checked against traces its own tests make, and no recorded trace has "
      + "been through it yet (measure/doc/perfetto.md). A trace names the device and its programs: keep what is written as private as the "
      + "recording.";

    public Option<string?> Trace { get; } =
      Experimental(
        new Option<string?>("--trace")
        {
          Description =
            "(experimental) A Perfetto trace recorded on the device during the run (.pftrace, .perfetto-trace): the analysis says what "
            + "the trace says of each presented frame (run-<id>-trace.csv), and --charts also writes the trace with the capture's "
            + "measurements added as tracks (run-<id>-augmented.pftrace). Needs markers with the CPU start time.",
          HelpName = "file",
        }
      );

    public Option<string?> Process { get; } =
      Experimental(
        new Option<string?>("--trace-process")
        {
          Description =
            "With --trace: the application's process name in the trace (* and ? as in a file name). The system's own account of the "
            + "frames and the CPU's time need it ('trace-info' lists the processes).",
          HelpName = "pattern",
        }
      );

    public Option<string?> Layer { get; } =
      Experimental(
        new Option<string?>("--trace-layer")
        {
          Description = "With --trace: the application's layer in the trace's frame timeline, when it has several (default: every layer).",
          HelpName = "pattern",
        }
      );

    public Option<string?> Thread { get; } =
      Experimental(
        new Option<string?>("--trace-thread")
        {
          Description = "With --trace: the thread whose CPU time is counted (default: the process's main thread).",
          HelpName = "name",
        }
      );

    public Option<string?> VBlanks { get; } =
      Experimental(
        new Option<string?>("--trace-vblanks")
        {
          Description =
            "With --trace: which of the trace's vertical blank sources to use, or 'none' (default: the kernel's vertical blank events, when "
            + "the trace has them; 'trace-info' lists the sources).",
          HelpName = "name|none",
        }
      );

    public Option<string?> AppClock { get; } =
      Experimental(
        new Option<string?>("--trace-app-clock")
        {
          Description =
            $"With --trace: the system clock the application's steady clock is: {string.Join(", ", TraceAppClockUtil.Names)} (default: "
            + $"{TraceAppClockUtil.Names[0]}).",
          HelpName = "clock",
          Validators =
          {
            result =>
            {
              if (result.GetValueOrDefault<string?>() is { } name && !TraceAppClockUtil.TryParse(name, out _))
                result.AddError($"--trace-app-clock is one of {string.Join(", ", TraceAppClockUtil.Names)}.");
            },
          },
        }
      );

    public Option<long?> AppClockOffset { get; } =
      Experimental(
        new Option<long?>("--trace-app-clock-offset-ns")
        {
          Description =
            "With --trace: what to add to a marker's time to get that clock's, in nanoseconds, for an application whose clock counts from "
            + "an epoch of its own.",
          HelpName = "ns",
        }
      );

    public Option<long?> CaptureOffset { get; } =
      Experimental(
        new Option<long?>("--trace-capture-offset-ns")
        {
          Description =
            "With --trace: the capture's clock minus the application's, in nanoseconds, when you know it: nothing is worked out from the "
            + "frames then.",
          HelpName = "ns",
        }
      );

    public Option<string?> Processor { get; } = ProcessorOption();

    /// <summary>analyze only: join no trace, also when the capture names one.</summary>
    public Option<bool> NoTrace { get; } =
      Experimental(new Option<bool>("--no-trace") { Description = "(experimental) Join no Perfetto trace, also when capture.json names one." });

    /// <summary>--trace-processor: the user's trace_processor_shell.</summary>
    public static Option<string?> ProcessorOption() =>
      Experimental(
        new Option<string?>("--trace-processor")
        {
          Description =
            $"(experimental) Path to Perfetto's trace_processor_shell, which reads a trace (default: ${TraceProcessorLocator.EnvironmentVariable}, "
            + "then traceProcessorPath in the configuration file, then PATH, then the copy Perfetto's Python package downloaded). Download: "
            + TraceProcessorLocator.DownloadPage,
          HelpName = "path",
        }
      );

    private static T Experimental<T>(T option)
      where T : Option => CommonOptions.ExperimentalOption(option, What);

    /// <summary>Add the options to <paramref name="command"/>: the trace's own only with --trace.</summary>
    /// <param name="remembered">The command analyses a capture that may name a trace itself (analyze): --no-trace is offered.</param>
    public void AddTo(Command command, bool remembered)
    {
      var details = new Option[] { Process, Layer, Thread, VBlanks, AppClock, AppClockOffset, CaptureOffset };
      command.Options.Add(Trace);
      foreach (var option in details)
        command.Options.Add(option);
      command.Options.Add(Processor);
      if (remembered)
        command.Options.Add(NoTrace);
      command.Validators.Add(result =>
      {
        if (result.GetResult(Trace) is not { Implicit: false })
        {
          foreach (var option in details.Where(option => result.GetResult(option) is { Implicit: false }))
            result.AddError($"{option.Name} goes with --trace.");
        }
        else if (remembered && result.GetValue(NoTrace))
        {
          result.AddError("--no-trace does not go with --trace.");
        }
      });
    }

    /// <summary>
    /// The trace the options name; null without --trace. A file that is not there is a <see cref="FileNotFoundException"/>. Prints the
    /// experimental notice.
    /// </summary>
    public TraceJoinOptions? Options(ParseResult parseResult)
    {
      if (parseResult.GetValue(Trace) is not { } trace)
        return null;
      string path = Path.GetFullPath(trace);
      if (!File.Exists(path))
        throw new FileNotFoundException($"The Perfetto trace does not exist: '{path}'");
      PrintNotice();
      return new TraceJoinOptions
      {
        Path = path,
        Process = parseResult.GetValue(Process),
        Layer = parseResult.GetValue(Layer),
        Thread = parseResult.GetValue(Thread),
        VBlanks = parseResult.GetValue(VBlanks),
        AppClock = parseResult.GetValue(AppClock),
        AppClockOffsetNs = parseResult.GetValue(AppClockOffset),
        CaptureOffsetNs = parseResult.GetValue(CaptureOffset),
      };
    }

    /// <summary>
    /// <paramref name="options"/> with the trace of this command line and the trace processor a join needs. A trace the command line
    /// names needs a trace processor: none is a <see cref="FileNotFoundException"/> that says where to get one. For a trace the capture
    /// itself names, none leaves the options without one, and the analysis warns.
    /// </summary>
    public AnalysisOptions Apply(ParseResult parseResult, string captureDirectory, AnalysisOptions options)
    {
      options = options with { Trace = Options(parseResult), NoTrace = parseResult.GetValue(NoTrace) };
      return WithProcessor(parseResult, captureDirectory, options);
    }

    /// <summary>The trace processor for an analysis of <paramref name="captureDirectory"/> that joins a trace; the options as they are without one.</summary>
    public AnalysisOptions WithProcessor(ParseResult parseResult, string captureDirectory, AnalysisOptions options)
    {
      if (options.Trace != null)
        return options with { TraceProcessorPath = FindProcessor(parseResult, Processor) };
      if (CaptureAnalyzer.TraceOf(captureDirectory, options) == null)
        return options;
      try
      {
        return options with { TraceProcessorPath = FindProcessor(parseResult, Processor) };
      }
      catch (FileNotFoundException ex)
      {
        AnsiConsole.MarkupLineInterpolated($"[yellow]warning:[/] {ex.Message}");
        return options;
      }
    }

    public static void PrintNotice() => AnsiConsole.MarkupLineInterpolated($"[yellow]WARNING:[/] {Notice}");

    /// <summary>The trace processor: --trace-processor, else as the configuration finds it.</summary>
    public static string FindProcessor(ParseResult parseResult, Option<string?> processorOption) =>
      TraceProcessorLocator.Find(parseResult.GetValue(processorOption), CommonOptions.LoadConfig(parseResult));

    /// <summary>
    /// Write the augmented trace of each run of <paramref name="report"/> that was joined (with the charts), read it back, and print where
    /// it is and how it read back.
    /// </summary>
    public static void WriteAugmented(AnalysisReport report, string? processorPath)
    {
      foreach (var file in AugmentedTraceFiles.Write(report, processorPath))
      {
        AnsiConsole.MarkupLineInterpolated(
          $"Augmented trace ({file.Events} events of the capture added; it names the device and its programs, as the trace does):"
        );
        AnsiConsole.MarkupLineInterpolated($"  [link]{file.Path}[/]");
        foreach (var problem in file.Problems)
          AnsiConsole.MarkupLineInterpolated($"[yellow]warning:[/] the augmented trace does not read back as written: {problem}");
        if (file.Checked && file.Problems.Count == 0)
          AnsiConsole.MarkupLineInterpolated($"[grey]  read back: every track has what was written, and no new error is counted[/]");
      }
    }
  }
}
