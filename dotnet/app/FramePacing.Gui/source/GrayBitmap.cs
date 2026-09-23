//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Converts Gray8 capture frames to Avalonia bitmaps for the live preview.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Gui
{
  public static class GrayBitmap
  {
    /// <summary>Snapshot a Gray8 frame as BGRA (the frame is reused by the capture thread, so it is copied here).</summary>
    public static int[] ToBgra(GrayImage image)
    {
      var pixels = new int[image.Width * image.Height];
      for (int y = 0; y < image.Height; ++y)
      {
        var row = image.Row(y);
        int offset = y * image.Width;
        for (int x = 0; x < row.Length; ++x)
        {
          int luma = row[x];
          pixels[offset + x] = unchecked((int)0xFF000000) | (luma << 16) | (luma << 8) | luma;
        }
      }
      return pixels;
    }

    /// <summary>Create (or reuse, when the size matches) a bitmap and copy the BGRA pixels into it. Must run on the UI thread.</summary>
    public static WriteableBitmap Update(WriteableBitmap? bitmap, int[] pixels, int width, int height)
    {
      if (bitmap == null || bitmap.PixelSize.Width != width || bitmap.PixelSize.Height != height)
        bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
      using var buffer = bitmap.Lock();
      for (int y = 0; y < height; ++y)
        Marshal.Copy(pixels, y * width, buffer.Address + (y * buffer.RowBytes), width);
      return bitmap;
    }
  }
}
