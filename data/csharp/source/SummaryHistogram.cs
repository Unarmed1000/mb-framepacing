//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One histogram of summary.json: bin k covers [(k - 0.5) * width, (k + 0.5) * width) and is listed by its center.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Data
{
  public sealed record SummaryHistogram(double BinWidthMs, long Total, IReadOnlyList<SummaryHistogramBin> Bins);
}
