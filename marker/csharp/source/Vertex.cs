//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A pixel aligned vertex: X and Y lie on pixel corners (top-left origin, +y down). Luma is 0 (dark) or 255 (light); render it as the RGB
//* color (Luma, Luma, Luma).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FrameMarker
{
  public readonly struct Vertex : IEquatable<Vertex>
  {
    public Vertex(int x, int y, byte luma)
    {
      X = x;
      Y = y;
      Luma = luma;
    }

    public int X { get; }

    public int Y { get; }

    public byte Luma { get; }

    public bool Equals(Vertex other) => X == other.X && Y == other.Y && Luma == other.Luma;

    public override bool Equals(object obj) => obj is Vertex other && Equals(other);

    public override int GetHashCode() => unchecked((((X * 397) ^ Y) * 397) ^ Luma);

    public static bool operator ==(Vertex left, Vertex right) => left.Equals(right);

    public static bool operator !=(Vertex left, Vertex right) => !left.Equals(right);

    public override string ToString() => $"{{{X},{Y} luma {Luma}}}";
  }
}
