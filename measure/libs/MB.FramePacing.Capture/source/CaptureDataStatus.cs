//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What was read from one captured frame's markers, as stored in captures.mbcd.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture
{
  public enum CaptureDataStatus : byte
  {
    /// <summary>No marker could be read (the frame changed during capture, blended, damaged, or no marker yet).</summary>
    Undecodable = 0,

    /// <summary>The main marker was read.</summary>
    Decoded = 1,

    /// <summary>The markers at different heights of the frame disagree (the capture shows parts of two frames).</summary>
    Torn = 2,
  }
}
