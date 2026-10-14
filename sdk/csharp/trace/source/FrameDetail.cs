//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What of a frame a slice or a moment of the frame record's second part is (sdk/doc/trace-frame-record.md, part 2: optional, not checked
//* against the capture). Its name is the detail's prefix and the frame's ids (FrameDetailName).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Trace
{
  /// <summary>What of a frame a slice or a moment of the frame record's second part is.</summary>
  public enum FrameDetail : byte
  {
    /// <summary>A wait before the frame's CPU work (<see cref="FrameRecordText.WaitNamePrefix"/>, on WaitTrackName).</summary>
    Wait = 1,

    /// <summary>The frame's present call (<see cref="FrameRecordText.PresentNamePrefix"/>, on PresentTrackName).</summary>
    Present = 2,

    /// <summary>The GPU's work on the frame (<see cref="FrameRecordText.GpuNamePrefix"/>, on GpuTrackName).</summary>
    Gpu = 3,

    /// <summary>A moment: when the platform says the frame was shown (<see cref="FrameRecordText.DisplayNamePrefix"/>, on DisplayTrackName).</summary>
    Display = 4,

    /// <summary>A moment: a frame that was made and never presented (<see cref="FrameRecordText.NotPresentedNamePrefix"/>, on RunTrackName).</summary>
    NotPresented = 5,
  }
}
