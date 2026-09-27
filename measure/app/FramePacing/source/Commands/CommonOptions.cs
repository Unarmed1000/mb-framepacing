//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options and value parsing shared by the commands.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

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

    /// <summary>--display-hz: the display refresh rate the user expects, compared with the one the capture shows.</summary>
    public static Option<double?> DisplayHz(string where) =>
      new Option<double?>("--display-hz")
      {
        Description =
          $"The display's refresh rate you expect (Hz). A camera capture compares it with the refresh rate calculated from the frames; a "
          + $"capture card, with its capture rate. A mismatch is a warning ({where}).",
        Validators =
        {
          result =>
          {
            if (result.GetValue<double?>("--display-hz") is <= 0)
              result.AddError("--display-hz must be positive");
          },
        },
      };

    /// <summary>--target-fps: the frame rate the application aims for; late frames are measured against it.</summary>
    public static Option<double?> TargetFps(string where) =>
      new Option<double?>("--target-fps")
      {
        Description =
          $"The frame rate the application aims for, e.g. 30 on a 60 Hz display; frames shown a refresh later are late ({where}). "
          + "Default: the pacing in the markers, else one refresh per frame (the display's native rate).",
        Validators =
        {
          result =>
          {
            if (result.GetValue<double?>("--target-fps") is <= 0)
              result.AddError("--target-fps must be positive");
          },
        },
      };
  }
}
