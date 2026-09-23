//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Marker example images for the documentation: markers drawn into a mock game frame exactly the way an application draws them (pixel
//* aligned modules, pure black/white, drawn last on top of the scene).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MB.FramePacing.Marker;

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
      var frameMarker = new MarkerPayload(1234, TimeSpan.FromSeconds(20.567).Ticks, 7, MarkerKind.Frame);
      var start = new StartMetadata(new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc).Ticks, "menu scroll benchmark");

      // A marker in a 1280x720 frame at the recommended place: top-left, 32 px in
      var scene = CreateScene(1280, 720);
      DrawMarker(scene, frameMarker, null, 32, 32, 4);
      scene.Save(Path.Combine(directory, "marker-in-frame.png"));

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
        int size = MarkerRenderer.MarkerSizePx(6, MarkerRenderer.RecommendedQuietZoneModules, modules.Size);
        var image = new RgbImage(size, size);
        DrawModules(image, modules, 0, 0, 6);
        image.Save(Path.Combine(directory, name));
      }

      // Tearing check: the same marker at the top, middle and bottom of the frame
      var tearing = CreateScene(1280, 720);
      int markerSize = MarkerRenderer.MarkerSizePx(3);
      DrawMarker(tearing, frameMarker, null, 32, 32, 3);
      DrawMarker(tearing, frameMarker, null, 32, (720 - markerSize) / 2, 3);
      DrawMarker(tearing, frameMarker, null, 32, 720 - 32 - markerSize, 3);
      tearing.Save(Path.Combine(directory, "marker-tearing.png"));
    }

    /// <summary>A colourful stand-in for a game frame: sky gradient, ground, a few shapes.</summary>
    private static RgbImage CreateScene(int width, int height)
    {
      var image = new RgbImage(width, height);
      int horizon = height * 3 / 5;
      for (int y = 0; y < height; ++y)
      {
        int color =
          y < horizon
            ? Rgb(40 + (y * 60 / horizon), 90 + (y * 90 / horizon), 170 + (y * 60 / horizon))
            : Rgb(60 + ((y - horizon) * 40 / (height - horizon)), 120 - ((y - horizon) * 30 / (height - horizon)), 60);
        Array.Fill(image.Pixels, color, y * width, width);
      }
      var random = new Random(7);
      for (int i = 0; i < 9; ++i)
      {
        int w = random.Next(60, 180);
        int h = random.Next(80, 260);
        int x = random.Next(260, width - w);
        image.Fill(x, horizon - h, w, h, Rgb(random.Next(70, 160), random.Next(60, 120), random.Next(90, 170)));
        image.Fill(x + 10, horizon - h + 12, w - 20, 10, Rgb(240, 220, 140));
      }
      image.Fill(width / 2 - 20, horizon + 40, 40, 90, Rgb(220, 70, 60));
      return image;
    }

    private static void DrawMarker(RgbImage image, MarkerPayload payload, StartMetadata? metadata, int originX, int originY, int moduleSize) =>
      DrawModules(image, MarkerRenderer.GenerateModules(payload, metadata), originX, originY, moduleSize);

    /// <summary>White quiet zone + black modules on pixel edges, like GenerateQuads draws them.</summary>
    private static void DrawModules(RgbImage image, ModuleMatrix modules, int originX, int originY, int moduleSize)
    {
      int quiet = MarkerRenderer.RecommendedQuietZoneModules;
      int size = MarkerRenderer.MarkerSizePx(moduleSize, quiet, modules.Size);
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
