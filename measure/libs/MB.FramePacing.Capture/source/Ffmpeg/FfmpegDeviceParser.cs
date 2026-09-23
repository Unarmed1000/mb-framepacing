//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Parses ffmpeg's device and mode listings (dshow -list_devices/-list_options, avfoundation -list_devices, v4l2 -list_formats).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MB.FramePacing.Capture.Ffmpeg
{
  public static partial class FfmpegDeviceParser
  {
    /// <summary>
    /// dshow device list. Newer ffmpeg tags each device: '"Name" (video)'. Older ffmpeg lists names under a "DirectShow video devices" header.
    /// </summary>
    public static List<CaptureDevice> ParseDirectShowDevices(IEnumerable<string> lines)
    {
      var devices = new List<CaptureDevice>();
      bool legacyVideoSection = false;
      foreach (var line in lines)
      {
        if (line.Contains("DirectShow video devices", StringComparison.Ordinal))
        {
          legacyVideoSection = true;
          continue;
        }
        if (line.Contains("DirectShow audio devices", StringComparison.Ordinal))
        {
          legacyVideoSection = false;
          continue;
        }
        if (line.Contains("Alternative name", StringComparison.Ordinal))
          continue;

        var tagged = DirectShowTaggedDeviceRegex().Match(line);
        if (tagged.Success)
        {
          if (tagged.Groups["type"].Value.Contains("video", StringComparison.OrdinalIgnoreCase))
            AddUnique(devices, new CaptureDevice(FfmpegInputKind.DirectShow, tagged.Groups["name"].Value, tagged.Groups["name"].Value));
          continue;
        }
        if (legacyVideoSection)
        {
          var legacy = DirectShowLegacyDeviceRegex().Match(line);
          if (legacy.Success)
            AddUnique(devices, new CaptureDevice(FfmpegInputKind.DirectShow, legacy.Groups["name"].Value, legacy.Groups["name"].Value));
        }
      }
      return devices;
    }

    /// <summary>dshow '-list_options true': "vcodec=mjpeg  min s=1920x1080 fps=5 max s=1920x1080 fps=60". One mode per line (max size/rate).</summary>
    public static List<CaptureMode> ParseDirectShowModes(IEnumerable<string> lines)
    {
      var modes = new List<CaptureMode>();
      foreach (var line in lines)
      {
        var match = DirectShowModeRegex().Match(line);
        if (!match.Success)
          continue;
        bool compressed = match.Groups["key"].Value == "vcodec";
        var mode = new CaptureMode(
          int.Parse(match.Groups["w"].Value, CultureInfo.InvariantCulture),
          int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture),
          double.Parse(match.Groups["fps"].Value, CultureInfo.InvariantCulture),
          match.Groups["format"].Value,
          compressed
        );
        if (!modes.Contains(mode))
          modes.Add(mode);
      }
      return modes;
    }

    /// <summary>avfoundation '-list_devices true': "[0] FaceTime HD Camera" lines between the video and audio headers.</summary>
    public static List<CaptureDevice> ParseAVFoundationDevices(IEnumerable<string> lines)
    {
      var devices = new List<CaptureDevice>();
      bool videoSection = false;
      foreach (var line in lines)
      {
        if (line.Contains("AVFoundation video devices", StringComparison.Ordinal))
        {
          videoSection = true;
          continue;
        }
        if (line.Contains("AVFoundation audio devices", StringComparison.Ordinal))
        {
          videoSection = false;
          continue;
        }
        if (!videoSection)
          continue;
        var match = AVFoundationDeviceRegex().Match(line);
        if (match.Success)
          devices.Add(new CaptureDevice(FfmpegInputKind.AVFoundation, match.Groups["index"].Value, match.Groups["name"].Value.Trim()));
      }
      return devices;
    }

    /// <summary>v4l2 '-list_formats all': "Raw : yuyv422 : YUYV 4:2:2 : 640x480 1280x720". v4l2 does not report rates here (Fps = 0).</summary>
    public static List<CaptureMode> ParseVideo4Linux2Modes(IEnumerable<string> lines)
    {
      var modes = new List<CaptureMode>();
      foreach (var line in lines)
      {
        var match = Video4Linux2FormatRegex().Match(line);
        if (!match.Success)
          continue;
        bool compressed = match.Groups["kind"].Value.StartsWith("Compressed", StringComparison.Ordinal);
        string format = match.Groups["format"].Value.Trim();
        foreach (Match size in SizeRegex().Matches(match.Groups["sizes"].Value))
        {
          var mode = new CaptureMode(
            int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture),
            0,
            format,
            compressed
          );
          if (!modes.Contains(mode))
            modes.Add(mode);
        }
      }
      return modes;
    }

    private static void AddUnique(List<CaptureDevice> devices, CaptureDevice device)
    {
      if (!devices.Exists(existing => existing.Input == device.Input))
        devices.Add(device);
    }

    [GeneratedRegex("\"(?<name>[^\"]+)\"\\s*\\((?<type>[^)]+)\\)")]
    private static partial Regex DirectShowTaggedDeviceRegex();

    [GeneratedRegex("\\]\\s+\"(?<name>[^\"]+)\"\\s*$")]
    private static partial Regex DirectShowLegacyDeviceRegex();

    [GeneratedRegex(@"(?<key>vcodec|pixel_format)=(?<format>\S+)\s+min s=\d+x\d+ fps=[\d.]+\s+max s=(?<w>\d+)x(?<h>\d+) fps=(?<fps>[\d.]+)")]
    private static partial Regex DirectShowModeRegex();

    [GeneratedRegex(@"\]\s*\[(?<index>\d+)\]\s*(?<name>.+)$")]
    private static partial Regex AVFoundationDeviceRegex();

    // The description may itself contain colons ("YUYV 4:2:2"), so the sizes are whatever follows the last colon
    [GeneratedRegex(@"(?<kind>Raw|Compressed)\s*:\s*(?<format>\S+)\s*:.*:\s*(?<sizes>(?:\d+x\d+\s*)+)$")]
    private static partial Regex Video4Linux2FormatRegex();

    [GeneratedRegex(@"(\d+)x(\d+)")]
    private static partial Regex SizeRegex();
  }
}
