//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The statistics of one quantity in summary.json, in milliseconds: percentiles by linear interpolation between the closest ranks.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.Text.Json.Serialization;

namespace MB.FramePacing.Data
{
  /// <param name="Count">How many values; 0 = none (every other field is then 0).</param>
  /// <param name="StdDev">The sample standard deviation.</param>
  public sealed record ValueStatistics(
    [property: JsonRequired] int Count,
    [property: JsonRequired] double Min,
    [property: JsonRequired] double Mean,
    [property: JsonRequired] double StdDev,
    [property: JsonRequired] double P50,
    [property: JsonRequired] double P95,
    [property: JsonRequired] double P99,
    double P999,
    [property: JsonRequired] double Max
  )
  {
    public static readonly ValueStatistics Empty = new ValueStatistics(0, 0, 0, 0, 0, 0, 0, 0, 0);
  }
}
