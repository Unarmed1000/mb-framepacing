//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a marker means. Frame markers are drawn every frame of a test run; the sequence markers bracket the run.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.MarkerDecoding
{
  /// <summary>What a marker means. Frame markers are drawn every frame of a test run; the sequence markers bracket the run.</summary>
  public enum MarkerKind : byte
  {
    Frame = 0,
    SequenceStart = 1,
    SequenceEnd = 2,

    /// <summary>The small second marker for tearing checks and camera timing: it only carries the frame index.</summary>
    Sync = 3,
  }
}
