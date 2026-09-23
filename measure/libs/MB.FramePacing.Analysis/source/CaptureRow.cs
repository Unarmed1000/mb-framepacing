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
  public enum CaptureStatus
  {
    /// <summary>The marker was read.</summary>
    Decoded,

    /// <summary>No marker could be read (frame changed during capture, blended, damaged).</summary>
    Undecodable,

    /// <summary>The markers at different heights of the frame disagree (the capture shows parts of two frames).</summary>
    Torn,

    /// <summary>The capture card delivered this frame but the recorder had to drop it (ring full). There is no image.</summary>
    NotRecorded,
  }

  /// <param name="CaptureIndex">The capture card's frame counter. Unrelated to <see cref="MarkerPayload.FrameIndex"/>.</param>
  /// <param name="CaptureTicks">The capture time used for analysis (device or host clock, TimeSpan ticks). Unknown for NotRecorded rows.</param>
  public readonly record struct CaptureRow(
    long CaptureIndex,
    long CaptureTicks,
    CaptureStatus Status,
    MarkerPayload Payload,
    StartMetadata? Start = null,
    bool SourceDropBefore = false
  )
  {
    public bool IsDecoded => Status == CaptureStatus.Decoded;
  }
}
