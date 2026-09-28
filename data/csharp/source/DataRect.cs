//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* An integer pixel rectangle of the capture data: [X, X+Width) x [Y, Y+Height).
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Data
{
  public readonly record struct DataRect(int X, int Y, int Width, int Height)
  {
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>"x,y,width,height", as summary.json writes a marker's bounds.</summary>
    public override string ToString() => FormattableString.Invariant($"{X},{Y},{Width},{Height}");
  }
}
