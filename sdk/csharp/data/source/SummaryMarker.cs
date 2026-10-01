//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* summary.json's markers[]: where a marker was found in the stored frames.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.Text.Json.Serialization;

namespace MB.FramePacing.Data
{
  /// <param name="Bounds">"x,y,width,height" in stored pixels, including the quiet zone.</param>
  public sealed record SummaryMarker([property: JsonRequired] string Bounds, [property: JsonRequired] double ModuleSizePx);
}
