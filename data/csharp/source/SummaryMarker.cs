//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* summary.json's markers[]: where a marker was found in the stored frames.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Data
{
  /// <param name="Bounds">"x,y,width,height" in stored pixels, including the quiet zone.</param>
  public sealed record SummaryMarker(string Bounds, double ModuleSizePx);
}
