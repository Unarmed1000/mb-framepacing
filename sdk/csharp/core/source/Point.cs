//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A pixel position: origin at the top-left corner, +x to the right, +y down.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing
{
  public readonly struct Point : IEquatable<Point>
  {
    public Point(int x, int y)
    {
      X = x;
      Y = y;
    }

    public readonly int X;

    public readonly int Y;

    public bool Equals(Point other) => X == other.X && Y == other.Y;

    public override bool Equals(object obj) => obj is Point other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(Point left, Point right) => left.Equals(right);

    public static bool operator !=(Point left, Point right) => !left.Equals(right);

    public override string ToString() => $"{{{X},{Y}}}";
  }
}
