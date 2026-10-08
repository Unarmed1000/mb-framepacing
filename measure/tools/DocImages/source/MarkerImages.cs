//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Marker example images for the documentation: the start, frame and end markers drawn exactly the way an application draws them (pixel
//* aligned modules, pure black/white). The README's pictures of the marker in a real application are screenshots of the unofficial
//* gtec-demo-framework's FramePacing sample.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.DocImages
{
  internal static class MarkerImages
  {
    private sealed class RgbImage(int width, int height)
    {
      public readonly int Width = width;
      public readonly int Height = height;
      public readonly int[] Pixels = new int[width * height];

      public void Fill(int x0, int y0, int w, int h, int argb)
      {
        for (int y = Math.Max(y0, 0); y < Math.Min(y0 + h, Height); ++y)
          Array.Fill(Pixels, argb, (y * Width) + Math.Max(x0, 0), Math.Max(0, Math.Min(x0 + w, Width) - Math.Max(x0, 0)));
      }

      public void Save(string path)
      {
        using var bitmap = new WriteableBitmap(new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = bitmap.Lock())
        {
          for (int y = 0; y < Height; ++y)
            Marshal.Copy(Pixels, y * Width, buffer.Address + (y * buffer.RowBytes), Width);
        }
        bitmap.Save(path, new PngBitmapEncoderOptions());
      }
    }

    private static int Rgb(int r, int g, int b) =>
      unchecked((int)0xFF000000) | (Math.Clamp(r, 0, 255) << 16) | (Math.Clamp(g, 0, 255) << 8) | Math.Clamp(b, 0, 255);

    public static void WriteAll(string directory)
    {
      var animationTime = NanosecondTimeSpan.FromSeconds(20.567);
      var frameMarker = new MarkerPayload(MarkerKind.Frame, 7, 1234, MB.FramePacing.Marker.MarkerFlags.NoFlags, animationTime);
      var start = StartMetadata.FromTag(new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc).Ticks, "menu scroll");

      // The three kinds at the same module size
      foreach (
        var (name, payload, metadata) in new (string, MarkerPayload, StartMetadata?)[]
        {
          ("marker-start.png", frameMarker with { Kind = MarkerKind.SequenceStart, FrameIndex = 1200 }, start),
          ("marker-frame.png", frameMarker, null),
          ("marker-end.png", frameMarker with { Kind = MarkerKind.SequenceEnd, FrameIndex = 2400 }, null),
        }
      )
      {
        var modules = MarkerRenderer.GenerateModules(payload, metadata);
        int size = MarkerRenderer.MarkerSizePx(6);
        var image = new RgbImage(size, size);
        DrawModules(image, modules, 0, 0, 6);
        image.Save(Path.Combine(directory, name));
      }
    }

    /// <summary>White quiet zone + black modules on pixel edges, like GenerateQuads draws them.</summary>
    private static void DrawModules(RgbImage image, ModuleMatrix modules, int originX, int originY, int moduleSize)
    {
      int quiet = MarkerRenderer.RecommendedQuietZoneModules;
      int size = (modules.Size + (2 * quiet)) * moduleSize;
      image.Fill(originX, originY, size, size, Rgb(255, 255, 255));
      for (int y = 0; y < modules.Size; ++y)
      {
        for (int x = 0; x < modules.Size; ++x)
        {
          if (modules.IsDark(x, y))
            image.Fill(originX + ((quiet + x) * moduleSize), originY + ((quiet + y) * moduleSize), moduleSize, moduleSize, Rgb(0, 0, 0));
        }
      }
    }
  }
}
