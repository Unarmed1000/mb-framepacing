//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One histogram bin: its centre in milliseconds and how many values fell into it.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  public sealed record HistogramBin(double CenterMs, long Count);
}
