//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One bin of a summary.json histogram.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Data
{
  public sealed record SummaryHistogramBin(double CenterMs, long Count);
}
