//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* FrameRecorder statistics: frames captured, written, dropped and discarded while armed, bytes written and ring fill.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture
{
  public readonly record struct FrameRecorderStats(
    long FramesCaptured,
    long FramesWritten,
    long FramesDropped,
    long FramesDiscardedWhileArmed,
    long BytesWritten,
    int RingFill,
    int RingCapacity,
    bool IsArmed
  );
}
