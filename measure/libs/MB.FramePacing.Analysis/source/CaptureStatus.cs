//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a capture row holds: a decoded marker, an unreadable marker, a torn marker (two frames in one capture) or a frame the recorder did not
//* store.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

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
}
