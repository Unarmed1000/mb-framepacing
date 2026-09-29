//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The phases of a capture run: waiting for the start marker, recording, stopping and finished.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
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
