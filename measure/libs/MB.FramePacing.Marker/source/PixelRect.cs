//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Integer pixel rectangle covering [X, X+Width) x [Y, Y+Height).
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
}
