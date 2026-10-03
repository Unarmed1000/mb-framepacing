//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Which part of its source's frames a capture stores, the same for the command line and the GUI. A recording (a video file or an image
//* folder) stores only its markers' regions unless told otherwise: reading the whole frame from ffmpeg is several times slower, and the
//* markers do not move. The whole frame stays for live sources, for 'full', and where the frames themselves are wanted (a stored size, or
//* the frames kept).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Capture.Ffmpeg
{
  public static class RegionRule
  {
    /// <summary>The text that asks for the whole frame (--roi full), where the default would be the markers' regions.</summary>
    public const string FullFrame = "full";

    public static bool IsFullFrame(string? text) => string.Equals(text?.Trim(), FullFrame, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What a capture stores: <paramref name="roiText"/> is the region asked for (a rectangle, 'auto', 'full', or nothing),
    /// <paramref name="hasScale"/> whether a stored size was given, <paramref name="recording"/> whether the source is a recording (a
    /// video file or an image folder, not a device or a stream), <paramref name="keepFrames"/> whether the frames are stored too.
    /// </summary>
    public static RegionChoice For(string? roiText, bool hasScale, bool recording, bool keepFrames)
    {
      if (IsFullFrame(roiText))
        return RegionChoice.WholeFrame;
      if (FfmpegMarkerLocator.IsAutoRoi(roiText))
        return RegionChoice.Markers;
      if (!string.IsNullOrWhiteSpace(roiText))
        return RegionChoice.Rectangle;
      return recording && !hasScale && !keepFrames ? RegionChoice.MarkersIfTheyFit : RegionChoice.WholeFrame;
    }

    /// <summary>
    /// <paramref name="options"/> with the region and stored size of <see cref="For"/> applied. <paramref name="locate"/> finds the markers
    /// (it runs only when they are needed); <paramref name="note"/> is told why the whole frame is stored when a recording's markers
    /// cannot be cropped to. A recording without any marker is <paramref name="locate"/>'s <see cref="MarkerNotFoundException"/>.
    /// </summary>
    public static FfmpegCaptureOptions Apply(
      FfmpegCaptureOptions options,
      string? roiText,
      (int Width, int Height)? scale,
      bool keepFrames,
      Func<MarkerLocateResult> locate,
      Action<string> note
    )
    {
      ArgumentNullException.ThrowIfNull(options);
      ArgumentNullException.ThrowIfNull(locate);
      ArgumentNullException.ThrowIfNull(note);
      var wholeFrame = options with { Roi = null, SyncRoi = null, RoiDownscale = 1, Scale = scale };
      switch (For(roiText, scale != null, options.InputPath != null, keepFrames))
      {
        case RegionChoice.Rectangle:
          return wholeFrame with { Roi = PixelRect.Parse(roiText!.Trim()) };
        case RegionChoice.Markers:
          if (scale != null)
            throw new ArgumentException("The region 'auto' chooses the stored size itself: give no stored size (--scale).");
          return locate().Apply(options);
        case RegionChoice.MarkersIfTheyFit:
          try
          {
            return locate().Apply(options);
          }
          catch (MarkerRegionException exception)
          {
            note("Storing the whole frame, not only the markers: " + exception.Message);
            return wholeFrame;
          }
        default:
          return wholeFrame;
      }
    }
  }
}
