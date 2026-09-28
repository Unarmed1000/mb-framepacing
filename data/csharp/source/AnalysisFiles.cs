//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The file names of an analysis output folder (doc/analysis-output-format.md), and finding it next to a capture.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.IO;

namespace MB.FramePacing.Data
{
  public static class AnalysisFiles
  {
    /// <summary>The analysis output folder inside a capture folder.</summary>
    public const string DirectoryName = "analysis";

    public const string SummaryFileName = "summary.json";
    public const string CapturesFileName = "captures.csv";

    /// <summary>
    /// The start of every file of a run: "run-{id}", and "run-{id}-{n}" for the n-th run with the same id (<paramref name="ordinal"/> counts
    /// from 0 among the runs with that id).
    /// </summary>
    public static string RunFilePrefix(uint runId, int ordinal) => ordinal == 0 ? $"run-{runId}" : $"run-{runId}-{ordinal + 1}";

    /// <summary>A run's frames file: "run-{id}-frames.csv".</summary>
    public static string FramesFileName(uint runId, int ordinal) => RunFilePrefix(runId, ordinal) + "-frames.csv";

    /// <summary>The analysis output folder of <paramref name="folder"/>: the folder itself, or its analysis folder; null when neither holds one.</summary>
    public static string? Find(string folder)
    {
      if (File.Exists(Path.Combine(folder, SummaryFileName)))
        return folder;
      string analysis = Path.Combine(folder, DirectoryName);
      return File.Exists(Path.Combine(analysis, SummaryFileName)) ? analysis : null;
    }
  }
}
