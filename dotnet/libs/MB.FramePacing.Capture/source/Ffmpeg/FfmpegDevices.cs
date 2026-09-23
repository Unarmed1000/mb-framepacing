//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Device and mode discovery for the ffmpeg backend.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MB.FramePacing.Capture.Ffmpeg
{
  public static class FfmpegDevices
  {
    public static List<CaptureDevice> ListDevices(string ffmpegPath)
    {
      switch (CaptureDevice.PlatformKind)
      {
        case FfmpegInputKind.DirectShow:
          return FfmpegDeviceParser.ParseDirectShowDevices(
            RunForOutput(ffmpegPath, FfmpegCommandBuilder.BuildListDevices(FfmpegInputKind.DirectShow))
          );
        case FfmpegInputKind.AVFoundation:
          return FfmpegDeviceParser.ParseAVFoundationDevices(
            RunForOutput(ffmpegPath, FfmpegCommandBuilder.BuildListDevices(FfmpegInputKind.AVFoundation))
          );
        default:
          return ListVideo4Linux2Devices();
      }
    }

    public static List<CaptureMode> ListModes(string ffmpegPath, CaptureDevice device)
    {
      return device.Kind switch
      {
        FfmpegInputKind.DirectShow => FfmpegDeviceParser.ParseDirectShowModes(RunForOutput(ffmpegPath, FfmpegCommandBuilder.BuildListModes(device))),
        FfmpegInputKind.Video4Linux2 => FfmpegDeviceParser.ParseVideo4Linux2Modes(
          RunForOutput(ffmpegPath, FfmpegCommandBuilder.BuildListModes(device))
        ),
        _ => new List<CaptureMode>(),
      };
    }

    /// <summary>ffmpeg version line ("ffmpeg version 7.1 ..."), for logs and capture.json.</summary>
    public static string GetVersion(string ffmpegPath)
    {
      var lines = RunForOutput(ffmpegPath, new List<string> { "-hide_banner", "-version" });
      return lines.FirstOrDefault(line => line.StartsWith("ffmpeg version", StringComparison.Ordinal)) ?? "unknown";
    }

    /// <summary>The oldest ffmpeg the capture command line works with (-fps_mode).</summary>
    public static readonly Version MinimumVersion = new Version(5, 1);

    /// <summary>
    /// Parse "ffmpeg version 7.1.1-..." or "ffmpeg version n6.0 ...". Git snapshot builds ("N-113284-g...") carry no release number and give
    /// null (treated as recent).
    /// </summary>
    public static Version? ParseVersion(string versionLine)
    {
      var match = System.Text.RegularExpressions.Regex.Match(versionLine, @"ffmpeg version n?(\d+)\.(\d+)");
      return match.Success
        ? new Version(
          int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
          int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
        )
        : null;
    }

    private static List<CaptureDevice> ListVideo4Linux2Devices()
    {
      var devices = new List<CaptureDevice>();
      if (!Directory.Exists("/dev"))
        return devices;
      foreach (var path in Directory.GetFiles("/dev", "video*").OrderBy(p => p.Length).ThenBy(p => p, StringComparer.Ordinal))
      {
        var nameFile = Path.Combine("/sys/class/video4linux", Path.GetFileName(path), "name");
        var name = File.Exists(nameFile) ? File.ReadAllText(nameFile).Trim() : path;
        devices.Add(new CaptureDevice(FfmpegInputKind.Video4Linux2, path, $"{name} ({path})"));
      }
      return devices;
    }

    /// <summary>Run ffmpeg for a listing and return stdout + stderr lines (listings go to stderr and ffmpeg exits with an error code).</summary>
    private static List<string> RunForOutput(string ffmpegPath, List<string> arguments)
    {
      var startInfo = new ProcessStartInfo(ffmpegPath)
      {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      };
      foreach (var argument in arguments)
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ffmpeg");
      process.StandardInput.Close();
      var stdout = process.StandardOutput.ReadToEndAsync();
      var stderr = process.StandardError.ReadToEndAsync();
      if (!process.WaitForExit(15000))
      {
        process.Kill(entireProcessTree: true);
        throw new TimeoutException("ffmpeg did not finish listing devices within 15s");
      }
      var text = stdout.Result + Environment.NewLine + stderr.Result;
      return text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
    }
  }
}
