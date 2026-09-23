//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A pixel position: origin at the top-left corner, +x to the right, +y down.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FrameMarker
{
  public readonly struct Point : IEquatable<Point>
  {
    public Point(int x, int y)
    {
      X = x;
      Y = y;
    }

    public int X { get; }

    public int Y { get; }

    public bool Equals(Point other) => X == other.X && Y == other.Y;

    public override bool Equals(object obj) => obj is Point other && Equals(other);

    public override int GetHashCode() => unchecked((X * 397) ^ Y);

    public static bool operator ==(Point left, Point right) => left.Equals(right);

    public static bool operator !=(Point left, Point right) => !left.Equals(right);

    public override string ToString() => $"{{{X},{Y}}}";
  }
}
