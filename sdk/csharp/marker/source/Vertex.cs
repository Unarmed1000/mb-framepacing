//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A pixel aligned vertex: X and Y lie on pixel corners (top-left origin, +y down). Luma is 0 (dark) or 255 (light); render it as the RGB
//* color (Luma, Luma, Luma).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  public readonly struct Vertex : IEquatable<Vertex>
  {
    public Vertex(int x, int y, byte luma)
    {
      X = x;
      Y = y;
      Luma = luma;
    }

    public readonly int X;

    public readonly int Y;

    public readonly byte Luma;

    public bool Equals(Vertex other) => X == other.X && Y == other.Y && Luma == other.Luma;

    public override bool Equals(object obj) => obj is Vertex other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Luma);

    public static bool operator ==(Vertex left, Vertex right) => left.Equals(right);

    public static bool operator !=(Vertex left, Vertex right) => !left.Equals(right);

    public override string ToString() => $"{{{X},{Y} luma {Luma}}}";
  }
}
