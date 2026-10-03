//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* 'locate': find the marker in a capture device's picture and print the region a fast capture stores (--roi/--scale for 'capture'). The
//* same search runs before 'capture --roi auto' and 'import --roi auto'. Nothing is recorded.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Ffmpeg;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal static class LocateCommand
  {
    public static Command Create()
    {
      var deviceOption = new Option<string>("--device", "-d")
      {
        Description = "Capture device (see 'devices'), or 'lavfi:<graph>' for an ffmpeg test source.",
        Required = true,
      };
      var modeOption = new Option<string?>("--mode")
      {
        Description = "Device mode: WIDTHxHEIGHT@FPS, WIDTHxHEIGHT or @FPS (default: device default).",
      };
      var inputFormatOption = new Option<string?>("--input-format") { Description = "Device pixel format or codec: mjpeg, yuyv422, nv12, ..." };
      var timeoutOption = new Option<string?>("--timeout")
      {
        Description =
          $"Give up when no marker was found after this long (default {FfmpegMarkerLocator.DefaultTimeout.TotalSeconds:0}s). A recording "
          + "is read to its end.",
      };
      var ffmpegOption = CommonOptions.Ffmpeg();

      var command = new Command(
        "locate",
        "(EXPERIMENTAL, live capture) Find the marker and print the region a fast capture stores ('capture --roi auto' does this itself)."
      )
      {
        deviceOption,
        modeOption,
        inputFormatOption,
        timeoutOption,
        ffmpegOption,
      };
      command.SetAction(
        async (parseResult, cancellationToken) =>
        {
          try
          {
            CommonOptions.PrintLiveCaptureWarning();
            var config = CommonOptions.LoadConfig(parseResult);
            var modeText = parseResult.GetValue(modeOption);
            var options = new FfmpegCaptureOptions
            {
              FfmpegPath = FfmpegLocator.Find(parseResult.GetValue(ffmpegOption), config),
              Device = CaptureDevice.Parse(parseResult.GetValue(deviceOption)!),
              Mode = modeText != null ? RequestedMode.Parse(modeText) : default,
              InputFormat = parseResult.GetValue(inputFormatOption),
            };
            var timeout = DurationParser.ParseOptional(parseResult.GetValue(timeoutOption)) ?? FfmpegMarkerLocator.DefaultTimeout;
            await Task.Run(() => Locate(options, timeout, cancellationToken), CancellationToken.None);
            return Program.ResultSuccess;
          }
          catch (Exception ex)
          {
            Program.ReportError(ex);
            return Program.ResultError;
          }
        }
      );
      return command;
    }

    /// <summary>Find the markers for a capture that stores only their regions, and report what it found.</summary>
    internal static MarkerLocateResult Locate(FfmpegCaptureOptions options, CancellationToken cancellationToken) =>
      Locate(options, FfmpegMarkerLocator.DefaultTimeout, cancellationToken);

    private static MarkerLocateResult Locate(FfmpegCaptureOptions options, TimeSpan timeout, CancellationToken cancellationToken)
    {
      MarkerLocateResult? result = null;
      AnsiConsole
        .Status()
        .Spinner(Spinner.Known.Dots)
        .Start(
          $"Looking for the marker in {Markup.Escape(options.Device.Name)}...",
          _ => result = FfmpegMarkerLocator.Locate(options, timeout, cancellationToken)
        );
      Print(result!);
      return result!;
    }

    private static void Print(MarkerLocateResult result)
    {
      AnsiConsole.MarkupLineInterpolated($"{result.Summary}");
      AnsiConsole.MarkupLineInterpolated(
        $"[grey]The markers must not move. To store the main marker's region without locating it again: {result.Arguments}[/]"
      );
      if (result.Crop.HasSyncRoi)
        AnsiConsole.MarkupLine("[grey]A region given that way is one rectangle: the sync marker is not stored, so tearing is not checked.[/]");
    }
  }
}
