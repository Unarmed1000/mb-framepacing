//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The outcome of a capture run: where it was written, the session info and the recorder statistics.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture
{
  public sealed record CaptureResult(string Directory, string FramesPath, CaptureSessionInfo Session);
}
