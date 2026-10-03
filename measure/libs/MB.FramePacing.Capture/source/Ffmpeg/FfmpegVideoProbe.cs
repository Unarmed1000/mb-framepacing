//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a video file holds, from ffmpeg's description of its input ("ffmpeg -i file": the "Input #0, <demuxers>, from" line, "Duration:"
//* and the first "Stream #0:n...: Video: <codec> (<profile>) (...), <pixel format>, ..., <fps> fps" line), for the playback page's question
//* whether browsers can play it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace MB.FramePacing.Capture.Ffmpeg
{
  public static partial class FfmpegVideoProbe
  {
    /// <summary>Describe the first video stream of <paramref name="file"/>. Throws when ffmpeg finds none.</summary>
    public static VideoCodecInfo Probe(string ffmpegPath, string file)
    {
      var startInfo = new ProcessStartInfo(ffmpegPath)
      {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      };
      // Only the description: without an output ffmpeg describes the input and exits with an error code
      foreach (var argument in new[] { "-hide_banner", "-nostdin", "-i", Path.GetFullPath(file) })
        startInfo.ArgumentList.Add(argument);
      using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ffmpeg");
      process.StandardInput.Close();
      var stdout = process.StandardOutput.ReadToEndAsync();
      var stderr = process.StandardError.ReadToEndAsync();
      if (!process.WaitForExit(30000))
      {
        process.Kill(entireProcessTree: true);
        throw new TimeoutException($"ffmpeg did not describe {file} within 30 s");
      }
      var lines = (stdout.Result + "\n" + stderr.Result).Split('\n');
      return Parse(lines, file) ?? throw new InvalidDataException($"ffmpeg finds no video stream in {file}");
    }

    /// <summary>The first video stream of ffmpeg's description <paramref name="lines"/> of <paramref name="file"/>; null when there is none.</summary>
    public static VideoCodecInfo? Parse(IEnumerable<string> lines, string file)
    {
      string container = string.Empty;
      TimeSpan? duration = null;
      foreach (var raw in lines)
      {
        string line = raw.TrimEnd('\r');
        var input = InputRegex().Match(line);
        if (input.Success)
        {
          container = input.Groups["container"].Value;
          continue;
        }
        var durationMatch = DurationRegex().Match(line);
        if (durationMatch.Success)
        {
          duration = VideoCodecInfo.ParseDuration(durationMatch.Groups["duration"].Value);
          continue;
        }
        var stream = StreamRegex().Match(line);
        if (!stream.Success)
          continue;
        var fps = FpsRegex().Match(line);
        return new VideoCodecInfo(
          VideoCodecInfo.ExtensionOf(file),
          container,
          stream.Groups["codec"].Value,
          stream.Groups["profile"].Value.Trim(),
          stream.Groups["pix"].Value,
          fps.Success ? double.Parse(fps.Groups["fps"].Value, CultureInfo.InvariantCulture) : null,
          duration
        );
      }
      return null;
    }

    [GeneratedRegex(@"^Input #0, (?<container>.+), from ")]
    private static partial Regex InputRegex();

    [GeneratedRegex(@"^\s*Duration: (?<duration>\d+:\d\d:\d\d\.\d+)")]
    private static partial Regex DurationRegex();

    // "Video: h264 (High 4:4:4 Predictive) (avc1 / 0x31637661), yuv444p(progressive), ...": the profile has no " / ", the codec tag has
    [GeneratedRegex(@"Stream #\d+:\d+.*?: Video: (?<codec>[\w-]+)(?: \((?<profile>[^()/]*)\))?(?: \([^()]*\))*, (?<pix>\w+)")]
    private static partial Regex StreamRegex();

    [GeneratedRegex(@", (?<fps>\d+(?:\.\d+)?) fps")]
    private static partial Regex FpsRegex();
  }
}
