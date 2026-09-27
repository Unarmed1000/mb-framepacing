//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The phases of a capture run: waiting for the start marker, recording, stopping and finished.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture
{
  public enum CapturePhase
  {
    WaitingForStart,
    Recording,
    Stopping,
    Finished,
  }
}
