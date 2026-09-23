//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Minimal 8 bit grayscale image (luma only - all the QR decoder needs) and an integer pixel rectangle.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  /// <summary>Integer pixel rectangle covering [X, X+Width) x [Y, Y+Height).</summary>
  public readonly record struct PixelRect(int X, int Y, int Width, int Height)
  {
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public PixelRect Intersect(PixelRect other)
    {
      int left = Math.Max(X, other.X);
      int top = Math.Max(Y, other.Y);
      int right = Math.Min(Right, other.Right);
      int bottom = Math.Min(Bottom, other.Bottom);
      return right > left && bottom > top ? new PixelRect(left, top, right - left, bottom - top) : default;
    }

    public PixelRect Inflate(int amount) => new PixelRect(X - amount, Y - amount, Width + (2 * amount), Height + (2 * amount));

    public override string ToString() => $"{X},{Y},{Width},{Height}";

    /// <summary>Parse "x,y,w,h".</summary>
    public static PixelRect Parse(string text)
    {
      var parts = text.Split(',');
      if (
        parts.Length != 4
        || !int.TryParse(parts[0], out int x)
        || !int.TryParse(parts[1], out int y)
        || !int.TryParse(parts[2], out int w)
        || !int.TryParse(parts[3], out int h)
        || w <= 0
        || h <= 0
      )
        throw new FormatException($"Expected a rectangle as 'x,y,width,height' but got '{text}'");
      return new PixelRect(x, y, w, h);
    }
  }

  /// <summary>An 8 bit grayscale image. Rows are <see cref="Stride"/> bytes apart.</summary>
  public sealed class GrayImage
  {
    public GrayImage(int width, int height, byte fill = 0)
      : this(width, height, width, new byte[checked(width * height)])
    {
      if (fill != 0)
        Array.Fill(Pixels, fill);
    }

    public GrayImage(int width, int height, int stride, byte[] pixels)
    {
      if (width <= 0 || height <= 0)
        throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive");
      if (stride < width)
        throw new ArgumentOutOfRangeException(nameof(stride), "Stride must be at least the width");
      if (pixels.Length < ((long)stride * (height - 1)) + width)
        throw new ArgumentException("Pixel buffer is too small", nameof(pixels));
      Width = width;
      Height = height;
      Stride = stride;
      Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public byte[] Pixels { get; }

    public PixelRect Bounds => new PixelRect(0, 0, Width, Height);

    public byte this[int x, int y]
    {
      get => Pixels[(y * Stride) + x];
      set => Pixels[(y * Stride) + x] = value;
    }

    public Span<byte> Row(int y) => Pixels.AsSpan(y * Stride, Width);

    public void FillRect(PixelRect rect, byte value)
    {
      var clipped = rect.Intersect(Bounds);
      for (int y = clipped.Y; y < clipped.Bottom; ++y)
        Pixels.AsSpan((y * Stride) + clipped.X, clipped.Width).Fill(value);
    }

    /// <summary>Area-average downscale by an integer factor (what a good capture scaler does for integer ratios).</summary>
    public GrayImage DownscaleBox(int factor)
    {
      if (factor < 1)
        throw new ArgumentOutOfRangeException(nameof(factor));
      if (factor == 1)
        return this;
      int width = Width / factor;
      int height = Height / factor;
      var result = new GrayImage(width, height);
      int area = factor * factor;
      for (int y = 0; y < height; ++y)
      {
        for (int x = 0; x < width; ++x)
        {
          int sum = 0;
          for (int sy = 0; sy < factor; ++sy)
          {
            int rowOffset = (((y * factor) + sy) * Stride) + (x * factor);
            for (int sx = 0; sx < factor; ++sx)
              sum += Pixels[rowOffset + sx];
          }
          result[x, y] = (byte)((sum + (area / 2)) / area);
        }
      }
      return result;
    }

    /// <summary>Bilinear resample to an arbitrary size (models non-integer capture scaling).</summary>
    public GrayImage ResizeBilinear(int width, int height)
    {
      var result = new GrayImage(width, height);
      double scaleX = (double)Width / width;
      double scaleY = (double)Height / height;
      for (int y = 0; y < height; ++y)
      {
        double srcY = Math.Clamp(((y + 0.5) * scaleY) - 0.5, 0, Height - 1);
        int y0 = (int)srcY;
        int y1 = Math.Min(y0 + 1, Height - 1);
        double fy = srcY - y0;
        for (int x = 0; x < width; ++x)
        {
          double srcX = Math.Clamp(((x + 0.5) * scaleX) - 0.5, 0, Width - 1);
          int x0 = (int)srcX;
          int x1 = Math.Min(x0 + 1, Width - 1);
          double fx = srcX - x0;
          double top = (this[x0, y0] * (1 - fx)) + (this[x1, y0] * fx);
          double bottom = (this[x0, y1] * (1 - fx)) + (this[x1, y1] * fx);
          result[x, y] = (byte)Math.Round((top * (1 - fy)) + (bottom * fy));
        }
      }
      return result;
    }
  }
}
