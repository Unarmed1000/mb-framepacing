//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Axis aligned rectangle to fill, covering the pixels [X, X + Width) x [Y, Y + Height): [Left,Right) x [Top,Bottom). Every edge lies on an
//* integer pixel edge.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  public readonly struct QuadRect : IEquatable<QuadRect>
  {
    public QuadRect(int x, int y, int width, int height, bool dark)
    {
      X = x;
      Y = y;
      Width = width;
      Height = height;
      Dark = dark;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>True: draw black (luma 0). False: draw white (luma 255).</summary>
    public bool Dark { get; }

    public int Left => X;

    public int Top => Y;

    /// <summary>The first pixel column right of the rectangle.</summary>
    public int Right => X + Width;

    /// <summary>The first pixel row below the rectangle.</summary>
    public int Bottom => Y + Height;

    public bool Equals(QuadRect other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height && Dark == other.Dark;

    public override bool Equals(object obj) => obj is QuadRect other && Equals(other);

    public override int GetHashCode() => unchecked((((((((X * 397) ^ Y) * 397) ^ Width) * 397) ^ Height) * 397) ^ (Dark ? 1 : 0));

    public static bool operator ==(QuadRect left, QuadRect right) => left.Equals(right);

    public static bool operator !=(QuadRect left, QuadRect right) => !left.Equals(right);

    public override string ToString() => $"{{{X},{Y} {Width}x{Height} {(Dark ? "dark" : "light")}}}";
  }
}
