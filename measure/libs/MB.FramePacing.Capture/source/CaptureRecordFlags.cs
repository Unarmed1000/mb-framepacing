//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Flags stored with every record of a .mbfc capture file, for example that the source dropped frames before it.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Capture
{
  [Flags]
  public enum CaptureRecordFlags : uint
  {
    None = 0,

    /// <summary>The capture source (driver / ffmpeg) reported dropping frames between the previous record and this one.</summary>
    SourceDropBefore = 1,
  }
}
