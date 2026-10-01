//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* An integer pixel rectangle covering [X, X + Width) x [Y, Y + Height): [Left, Right) x [Top, Bottom). Origin at the top-left corner, +x to
//* the right, +y down. Always valid: the constructor makes a negative width or height 0. Its edges must fit int (the C++ core's Rectangle).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing
{
  public readonly struct Rectangle : IEquatable<Rectangle>
  {
    public Rectangle(int x, int y, int width, int height)
    {
      X = x;
      Y = y;
      Width = Math.Max(width, 0);
      Height = Math.Max(height, 0);
    }

    /// <summary>The empty rectangle at (0, 0).</summary>
    public static Rectangle Empty => default;

    /// <summary>The rectangle between the edges: an edge before the opposite one gives a size of 0.</summary>
    public static Rectangle FromLeftTopRightBottom(int left, int top, int right, int bottom) => new Rectangle(left, top, right - left, bottom - top);

    public readonly int X;

    public readonly int Y;

    public readonly int Width;

    public readonly int Height;

    public int Left => X;

    public int Top => Y;

    /// <summary>The first pixel column right of the rectangle.</summary>
    public int Right => X + Width;

    /// <summary>The first pixel row below the rectangle.</summary>
    public int Bottom => Y + Height;

    /// <summary>No pixels: a width or height of 0.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>Whether the pixel (x, y) is inside.</summary>
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public void Deconstruct(out int x, out int y, out int width, out int height)
    {
      x = X;
      y = Y;
      width = Width;
      height = Height;
    }

    public bool Equals(Rectangle other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

    public override bool Equals(object obj) => obj is Rectangle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    public static bool operator ==(Rectangle left, Rectangle right) => left.Equals(right);

    public static bool operator !=(Rectangle left, Rectangle right) => !left.Equals(right);

    /// <summary>"x,y,width,height".</summary>
    public override string ToString() => FormattableString.Invariant($"{X},{Y},{Width},{Height}");
  }
}
