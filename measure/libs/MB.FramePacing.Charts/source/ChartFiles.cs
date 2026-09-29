//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Writes the charts of every run of an analysis next to its other reports, as SVG cards (the command line's --charts and the GUI's Save
//* charts): the whole run's report card and every distribution card.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.IO;
using System.Linq;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Charts
{
  public static class ChartFiles
  {
    /// <summary>
    /// Writes each run's charts into the report directory, named like its frames CSV (<see cref="CaptureAnalyzer.RunFilePrefix"/>):
    /// -report.svg (<see cref="ReportCard"/>) and -error-histogram.svg, -display-time-step-histogram.svg, -error-percentiles.svg and
    /// -drift.svg (<see cref="DistributionCard"/>). Returns the files written.
    /// </summary>
    public static IReadOnlyList<string> Write(AnalysisReport report)
    {
      var written = new List<string>();
      var ordinals = new Dictionary<uint, int>();
      var captures = ChartRun.CapturesOf(report);
      foreach (var run in report.Timeline.Runs)
      {
        int ordinal = ordinals.TryGetValue(run.RunId, out int seen) ? seen : 0;
        ordinals[run.RunId] = ordinal + 1;
        string prefix = CaptureAnalyzer.RunFilePrefix(run, ordinal);
        var chart = ChartRun.From(report, run, captures);
        written.AddRange(ReportFiles.Write(chart, prefix, report.OutputDirectory));
        written.AddRange(ReportFiles.WriteCards(chart, prefix, report.OutputDirectory, DistributionCard.All.Select(card => card.Id)));
      }
      return written;
    }
  }
}
