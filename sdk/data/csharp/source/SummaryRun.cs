//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* summary.json's runs[]: one measured run (from a start marker to an end marker, or the frames of one run id).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;

namespace MB.FramePacing.Data
{
  /// <param name="Name">The name the user gave the capture (capture.json, or the analysis), else null.</param>
  /// <param name="SequenceId">The start marker's sequence id, as text or UUID, else null.</param>
  /// <param name="FramesFile">The run's frames CSV, next to summary.json.</param>
  /// <param name="Pacing">How the run was paced; null in output written before pacing was measured.</param>
  /// <param name="Camera">EXPERIMENTAL camera captures only.</param>
  public sealed record SummaryRun(
    uint RunId,
    string? Name,
    string? SequenceId,
    DateTime? StartTimeUtc,
    bool HasStartMarker,
    bool HasEndMarker,
    string FramesFile,
    SummaryCounts Counts,
    SummaryStatistics Statistics,
    SummaryPacing? Pacing,
    SummaryHistograms? Histograms,
    SummaryCamera? Camera,
    IReadOnlyList<string>? Warnings
  );
}
