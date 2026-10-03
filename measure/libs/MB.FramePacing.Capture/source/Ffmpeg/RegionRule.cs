//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Which part of its source's frames a capture stores, the same for the command line and the GUI. A recording (a video file or an image
//* folder) stores only its markers' regions unless told otherwise: reading the whole frame from ffmpeg is several times slower, and the
//* markers do not move. The whole frame stays for live sources, for 'full', and where the frames themselves are wanted (a stored size, or
//* the frames kept).
//*
//* A region asked for is a rectangle, or two joined by '+': the main marker's, then the sync marker's, stored as one frame with the first on
//* top, which is what locating the markers gives. So a located pair can be written down and used again, and tearing is still checked.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Globalization;
using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Capture.Ffmpeg
{
  public static class RegionRule
  {
    /// <summary>The text that asks for the whole frame (--roi full), where the default would be the markers' regions.</summary>
    public const string FullFrame = "full";

    /// <summary>What joins two rectangles in a region text: "14,14,330,330+14,832,234,234".</summary>
    public const char RegionSeparator = '+';

    public static bool IsFullFrame(string? text) => string.Equals(text?.Trim(), FullFrame, StringComparison.OrdinalIgnoreCase);

    /// <summary>A region and, when there are two, the second one stored below it, as text that <see cref="ParseRegions"/> reads.</summary>
    public static string RegionText(PixelRect roi, PixelRect syncRoi = default) =>
      syncRoi.IsEmpty ? roi.ToString() : $"{roi}{RegionSeparator}{syncRoi}";

    /// <summary>The rectangle of a region text, or its two ("x,y,width,height+x,y,width,height": the second is stored below the first).</summary>
    public static (PixelRect Roi, PixelRect? SyncRoi) ParseRegions(string text)
    {
      ArgumentNullException.ThrowIfNull(text);
      var parts = text.Split(RegionSeparator);
      if (parts.Length > 2)
        throw new FormatException($"Expected one region as 'x,y,width,height', or two joined by '{RegionSeparator}', but got '{text}'");
      var roi = PixelRect.Parse(parts[0].Trim());
      return parts.Length == 2 ? (roi, PixelRect.Parse(parts[1].Trim())) : (roi, null);
    }

    /// <summary>
    /// The whole-number downscale that stores two stacked regions as <paramref name="scale"/> (the size of the two together: as wide as
    /// the wider one, as high as both), 1 without a stored size. Two regions are cropped and downscaled alike, so that a stored pixel
    /// is the same number of source pixels in both: a stored size that is no whole-number downscale of both is refused.
    /// </summary>
    public static int StackedDownscale(PixelRect roi, PixelRect syncRoi, (int Width, int Height)? scale)
    {
      int width = Math.Max(roi.Width, syncRoi.Width);
      int height = roi.Height + syncRoi.Height;
      if (scale is not { } stored)
        return 1;
      int factor = width / stored.Width;
      if (
        factor < 1
        || stored.Width * factor != width
        || stored.Height * factor != height
        || roi.Width % factor != 0
        || roi.Height % factor != 0
        || syncRoi.Width % factor != 0
        || syncRoi.Height % factor != 0
      )
        throw new ArgumentException(
          string.Create(
            CultureInfo.InvariantCulture,
            $"Two regions are stored one above the other, {width}x{height} as they are: the stored size must be that divided by a whole number that divides both regions' sizes, not {stored.Width}x{stored.Height}."
          )
        );
      return factor;
    }

    /// <summary>
    /// What a capture stores: <paramref name="roiText"/> is the region asked for (a rectangle or two, 'auto', 'full', or nothing),
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
          var (roi, syncRoi) = ParseRegions(roiText!);
          if (syncRoi is not { } second)
            return wholeFrame with { Roi = roi };
          // Two regions, stacked: downscaled by a whole number (ffmpeg scales each before they are stacked), not to a stored size
          return wholeFrame with { Roi = roi, SyncRoi = second, RoiDownscale = StackedDownscale(roi, second, scale), Scale = null };
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
