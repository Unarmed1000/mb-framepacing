//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads an analysis output folder back (summary.json and every run's run-<id>-frames.csv), so reports can be drawn from what an analysis
//* wrote without the capture: the runs, their pacing, statistics and counts, and every presented frame. The CSV's milliseconds have four
//* decimals, which is whole 100 ns ticks, so the frames come back to the tick.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MB.FramePacing.Analysis;
using MB.FramePacing.Data;

namespace MB.FramePacing.Charts
{
  public static class AnalysisOutput
  {
    /// <summary>The analysis output folder of <paramref name="folder"/>: the folder itself, or the capture folder's analysis folder.</summary>
    public static string Directory(string folder) =>
      AnalysisFiles.Find(folder)
      ?? throw new FileNotFoundException(
        $"'{folder}' holds no analysis ({AnalysisFiles.SummaryFileName}, or {AnalysisFiles.DirectoryName}/{AnalysisFiles.SummaryFileName}): "
          + "run 'analyze' first"
      );

    /// <summary>Every run of the analysis in <paramref name="folder"/> (an analysis output folder, or a capture folder that has one).</summary>
    public static IReadOnlyList<AnalysisOutputRun> Read(string folder)
    {
      string directory = Directory(folder);
      var summary = AnalysisSummary.Read(Path.Combine(directory, AnalysisFiles.SummaryFileName));
      long capturePeriod = Milliseconds.ToTicks(summary.CapturePeriodMs);
      long threshold = Milliseconds.ToTicks(summary.ErrorThresholdMs);
      bool camera = summary.Scanout == nameof(ScanoutModel.Camera);
      // What the capture missed, for every run's events (an analysis without the file draws its capture lane as not known)
      string capturesPath = Path.Combine(directory, AnalysisFiles.CapturesFileName);
      var captures = File.Exists(capturesPath) ? CapturesCsv.Read(capturesPath) : null;

      var runs = new List<AnalysisOutputRun>();
      foreach (var run in summary.Runs)
      {
        var frames = ReadFrames(Path.Combine(directory, run.FramesFile), capturePeriod);
        var analysis = run.ToRun(frames);
        string prefix = run.FramesFile.EndsWith("-frames.csv", StringComparison.Ordinal)
          ? run.FramesFile[..^"-frames.csv".Length]
          : $"run-{analysis.RunId}";
        runs.Add(new AnalysisOutputRun(new ChartRun(analysis, capturePeriod, threshold, camera) { Captures = captures }, prefix));
      }
      return runs;
    }

    /// <summary>The presented frames of a run-&lt;id&gt;-frames.csv, by column name.</summary>
    public static IReadOnlyList<PresentedFrame> ReadFrames(string path, long capturePeriodTicks) =>
      FramesCsv.Read(path).Select(row => row.ToFrame(capturePeriodTicks)).ToList();
  }
}
