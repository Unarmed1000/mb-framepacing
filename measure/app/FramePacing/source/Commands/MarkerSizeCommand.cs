//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* 'marker-size': the marker module size and position an application should use for a capture setup (doc/marker-format.md "Sizing"),
//* with the settings for the C++, C# and Unity libraries.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using System.Globalization;
using MB.FramePacing.Capture.Ffmpeg;
using MB.FramePacing.Marker;
using Spectre.Console;

namespace MB.FramePacing.App.Commands
{
  internal static class MarkerSizeCommand
  {
    public static Command Create()
    {
      var sourceOption = new Option<string>("--source")
      {
        Description = "The application's output resolution WIDTHxHEIGHT, e.g. 3840x2160.",
        Required = true,
      };
      var storedOption = new Option<string?>("--stored")
      {
        Description = "The stored capture size WIDTHxHEIGHT: the capture mode, or --scale if you downscale (default: the source size).",
      };
      var mjpegOption = new Option<bool>("--mjpeg")
      {
        Description = "The capture card delivers MJPEG (needs 4 instead of 3 stored pixels per module).",
      };
      var command = new Command("marker-size", "Show the marker module size and position an application should use for a capture setup.")
      {
        sourceOption,
        storedOption,
        mjpegOption,
      };
      command.SetAction(parseResult =>
      {
        try
        {
          var (sourceWidth, sourceHeight) = RequestedMode.ParseSize(parseResult.GetValue(sourceOption)!, "--source");
          string? storedText = parseResult.GetValue(storedOption);
          var (storedWidth, storedHeight) = storedText != null ? RequestedMode.ParseSize(storedText, "--stored") : (sourceWidth, sourceHeight);
          bool mjpeg = parseResult.GetValue(mjpegOption);
          Print(MarkerSizing.Advise(sourceWidth, sourceHeight, storedHeight, mjpeg), storedWidth);
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

    private static void Print(MarkerSizingAdvice advice, int storedWidth)
    {
      var table = new Table().AddColumn("Marker").AddColumn("Value");
      table.AddRow("Application output", $"{advice.SourceWidth}x{advice.SourceHeight}");
      table.AddRow("Stored capture", $"{storedWidth}x{advice.StoredHeight}{(advice.Mjpeg ? " (MJPEG)" : string.Empty)}");
      table.AddRow(
        "[bold]Module size[/]",
        Markup.Escape(
          string.Create(CultureInfo.InvariantCulture, $"{advice.RecommendedModulePx} px ({advice.StoredPxPerModule:0.##} stored px per module)")
        )
      );
      table.AddRow("Minimum module size", $"{advice.MinimumModulePx} px (2 stored px per module)");
      table.AddRow("Frame / end marker", $"{advice.FrameMarkerPx}x{advice.FrameMarkerPx} px");
      table.AddRow("Largest start marker", $"{advice.MaxStartMarkerPx}x{advice.MaxStartMarkerPx} px (keep this area free)");
      table.AddRow("Top-left origin", $"{advice.OriginX}, {advice.OriginY}");
      AnsiConsole.Write(table);

      if (advice.StoredHeight > advice.SourceHeight)
        AnsiConsole.MarkupLine("[yellow]The stored height is larger than the output: the capture upscales, which adds nothing.[/]");
      if (advice.NonIntegerRatio)
        AnsiConsole.MarkupLine("[yellow]The output height is not an integer multiple of the stored height: it works, but module edges blur.[/]");

      AnsiConsole.MarkupLine("[bold]Settings[/]");
      AnsiConsole.MarkupLineInterpolated($"  C++    MB::FrameMarker::Options options{{{advice.RecommendedModulePx}}};");
      AnsiConsole.MarkupLineInterpolated(
        $"         or RecommendModuleSizePx({advice.SourceHeight}, {advice.StoredHeight}{(advice.Mjpeg ? ", true" : string.Empty)})"
      );
      AnsiConsole.MarkupLineInterpolated($"  C#     new Options({advice.RecommendedModulePx}), or Marker.RecommendModuleSizePx(...)");
      AnsiConsole.MarkupLineInterpolated(
        $"  Unity  FrameMarkerOverlay: Stored Height {advice.StoredHeight}{(advice.Mjpeg ? ", MJPEG on" : string.Empty)} (or Module Size Px {advice.RecommendedModulePx})"
      );
      if (storedWidth != advice.SourceWidth || advice.StoredHeight != advice.SourceHeight)
        AnsiConsole.MarkupLineInterpolated($"  Capture --scale {storedWidth}x{advice.StoredHeight} (unless the capture mode already has this size)");
    }
  }
}
