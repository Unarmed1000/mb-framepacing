//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Fast capture, step one: run ffmpeg on the whole source frame (no crop, no scale) just long enough to find the marker, then compute the
//* region to store. Nothing is recorded; the capture itself starts a new ffmpeg with the crop.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Threading;
using MB.FramePacing.MarkerDecoding;

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

    /// <summary>
    /// Find the markers in the source <paramref name="options"/> describe (their Roi and Scale are ignored) and compute the crops: the main
    /// marker's and, when the source shows a sync marker, that one's. A live source is given <paramref name="timeout"/>; a recording is
    /// read until its first marker, to its end if need be (it may start before the application draws one), and then is a
    /// <see cref="MarkerNotFoundException"/>.
    /// </summary>
    public static MarkerLocateResult Locate(FfmpegCaptureOptions options, TimeSpan timeout, CancellationToken cancellationToken)
    {
      ArgumentNullException.ThrowIfNull(options);
      MarkerProbeResult found;
      int width;
      int height;
      using (var source = FfmpegCaptureSource.Start(options with { Roi = null, SyncRoi = null, Scale = null }, TimeSpan.FromSeconds(30)))
      {
        width = source.Format.Width;
        height = source.Format.Height;
        found = MarkerProbe.Locate(source, source.IsLive ? timeout : null, source.IsLive ? g_liveDecodeInterval : TimeSpan.Zero, cancellationToken);
      }
      bool mjpeg = string.Equals(options.InputFormat, "mjpeg", StringComparison.OrdinalIgnoreCase);
      return new MarkerLocateResult(found.Main, MarkerCrop.For(found.Main, found.Sync, width, height, mjpeg), width, height)
      {
        SyncLock = found.Sync,
      };
    }
  }
}
