//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The outcome of a capture run: where it was written, the session info and the recorder statistics.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture
{
  /// <param name="DataPath">captures.mbcd: every captured frame's decoded markers.</param>
  /// <param name="FramesPath">frames.mbfc, when the frames themselves were stored (<see cref="CaptureRunOptions.KeepFrames"/>), else null.</param>
  public sealed record CaptureResult(string Directory, string DataPath, string? FramesPath, CaptureSessionInfo Session);
}
