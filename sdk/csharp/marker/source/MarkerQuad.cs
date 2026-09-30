//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A rectangle of the marker to fill (FrameMarker.ModulesToQuads): its pixels, and its colour.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  public readonly struct MarkerQuad : IEquatable<MarkerQuad>
  {
    public MarkerQuad(Rectangle rect, bool dark)
    {
      Rect = rect;
      Dark = dark;
    }

    public readonly Rectangle Rect;

    /// <summary>True: draw black (luma 0). False: draw white (luma 255).</summary>
    public readonly bool Dark;

    public bool Equals(MarkerQuad other) => Rect == other.Rect && Dark == other.Dark;

    public override bool Equals(object obj) => obj is MarkerQuad other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Rect, Dark);

    public static bool operator ==(MarkerQuad left, MarkerQuad right) => left.Equals(right);

    public static bool operator !=(MarkerQuad left, MarkerQuad right) => !left.Equals(right);

    public override string ToString() => $"{{{Rect} {(Dark ? "dark" : "light")}}}";
  }
}
