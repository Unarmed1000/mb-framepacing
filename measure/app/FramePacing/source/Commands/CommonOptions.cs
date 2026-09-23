//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options and value parsing shared by the commands.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.CommandLine;
using MB.FramePacing.Capture;

namespace MB.FramePacing.App.Commands
{
  internal static class CommonOptions
  {
    /// <summary>Global (recursive) option: which mb-framepacing.json to use.</summary>
    public static readonly Option<string?> Config = new Option<string?>("--config")
    {
      Description = "Configuration file (default: mb-framepacing.json next to the executable, else the per user file - see 'config').",
      Recursive = true,
    };

    public static FramePacingConfig LoadConfig(ParseResult parseResult) => FramePacingConfig.Load(parseResult.GetValue(Config));

    public static Option<string?> Ffmpeg() =>
      new Option<string?>("--ffmpeg")
      {
        Description = "Path to the ffmpeg executable (default: $MB_FFMPEG, then ffmpegPath in the configuration file, then PATH). Needs FFmpeg 5.1+.",
      };
  }
}
