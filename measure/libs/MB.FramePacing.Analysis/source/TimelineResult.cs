//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Result of TimelineAnalyzer: the capture period, every analysed run and the warnings.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Analysis
{
  public sealed record TimelineResult(long CapturePeriodTicks, IReadOnlyList<RunAnalysis> Runs, IReadOnlyList<string> Warnings);
}
