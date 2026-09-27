//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a marker means. Frame markers are drawn every frame of a test run; the sequence markers bracket the run so the analyzer can cut the
//* capture to exactly the measured window. See doc/marker-format.md "Test sequences".
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FrameMarker
{
  public enum MarkerKind : byte
  {
    Frame = 0,
    SequenceStart = 1,
    SequenceEnd = 2,

    /// <summary>The small second marker for tearing checks and camera timing: it only carries the frame index.</summary>
    Sync = 3,
  }
}
