//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Builds ffmpeg argument lists (as ProcessStartInfo.ArgumentList entries, so no shell quoting is involved).
//*
//* Capture pipeline: device -> [crop] -> scale (area filter) -> gray -> showinfo -> raw Gray8 frames on stdout.
//* '-copyts' keeps the device timestamps and '-fps_mode passthrough' stops ffmpeg from duplicating or dropping frames to hit a rate, so every
//* frame the device delivers arrives exactly once. showinfo, the last filter, prints one line per output frame with its pts on stderr.
//* Requires FFmpeg 5.1 or newer (-fps_mode).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MB.FramePacing.Capture.Camera;

namespace MB.FramePacing.Capture.Ffmpeg
{
  public static class FfmpegCommandBuilder
  {
    public static List<string> BuildCapture(FfmpegCaptureOptions options)
    {
      var args = new List<string> { "-hide_banner", "-nostats", "-loglevel", "info" };
      AddInput(args, options);
      args.AddRange(new[] { "-an", "-sn", "-copyts", "-fps_mode", "passthrough" });
      if (options.Camera != null)
        args.AddRange(new[] { "-filter_complex", BuildCameraFilter(options.Camera), "-map", "[out]" });
      else
        args.AddRange(new[] { "-vf", BuildFilter(options) });
      args.AddRange(new[] { "-f", "rawvideo", "-pix_fmt", "gray", "pipe:1" });
      return args;
    }

    public static string BuildFilter(FfmpegCaptureOptions options)
    {
      var filters = new List<string>();
      // exact=1: crop at the given pixel even on chroma subsampled inputs (only luma is stored), so the marker grid stays where it was located
      if (options.Roi is { } roi && !roi.IsEmpty)
        filters.Add(Invariant($"crop={roi.Width}:{roi.Height}:{roi.X}:{roi.Y}:exact=1"));
      if (options.Scale is { } scale)
        filters.Add(Invariant($"scale={scale.Width}:{scale.Height}:flags=area"));
      filters.Add("format=gray");
      filters.Add("showinfo");
      return string.Join(",", filters);
    }

    /// <summary>
    /// EXPERIMENTAL camera capture: per zone crop the camera frame around the zone, undo the perspective (the zone's stored square becomes
    /// the whole output) and scale to <see cref="CameraZone.StoredSizePx"/>, then stack the zones in scanout order. Luma only from the start,
    /// so the perspective resampling touches one plane.
    /// </summary>
    public static string BuildCameraFilter(CameraRig rig)
    {
      ArgumentNullException.ThrowIfNull(rig);
      int count = rig.Zones.Count;
      var parts = new List<string>();
      var split = count > 1 ? Invariant($"split={count}") : "null";
      var labels = string.Concat(Enumerable.Range(0, count).Select(z => Invariant($"[z{z}]")));
      parts.Add(Invariant($"[0:v]format=gray,{split}{labels}"));
      for (int z = 0; z < count; ++z)
      {
        var zone = rig.Zones[z];
        var bounds = zone.StoredBounds(rig.CameraWidth, rig.CameraHeight);
        var quad = zone.StoredQuad();
        var corners = string.Join(":", quad.Select(p => Invariant($"{p.X - bounds.X:0.###}:{p.Y - bounds.Y:0.###}")));
        parts.Add(
          Invariant(
            $"[z{z}]crop={bounds.Width}:{bounds.Height}:{bounds.X}:{bounds.Y}:exact=1,perspective={corners}:sense=source:interpolation=cubic,"
          ) + Invariant($"scale={CameraZone.StoredSizePx}:{CameraZone.StoredSizePx}:flags=area[r{z}]")
        );
      }
      var stackInputs = string.Concat(Enumerable.Range(0, count).Select(z => Invariant($"[r{z}]")));
      parts.Add(count > 1 ? Invariant($"{stackInputs}vstack=inputs={count},showinfo[out]") : "[r0]showinfo[out]");
      return string.Join(";", parts);
    }

