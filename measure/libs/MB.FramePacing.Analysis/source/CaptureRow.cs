//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One row per capture index: what the capture card delivered at that instant and what marker (if any) was read from it.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using MB.FramePacing.Marker;

namespace MB.FramePacing.Analysis
{
  /// <param name="CaptureIndex">The capture card's frame counter. Unrelated to <see cref="MarkerPayload.FrameIndex"/>.</param>
  /// <param name="SecondaryFrameIndex">EXPERIMENTAL camera captures: the frame index the second (lower) zone shows, if it decoded.</param>
  /// <param name="CaptureTicks">The capture time used for analysis (device or host clock, TimeSpan ticks). Unknown for NotRecorded rows.</param>
  public readonly record struct CaptureRow(
    long CaptureIndex,
    long CaptureTicks,
    CaptureStatus Status,
    MarkerPayload Payload,
    StartMetadata? Start = null,
    bool SourceDropBefore = false,
    ulong? SecondaryFrameIndex = null
  )
  {
    public bool IsDecoded => Status == CaptureStatus.Decoded;
  }
}
