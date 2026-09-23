//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a marker means. Frame markers are drawn every frame of a test run; the sequence markers bracket the run so the analyzer can cut the
//* capture to exactly the measured window. See doc/marker-format.md "Test sequences".
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FrameMarker
{
  public enum MarkerKind : byte
  {
    Frame = 0,
    SequenceStart = 1,
    SequenceEnd = 2,
  }
}
