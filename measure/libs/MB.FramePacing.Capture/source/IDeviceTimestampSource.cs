//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Resolves device timestamps that arrive after the pixels (for example ffmpeg's showinfo lines on stderr).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture
{
  /// <summary>Resolves device timestamps that arrive after the pixels (for example ffmpeg's showinfo lines on stderr).</summary>
  public interface IDeviceTimestampSource
  {
    bool TryGetDeviceTime(long captureIndex, out TickCount64 deviceTime);
  }
}
