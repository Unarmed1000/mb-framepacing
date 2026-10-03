//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Fast capture: the smallest region of the source that holds the main marker drawn at a located origin (every kind has one size),
//* and the integer downscale that still leaves the recommended number of stored pixels per module. The marker must not move. The crop is
//* applied exactly (ffmpeg crop exact=1: odd offsets on chroma subsampled inputs are fine, only luma is stored).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.MarkerDecoding
{
  public static class MarkerCrop
  {
    /// <param name="sourceLock">The frame marker lock in source pixels.</param>
    /// <param name="sourceWidth">Source frame width.</param>
    /// <param name="sourceHeight">Source frame height.</param>
    /// <param name="mjpeg">The capture card delivers MJPEG (needs 4 stored pixels per module instead of 3).</param>
    public static MarkerCropResult For(MarkerLock sourceLock, int sourceWidth, int sourceHeight, bool mjpeg = false) =>
      For(sourceLock, null, sourceWidth, sourceHeight, mjpeg);

    /// <summary>
    /// The crop for the main marker at <paramref name="sourceLock"/> and, when the source shows a sync marker at
    /// <paramref name="syncLock"/>, a second crop for it, with the same downscale and each starting a whole number of downscale steps
    /// before its marker. Crops that would overlap become one that holds both markers.
    /// </summary>
    public static MarkerCropResult For(MarkerLock sourceLock, MarkerLock? syncLock, int sourceWidth, int sourceHeight, bool mjpeg = false)
    {
      if (sourceWidth <= 0 || sourceHeight <= 0)
        throw new ArgumentOutOfRangeException(nameof(sourceHeight), "The source size must be positive");
      if (sourceLock.ModuleSizePx <= 0)
        throw new ArgumentOutOfRangeException(nameof(sourceLock), "The marker lock has no module size");

      // Applications draw whole pixel modules; the located size is an estimate, so round it before choosing the downscale
      int module = Math.Max(1, (int)Math.Round(sourceLock.ModuleSizePx));
      int target = MarkerRenderer.RecommendModuleSizePx(1, 1, mjpeg);
      int factor = Math.Max(1, module / target);

      var roi = RegionOf(sourceLock, sourceLock.SearchRegion.Inflate(module), sourceWidth, sourceHeight, factor);
      double storedModule = sourceLock.ModuleSizePx / factor;
      if (syncLock is not { } sync)
        return new MarkerCropResult(roi, factor, storedModule);
      var syncRoi = RegionOf(sync, sync.SearchRegion.Inflate(module), sourceWidth, sourceHeight, factor);
      if (roi.Intersect(syncRoi).IsEmpty)
        return new MarkerCropResult(roi, factor, storedModule, syncRoi);

      // The two regions overlap (a small frame): one crop that holds both markers, on the main marker's grid
      var both = new PixelRect(
        Math.Min(roi.X, syncRoi.X),
        Math.Min(roi.Y, syncRoi.Y),
        Math.Max(roi.Right, syncRoi.Right) - Math.Min(roi.X, syncRoi.X),
        Math.Max(roi.Bottom, syncRoi.Bottom) - Math.Min(roi.Y, syncRoi.Y)
      );
      return new MarkerCropResult(RegionOf(sourceLock, both, sourceWidth, sourceHeight, factor), factor, storedModule);
    }

    /// <summary>
    /// The crop that holds <paramref name="region"/> around the marker at <paramref name="markerLock"/>: one extra module around the
    /// marker's search region absorbs small errors in the located origin. It starts a whole number of factors before the marker origin,
    /// so the downscale keeps module edges on stored pixel edges (a half pixel offset blurs every module), and its size is a multiple of
    /// the factor, so the stored size is whole.
    /// </summary>
    private static PixelRect RegionOf(MarkerLock markerLock, PixelRect region, int sourceWidth, int sourceHeight, int factor)
    {
      var (left, width) = Span(region.X, region.Right, markerLock.Bounds.X, sourceWidth, factor);
      var (top, height) = Span(region.Y, region.Bottom, markerLock.Bounds.Y, sourceHeight, factor);
      var roi = new PixelRect(left, top, width, height);
      if (roi.IsEmpty || roi.Intersect(markerLock.Bounds) != markerLock.Bounds)
        throw new MarkerRegionException(
          $"The marker at {markerLock.Bounds} does not fit in the {sourceWidth}x{sourceHeight} source; it must be drawn fully inside the frame."
        );
      return roi;
    }

    /// <summary>One axis: [start, end) clamped to [0, limit), starting a multiple of <paramref name="factor"/> before <paramref name="origin"/>.</summary>
    private static (int Start, int Length) Span(int start, int end, int origin, int limit, int factor)
    {
      start = Math.Max(0, start);
      start -= ((start - origin) % factor + factor) % factor;
      if (start < 0)
        start += factor;
      end = Math.Min(end, limit);
      int length = (end - start + factor - 1) / factor * factor;
      if (start + length > limit)
        length -= factor;
      return (start, Math.Max(0, length));
    }
  }
}
