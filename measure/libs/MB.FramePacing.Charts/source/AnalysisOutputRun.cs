//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One run read back from an analysis output folder: what the charts are drawn from, and the prefix its report files are named with.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  /// <param name="Chart">The run and its capture's period, error threshold and kind.</param>
  /// <param name="FilePrefix">The run's report file prefix (run-&lt;id&gt;[-&lt;n&gt;]), as its frames CSV is named.</param>
  public sealed record AnalysisOutputRun(ChartRun Chart, string FilePrefix);
}
