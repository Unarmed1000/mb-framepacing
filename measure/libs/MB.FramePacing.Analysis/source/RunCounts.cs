//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Capture and frame counts of one run: decoded, undecodable, torn and not recorded captures, presented and skipped frames, out of order
//* captures and segments.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  public sealed record RunCounts(
    long Captures,
    long Decoded,
    long Undecodable,
    long Torn,
    long NotRecorded,
    long SourceDropEvents,
    long PresentedFrames,
    long SkippedFrameIndices,
    long OutOfOrderCaptures,
    int Segments
  );
}
