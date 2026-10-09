//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* 'config': show, create or update mb-framepacing.json (the ffmpeg installation, the capture folder and the playback page's answers).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using System.IO;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Ffmpeg;
using MB.FramePacing.Trace;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal static class ConfigCommand
  {
    public static Command Create()
    {
      var initOption = new Option<bool>("--init") { Description = "Create the configuration file with a commented template if it does not exist." };
      var ffmpegOption = new Option<string?>("--set-ffmpeg") { Description = "Store the path of your ffmpeg executable." };
      var captureOption = new Option<string?>("--set-capture-dir") { Description = "Store the folder that receives new captures." };
      var playbackTranscodeOption = new Option<PlaybackTranscodeChoice?>("--set-playback-transcode")
      {
        Description = "Store what a playback report does with a recording browsers cannot play: ask, yes (make a playable copy) or no (no video).",
        HelpName = "ask|yes|no",
      };
      var traceProcessorOption = CommonOptions.ExperimentalOption(
        new Option<string?>("--set-trace-processor")
        {
          Description = "(experimental) Store the path of Perfetto's trace_processor_shell, which reads a Perfetto trace (--trace).",
        },
        TraceInput.What
      );
      var command = new Command(
        "config",
        "Show or change the configuration file (ffmpeg installation, capture folder, the playback page's answers). Without options it shows the active settings."
      )
      {
        initOption,
        ffmpegOption,
        captureOption,
        playbackTranscodeOption,
        traceProcessorOption,
      };
      command.SetAction(parseResult =>
      {
        try
        {
          var explicitPath = parseResult.GetValue(CommonOptions.Config);
          if (parseResult.GetValue(initOption))
            AnsiConsole.MarkupLineInterpolated($"Configuration file: {FramePacingConfig.EnsureExists(explicitPath)}");

          var setFfmpeg = parseResult.GetValue(ffmpegOption);
          var setCapture = parseResult.GetValue(captureOption);
          var setPlaybackTranscode = parseResult.GetValue(playbackTranscodeOption);
          var setTraceProcessor = parseResult.GetValue(traceProcessorOption);
          if (setFfmpeg != null || setCapture != null || setPlaybackTranscode != null || setTraceProcessor != null)
          {
            var config = FramePacingConfig.Load(File.Exists(FramePacingConfig.ResolvePath(explicitPath)) ? explicitPath : null);
            if (setFfmpeg != null)
            {
              var full = Path.GetFullPath(setFfmpeg);
              if (!File.Exists(full))
                throw new FileNotFoundException($"'{full}' does not exist");
              config = config with { FfmpegPath = full };
            }
            if (setCapture != null)
              config = config with { CaptureDirectory = Path.GetFullPath(setCapture) };
            // "ask" is the default: stored as no value
            if (setPlaybackTranscode != null)
              config = config with { PlaybackTranscode = setPlaybackTranscode == PlaybackTranscodeChoice.Ask ? null : setPlaybackTranscode };
            if (setTraceProcessor != null)
            {
              var full = Path.GetFullPath(setTraceProcessor);
              if (!File.Exists(full))
                throw new FileNotFoundException($"'{full}' does not exist");
              config = config with { TraceProcessorPath = full };
            }
            var written = config.Save(explicitPath);
            AnsiConsole.MarkupLineInterpolated($"[green]Saved[/] {written}");
          }

          Show(explicitPath);
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

    private static void Show(string? explicitPath)
    {
      var path = FramePacingConfig.ResolvePath(explicitPath);
      var config = File.Exists(path) ? FramePacingConfig.Load(explicitPath) : new FramePacingConfig();
      var table = new Table().AddColumn("Setting").AddColumn("Value");
      table.AddRow("Configuration file", Markup.Escape(File.Exists(path) ? path : $"{path} (not created yet, use --init)"));
      table.AddRow("ffmpegPath", Markup.Escape(config.FfmpegPath ?? "(not set)"));
      table.AddRow("captureDirectory", Markup.Escape(config.CaptureDirectory ?? "(not set: Documents/mb-framepacing)"));
      table.AddRow("playbackTranscode", (config.PlaybackTranscode ?? PlaybackTranscodeChoice.Ask).ToString().ToLowerInvariant());
      // Experimental: the trace processor a Perfetto trace is read with, shown with the experimental parts only
      if (CommonOptions.ExperimentalRequested)
      {
        table.AddRow("traceProcessorPath (experimental)", Markup.Escape(config.TraceProcessorPath ?? "(not set)"));
        string traceProcessor;
        try
        {
          traceProcessor = Markup.Escape(TraceProcessorLocator.Find(null, config));
        }
        catch (FileNotFoundException)
        {
          traceProcessor = "[red]not found[/] - download: " + Markup.Escape(TraceProcessorLocator.DownloadPage);
        }
        table.AddRow("trace processor in use (experimental)", traceProcessor);
      }
      string ffmpeg;
      try
      {
        ffmpeg = FfmpegLocator.Find(null, config);
      }
      catch (FileNotFoundException)
      {
        ffmpeg = "[red]not found[/] - download: " + Markup.Escape(FfmpegLocator.DownloadPage);
        table.AddRow("ffmpeg in use", ffmpeg);
        AnsiConsole.Write(table);
        return;
      }
      table.AddRow("ffmpeg in use", Markup.Escape(ffmpeg));
      AnsiConsole.Write(table);
    }
  }
}
