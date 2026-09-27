//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Resolves device timestamps that arrive after the pixels (for example ffmpeg's showinfo lines on stderr).
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture
{
  /// <summary>Resolves device timestamps that arrive after the pixels (for example ffmpeg's showinfo lines on stderr).</summary>
  public interface IDeviceTimestampSource
  {
    bool TryGetDeviceTicks(long captureIndex, out long deviceTicks);
  }
}
