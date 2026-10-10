//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL (measure/doc/perfetto.md). The augmented traces of an analysis that joined a Perfetto trace: for each run that was joined,
//* the recorded trace with the capture's measurements added as tracks, next to the run's reports (run-<id>-augmented.pftrace), to open in
//* Perfetto's trace viewer. Written when the charts are (--charts, the GUI's Save charts): a trace is copied whole, so not by every
//* analysis.
//*
//* An augmented trace holds everything the recorded one did: it names the device and its programs, and is as private as the recording.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MB.FramePacing.TraceJoin;

namespace MB.FramePacing.Analysis
{
  /// <summary>Experimental. Writes the augmented traces of an analysis.</summary>
  public static class AugmentedTraceFiles
  {
    /// <summary>
    /// Write the augmented trace of every run of <paramref name="report"/> that was joined with a trace, into the report's folder, and
    /// read each back with the trace processor at <paramref name="processorPath"/> (null: not read back). Nothing is written for an
    /// analysis without a join. A recorded trace that can not be read any more is a <see cref="TraceException"/>.
    /// </summary>
    public static IReadOnlyList<AugmentedTraceFile> Write(AnalysisReport report, string? processorPath, CancellationToken cancellationToken = default)
    {
      var files = new List<AugmentedTraceFile>();
      if (report.TraceJoins.Count == 0)
        return files;
      var prefixes = CaptureAnalyzer.RunFilePrefixes(report.Timeline.Runs);
      for (int index = 0; index < prefixes.Count; ++index)
      {
        if (report.TraceJoinOf(index) is not { } join)
          continue;
        cancellationToken.ThrowIfCancellationRequested();
        string path = Path.Combine(report.OutputDirectory, TraceAugmenter.FileName(prefixes[index]));
        var written = TraceAugmenter.Write(path, join);
        IReadOnlyList<string> problems =
          processorPath != null
            ? TraceAugmenter.Check(path, processorPath, join.Run.Name, written, join.Data.Errors, cancellationToken)
            : Array.Empty<string>();
        files.Add(new AugmentedTraceFile(path, TraceAugmenter.Total(written), processorPath != null, problems));
      }
      return files;
    }
  }
}
