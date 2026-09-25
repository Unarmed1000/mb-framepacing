//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Fast capture: the smallest region of the source that holds every marker drawn at a located origin (the largest start marker included),
//* and the integer downscale that still leaves the recommended number of stored pixels per module. The marker must not move. The crop is
//* applied exactly (ffmpeg crop exact=1: odd offsets on chroma subsampled inputs are fine, only luma is stored).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  public static class MarkerCrop
  {
    /// <param name="sourceLock">The frame marker lock in source pixels.</param>
    /// <param name="sourceWidth">Source frame width.</param>
    /// <param name="sourceHeight">Source frame height.</param>
    /// <param name="mjpeg">The capture card delivers MJPEG (needs 4 stored pixels per module instead of 3).</param>
    public static MarkerCropResult For(MarkerLock sourceLock, int sourceWidth, int sourceHeight, bool mjpeg = false)
    {
      if (sourceWidth <= 0 || sourceHeight <= 0)
        throw new ArgumentOutOfRangeException(nameof(sourceHeight), "The source size must be positive");
      if (sourceLock.ModuleSizePx <= 0)
        throw new ArgumentOutOfRangeException(nameof(sourceLock), "The marker lock has no module size");

      // Applications draw whole pixel modules; the located size is an estimate, so round it before choosing the downscale
      int module = Math.Max(1, (int)Math.Round(sourceLock.ModuleSizePx));
      int target = MarkerRenderer.RecommendModuleSizePx(1, 1, mjpeg);
      int factor = Math.Max(1, module / target);

      // One extra module around the search region absorbs small errors in the located origin
      var region = sourceLock.SearchRegion.Inflate(module);
      // The crop starts a whole number of factors before the marker origin, so the downscale keeps module edges on stored pixel edges
      // (a half pixel offset blurs every module), and its size is a multiple of the factor, so the stored size is whole
      var (left, width) = Span(region.X, region.Right, sourceLock.Bounds.X, sourceWidth, factor);
      var (top, height) = Span(region.Y, region.Bottom, sourceLock.Bounds.Y, sourceHeight, factor);
      var roi = new PixelRect(left, top, width, height);

      if (roi.IsEmpty || roi.Intersect(sourceLock.Bounds) != sourceLock.Bounds)
        throw new InvalidOperationException(
          $"The marker at {sourceLock.Bounds} does not fit in the {sourceWidth}x{sourceHeight} source; it must be drawn fully inside the frame."
        );
      return new MarkerCropResult(roi, factor, sourceLock.ModuleSizePx / factor);
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
