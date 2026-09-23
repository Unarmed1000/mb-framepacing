//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* 'config': show, create or update mb-framepacing.json (the ffmpeg installation and the capture folder).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using System.IO;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Ffmpeg;
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
      var command = new Command(
        "config",
        "Show or change the configuration file (ffmpeg installation, capture folder). Without options it shows the active settings."
      )
      {
        initOption,
        ffmpegOption,
        captureOption,
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
          if (setFfmpeg != null || setCapture != null)
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
