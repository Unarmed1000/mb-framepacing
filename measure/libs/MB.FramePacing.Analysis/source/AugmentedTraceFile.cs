//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* An augmented trace that was written for a run (AugmentedTraceFiles), and how it read back.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Analysis
{
  /// <summary>Experimental. A run's augmented trace: the recorded Perfetto trace with the capture's measurements added as tracks.</summary>
  /// <param name="Events">How many events of the run were added.</param>
  /// <param name="Checked">The file was read back with the trace processor.</param>
  /// <param name="Problems">What did not read back as it was written; empty when all did, or when it was not read back.</param>
  public sealed record AugmentedTraceFile(string Path, long Events, bool Checked, IReadOnlyList<string> Problems);
}
