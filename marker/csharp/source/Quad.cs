//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Axis aligned rectangle covering the pixels [Left,Right) x [Top,Bottom). Every edge lies on an integer pixel edge.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FrameMarker
{
  public readonly struct Quad : IEquatable<Quad>
  {
    public Quad(int left, int top, int right, int bottom, bool dark)
    {
      Left = left;
      Top = top;
      Right = right;
      Bottom = bottom;
      Dark = dark;
    }

    public int Left { get; }

    public int Top { get; }

    public int Right { get; }

    public int Bottom { get; }

    /// <summary>True: draw black (luma 0). False: draw white (luma 255).</summary>
    public bool Dark { get; }

    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool Equals(Quad other) => Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom && Dark == other.Dark;

    public override bool Equals(object obj) => obj is Quad other && Equals(other);

    public override int GetHashCode() => unchecked((((((((Left * 397) ^ Top) * 397) ^ Right) * 397) ^ Bottom) * 397) ^ (Dark ? 1 : 0));

    public static bool operator ==(Quad left, Quad right) => left.Equals(right);

    public static bool operator !=(Quad left, Quad right) => !left.Equals(right);

    public override string ToString() => $"{{{Left},{Top},{Right},{Bottom} {(Dark ? "dark" : "light")}}}";
  }
}
