//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A Perfetto trace of the run on the command line (measure/doc/perfetto.md; import and analyze --trace), and a trace
//* the application wrote itself where the frame record is in a file of its own (--trace-app): the options, the traces they name, and
//* the trace processor that reads them. The join itself is the analysis's, shared with the GUI.
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
using MB.FramePacing.TraceJoin;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal sealed class TraceInput
  {
    /// <summary>What a command that reads a trace prints first.</summary>
    public const string Notice = "A trace names the device and its programs: keep what is written with it as private as the recording.";

    public Option<string?> Trace { get; } =
      new Option<string?>("--trace")
      {
        Description =
          "A Perfetto trace of the run (.pftrace, .perfetto-trace): the analysis says what the trace says of each "
          + "presented frame (run-<id>-trace.csv), and --charts also writes the trace with the capture's measurements added as tracks "
          + "(run-<id>-augmented.pftrace). The trace needs the frame record the application logs (a slice per frame named "
          + $"'{FrameRecordText.NamePrefix}<run id> <frame index>', sdk/doc/trace-frame-record.md): without it nothing is joined.",
        HelpName = "file",
      };

    public Option<string?> AppTrace { get; } =
      new Option<string?>("--trace-app")
      {
        Description =
          "A Perfetto trace the application wrote itself, for when its frame record is in a file of its own. With "
          + "--trace the join is placed by it; alone nothing is joined, and what the tools decoded from the markers is held against "
          + "it (run-<id>-trace-check.txt).",
        HelpName = "file",
      };

    public Option<string?> Process { get; } =
      new Option<string?>("--trace-process")
      {
        Description =
          "With --trace: the application's process name in the trace (* and ? as in a file name), for a frame record that is on no "
          + "process's track (default: the process the frame record is on; 'trace-info' lists the processes).",
        HelpName = "pattern",
      };

    public Option<string?> Layer { get; } =
      new Option<string?>("--trace-layer")
      {
        Description = "With --trace: the application's layer in the trace's frame timeline, when it has several (default: every layer).",
        HelpName = "pattern",
      };

    public Option<string?> Thread { get; } =
      new Option<string?>("--trace-thread")
      {
        Description = "With --trace: the thread whose CPU time is counted (default: the process's main thread).",
        HelpName = "name",
      };

    public Option<string?> VBlanks { get; } =
      new Option<string?>("--trace-vblanks")
      {
        Description =
          "With --trace: which of the trace's vertical blank sources to use, or 'none' (default: the kernel's vertical blank events, when "
          + "the trace has them; 'trace-info' lists the sources).",
        HelpName = "name|none",
      };

    public Option<long?> CaptureOffset { get; } =
      new Option<long?>("--trace-capture-offset-ns")
      {
        Description =
          "With --trace: the capture's clock minus the application's, in nanoseconds, when you know it: nothing is worked out from the "
          + "frames then.",
        HelpName = "ns",
      };

    public Option<string?> Processor { get; } = ProcessorOption();

    /// <summary>analyze only: join no trace, also when the capture names one.</summary>
    public Option<bool> NoTrace { get; } =
      new Option<bool>("--no-trace") { Description = "Read no Perfetto trace, also when capture.json names one." };

    /// <summary>--trace-processor: the user's trace_processor_shell.</summary>
    public static Option<string?> ProcessorOption() =>
      new Option<string?>("--trace-processor")
      {
        Description =
          $"Path to Perfetto's trace_processor_shell, which reads a trace (default: ${TraceProcessorLocator.EnvironmentVariable}, "
          + "then traceProcessorPath in the configuration file, then PATH, then the copy Perfetto's Python package downloaded). Download: "
          + TraceProcessorLocator.DownloadPage,
        HelpName = "path",
      };

    /// <summary>Add the options to <paramref name="command"/>: the trace's own only with --trace.</summary>
    /// <param name="remembered">The command analyses a capture that may name a trace itself (analyze): --no-trace is offered.</param>
    public void AddTo(Command command, bool remembered)
    {
      var details = new Option[] { Process, Layer, Thread, VBlanks, CaptureOffset };
      command.Options.Add(Trace);
      command.Options.Add(AppTrace);
      foreach (var option in details)
        command.Options.Add(option);
      command.Options.Add(Processor);
      if (remembered)
        command.Options.Add(NoTrace);
      command.Validators.Add(result =>
      {
        bool trace = result.GetResult(Trace) is { Implicit: false };
        bool appTrace = result.GetResult(AppTrace) is { Implicit: false };
        if (!trace)
        {
          foreach (var option in details.Where(option => result.GetResult(option) is { Implicit: false }))
            result.AddError($"{option.Name} goes with --trace.");
        }
        if ((trace || appTrace) && remembered && result.GetValue(NoTrace))
        {
          result.AddError($"--no-trace does not go with {(trace ? "--trace" : "--trace-app")}.");
        }
      });
    }

    /// <summary>
    /// The traces the options name; null without --trace and --trace-app. A file that is not there is a
    /// <see cref="FileNotFoundException"/>. Prints the notice.
    /// </summary>
    /// <param name="remembered">
    /// The trace the capture itself names: --trace-app alone adds the application's trace to it; --trace replaces it whole.
    /// </param>
    public TraceJoinOptions? Options(ParseResult parseResult, TraceJoinOptions? remembered = null)
    {
      string? trace = parseResult.GetValue(Trace);
      string? appTrace = parseResult.GetValue(AppTrace);
      if (trace == null && appTrace == null)
        return null;
      string? path = trace != null ? ExistingFile(trace, "The Perfetto trace") : null;
      string? appPath = appTrace != null ? ExistingFile(appTrace, "The application's trace") : null;
      PrintNotice();
      if (path == null)
        return (remembered ?? new TraceJoinOptions()) with { AppTracePath = appPath };
      return new TraceJoinOptions
      {
        Path = path,
        AppTracePath = appPath,
        Process = parseResult.GetValue(Process),
        Layer = parseResult.GetValue(Layer),
        Thread = parseResult.GetValue(Thread),
        VBlanks = parseResult.GetValue(VBlanks),
        CaptureOffsetNs = parseResult.GetValue(CaptureOffset),
      };
    }

    private static string ExistingFile(string given, string what)
    {
      string path = Path.GetFullPath(given);
      return File.Exists(path) ? path : throw new FileNotFoundException($"{what} does not exist: '{path}'");
    }

    /// <summary>
    /// <paramref name="options"/> with the trace of this command line and the trace processor a join needs. A trace the command line
    /// names needs a trace processor: none is a <see cref="FileNotFoundException"/> that says where to get one. For a trace the capture
    /// itself names, none leaves the options without one, and the analysis warns.
    /// </summary>
    public AnalysisOptions Apply(ParseResult parseResult, string captureDirectory, AnalysisOptions options)
    {
      var remembered = CaptureSessionInfo.TryLoad(captureDirectory)?.Trace;
      options = options with { Trace = Options(parseResult, remembered), NoTrace = parseResult.GetValue(NoTrace) };
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

    public static void PrintNotice() => AnsiConsole.MarkupLineInterpolated($"[yellow]Note:[/] {Notice}");

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
