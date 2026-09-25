//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Outlines the calibrated marker zones on a camera frame for the camera wizard (VERY EXPERIMENTAL camera support): the timing zone (scanned
//* first) in green, the second zone in orange.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using MB.FramePacing.Capture.Camera;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Gui
{
  public static class CameraOverlay
  {
    private static readonly int[] g_zoneColors = { unchecked((int)0xFF34C759), unchecked((int)0xFFFF9F0A) };

    /// <summary>
    /// Outline each zone's frame marker, around its quiet zone so the modules stay visible, into BGRA <paramref name="pixels"/> (as made by
    /// <see cref="GrayBitmap.ToBgra"/>). The frame is shown scaled down, so the lines are a few pixels wide.
    /// </summary>
    public static void Draw(int[] pixels, int width, int height, IReadOnlyList<CameraZone> zones)
    {
      int thickness = Math.Max(2, Math.Min(width, height) / 120);
      double near = -MarkerRenderer.RecommendedQuietZoneModules;
      double far = MarkerRenderer.FrameQrModuleCount + MarkerRenderer.RecommendedQuietZoneModules;
      for (int i = 0; i < zones.Count; ++i)
      {
        var map = zones[i].ModuleToCamera;
        var corners = new[]
        {
          map.Map(new ImagePoint(near, near)),
          map.Map(new ImagePoint(far, near)),
          map.Map(new ImagePoint(far, far)),
          map.Map(new ImagePoint(near, far)),
        };
        int color = g_zoneColors[Math.Min(i, g_zoneColors.Length - 1)];
        for (int c = 0; c < corners.Length; ++c)
          DrawLine(pixels, width, height, corners[c], corners[(c + 1) % corners.Length], thickness, color);
      }
    }

    private static void DrawLine(int[] pixels, int width, int height, ImagePoint from, ImagePoint to, int thickness, int color)
    {
      double length = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
      int steps = Math.Max(1, (int)Math.Ceiling(length));
      int half = thickness / 2;
      for (int s = 0; s <= steps; ++s)
      {
        double t = (double)s / steps;
        int cx = (int)Math.Round(from.X + ((to.X - from.X) * t));
        int cy = (int)Math.Round(from.Y + ((to.Y - from.Y) * t));
        for (int y = cy - half; y < cy - half + thickness; ++y)
        {
          if (y < 0 || y >= height)
            continue;
          for (int x = cx - half; x < cx - half + thickness; ++x)
          {
            if (x >= 0 && x < width)
              pixels[(y * width) + x] = color;
          }
        }
      }
    }
  }
}
