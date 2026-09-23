//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a marker means. Frame markers are drawn every frame of a test run; the sequence markers bracket the run.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Marker
{
  /// <summary>What a marker means. Frame markers are drawn every frame of a test run; the sequence markers bracket the run.</summary>
  public enum MarkerKind : byte
  {
    Frame = 0,
    SequenceStart = 1,
    SequenceEnd = 2,
  }
}
