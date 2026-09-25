//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Fast capture, step one: run ffmpeg on the whole source frame (no crop, no scale) just long enough to find the marker, then compute the
//* region to store. Nothing is recorded; the capture itself starts a new ffmpeg with the crop.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Threading;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture.Ffmpeg
{
  public static class FfmpegMarkerLocator
  {
    /// <summary>The text that asks for the marker region instead of a rectangle (--roi auto).</summary>
    public const string AutoRoi = "auto";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Live sources are read at full rate but only decoded this often, so the device buffers do not overflow while searching.</summary>
    private static readonly TimeSpan g_liveDecodeInterval = TimeSpan.FromMilliseconds(50);

    public static bool IsAutoRoi(string? text) => string.Equals(text?.Trim(), AutoRoi, StringComparison.OrdinalIgnoreCase);

    /// <summary>Find the marker in the source <paramref name="options"/> describe (their Roi and Scale are ignored) and compute the crop.</summary>
    public static MarkerLocateResult Locate(FfmpegCaptureOptions options, TimeSpan timeout, CancellationToken cancellationToken)
    {
      ArgumentNullException.ThrowIfNull(options);
      MarkerLock sourceLock;
      int width;
      int height;
      using (var source = FfmpegCaptureSource.Start(options with { Roi = null, Scale = null }, TimeSpan.FromSeconds(30)))
      {
        width = source.Format.Width;
        height = source.Format.Height;
        sourceLock = MarkerProbe.Locate(source, timeout, source.IsLive ? g_liveDecodeInterval : TimeSpan.Zero, cancellationToken);
      }
      bool mjpeg = string.Equals(options.InputFormat, "mjpeg", StringComparison.OrdinalIgnoreCase);
      return new MarkerLocateResult(sourceLock, MarkerCrop.For(sourceLock, width, height, mjpeg), width, height);
    }
  }
}
