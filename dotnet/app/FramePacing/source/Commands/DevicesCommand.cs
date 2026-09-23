//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* 'devices': list capture devices (and their modes) through ffmpeg.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using MB.FramePacing.Capture.Ffmpeg;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal static class DevicesCommand
  {
    public static Command Create()
    {
      var ffmpegOption = CommonOptions.Ffmpeg();
      var modesOption = new Option<bool>("--modes", "-m") { Description = "Also list each device's modes (DirectShow and v4l2)." };
      var command = new Command("devices", "List the capture devices ffmpeg can see.") { ffmpegOption, modesOption };
      command.SetAction(parseResult =>
      {
        try
        {
          var ffmpeg = FfmpegLocator.Find(parseResult.GetValue(ffmpegOption), CommonOptions.LoadConfig(parseResult));
          AnsiConsole.MarkupLineInterpolated($"[grey]{FfmpegDevices.GetVersion(ffmpeg)}[/]");
          var devices = FfmpegDevices.ListDevices(ffmpeg);
          if (devices.Count == 0)
          {
            AnsiConsole.MarkupLine("[yellow]No video capture devices found.[/]");
            return Program.ResultSuccess;
          }

          var table = new Table().AddColumn("Device (use with --device)").AddColumn("Name");
          foreach (var device in devices)
            table.AddRow(Markup.Escape(device.Input), Markup.Escape(device.Name));
          AnsiConsole.Write(table);

          if (parseResult.GetValue(modesOption))
          {
            foreach (var device in devices)
            {
              var modes = FfmpegDevices.ListModes(ffmpeg, device);
              AnsiConsole.MarkupLineInterpolated($"[bold]{device.Name}[/]: {(modes.Count == 0 ? "no mode list available" : string.Empty)}");
              foreach (var mode in modes)
                AnsiConsole.MarkupLineInterpolated($"  {mode}{(mode.IsCompressed ? " (compressed)" : string.Empty)}");
            }
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