    public static List<string> BuildListDevices(FfmpegInputKind kind)
    {
      return kind switch
      {
        FfmpegInputKind.DirectShow => new List<string> { "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy" },
        FfmpegInputKind.AVFoundation => new List<string> { "-hide_banner", "-list_devices", "true", "-f", "avfoundation", "-i", "" },
        _ => throw new NotSupportedException($"ffmpeg can not list {kind} devices; enumerate them directly"),
      };
    }

    public static List<string> BuildListModes(CaptureDevice device)
    {
      return device.Kind switch
      {
        FfmpegInputKind.DirectShow => new List<string> { "-hide_banner", "-list_options", "true", "-f", "dshow", "-i", "video=" + device.Input },
        FfmpegInputKind.Video4Linux2 => new List<string> { "-hide_banner", "-list_formats", "all", "-f", "v4l2", "-i", device.Input },
        _ => throw new NotSupportedException($"Listing modes is not supported for {device.Kind} devices"),
      };
    }

    private static void AddInput(List<string> args, FfmpegCaptureOptions options)
    {
      var mode = options.Mode;
      var device = options.Device;
      switch (device.Kind)
      {
        case FfmpegInputKind.DirectShow:
          args.AddRange(new[] { "-f", "dshow", "-rtbufsize", Invariant($"{options.RealTimeBufferMegabytes}M") });
          AddSizeAndRate(args, mode);
          if (options.InputFormat != null)
            args.AddRange(IsCodec(options.InputFormat) ? new[] { "-vcodec", options.InputFormat } : new[] { "-pixel_format", options.InputFormat });
          args.AddRange(options.ExtraInputArguments);
          args.AddRange(new[] { "-i", "video=" + device.Input });
          break;
        case FfmpegInputKind.Video4Linux2:
          args.AddRange(new[] { "-f", "v4l2" });
          if (options.InputFormat != null)
            args.AddRange(new[] { "-input_format", options.InputFormat });
          AddSizeAndRate(args, mode);
          args.AddRange(options.ExtraInputArguments);
          args.AddRange(new[] { "-i", device.Input });
          break;
        case FfmpegInputKind.AVFoundation:
          args.AddRange(new[] { "-f", "avfoundation" });
          if (options.InputFormat != null)
            args.AddRange(new[] { "-pixel_format", options.InputFormat });
          AddSizeAndRate(args, mode);
          args.AddRange(options.ExtraInputArguments);
          // "<video>:none" = video device only, no audio
          args.AddRange(new[] { "-i", device.Input.Contains(':', StringComparison.Ordinal) ? device.Input : device.Input + ":none" });
          break;
        case FfmpegInputKind.Media:
          // Files are read as fast as the recorder takes them; their own timestamps become the device timestamps (-copyts)
          args.AddRange(options.ExtraInputArguments);
          args.AddRange(new[] { "-i", device.Input });
          break;
        case FfmpegInputKind.ImageSequence:
          // An ffconcat list: one entry per image with its duration, so the timestamps follow --fps or the timestamp file
          args.AddRange(new[] { "-f", "concat", "-safe", "0" });
          args.AddRange(options.ExtraInputArguments);
          args.AddRange(new[] { "-i", device.Input });
          break;
        case FfmpegInputKind.Lavfi:
          args.AddRange(new[] { "-re", "-f", "lavfi" });
          args.AddRange(options.ExtraInputArguments);
          args.AddRange(new[] { "-i", device.Input });
          break;
        default:
          throw new NotSupportedException(device.Kind.ToString());
      }
    }

    private static void AddSizeAndRate(List<string> args, RequestedMode mode)
    {
      if (mode.HasSize)
        args.AddRange(new[] { "-video_size", Invariant($"{mode.Width}x{mode.Height}") });
      if (mode.HasFps)
        args.AddRange(new[] { "-framerate", mode.Fps.ToString("0.###", CultureInfo.InvariantCulture) });
    }

    private static bool IsCodec(string format) =>
      format.Equals("mjpeg", StringComparison.OrdinalIgnoreCase) || format.Equals("h264", StringComparison.OrdinalIgnoreCase);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
  }
}
