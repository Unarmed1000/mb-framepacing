//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the capture source reported about a capture (captures.mbcd and frames.mbfc record flags).
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Data
{
  [Flags]
  public enum CaptureRecordFlags : uint
  {
    None = 0,

    /// <summary>The capture source (driver / ffmpeg) reported dropping frames between the previous record and this one.</summary>
    SourceDropBefore = 1,
  }
}
