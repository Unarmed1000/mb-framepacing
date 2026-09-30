//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The statistics of one quantity in summary.json, in milliseconds: percentiles by linear interpolation between the closest ranks.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Data
{
  /// <param name="Count">How many values; 0 = none (every other field is then 0).</param>
  /// <param name="StdDev">The sample standard deviation.</param>
  public sealed record ValueStatistics(int Count, double Min, double Mean, double StdDev, double P50, double P95, double P99, double P999, double Max)
  {
    public static readonly ValueStatistics Empty = new ValueStatistics(0, 0, 0, 0, 0, 0, 0, 0, 0);
  }
}
