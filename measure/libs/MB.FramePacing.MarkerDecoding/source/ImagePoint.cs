//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A subpixel image position. Pixel (x, y) covers [x, x+1) x [y, y+1), so its centre is (x + 0.5, y + 0.5).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Globalization;

namespace MB.FramePacing.MarkerDecoding
{
  /// <summary>A subpixel image position. Pixel (x, y) covers [x, x+1) x [y, y+1), so its centre is (x + 0.5, y + 0.5).</summary>
  public readonly record struct ImagePoint(double X, double Y)
  {
    public static double Distance(ImagePoint lhs, ImagePoint rhs) =>
      Math.Sqrt(((lhs.X - rhs.X) * (lhs.X - rhs.X)) + ((lhs.Y - rhs.Y) * (lhs.Y - rhs.Y)));

    public ImagePoint Offset(double dx, double dy) => new ImagePoint(X + dx, Y + dy);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{X:0.###},{Y:0.###}");
  }
}
