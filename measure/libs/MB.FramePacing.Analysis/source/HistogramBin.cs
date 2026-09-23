//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One histogram bin: its centre in milliseconds and how many values fell into it.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  public sealed record HistogramBin(double CenterMs, long Count);
}
