//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A mode a device offers. Fps is the maximum for the size (0 if the platform does not report it).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Globalization;

namespace MB.FramePacing.Capture.Ffmpeg
{
  /// <summary>A mode a device offers. Fps is the maximum for the size (0 if the platform does not report it).</summary>
  /// <param name="Format">Pixel format or codec, e.g. "yuyv422", "nv12" or "mjpeg".</param>
  public sealed record CaptureMode(int Width, int Height, double Fps, string Format, bool IsCompressed)
  {
    public override string ToString() =>
      Fps > 0 ? string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}@{Fps:0.###} {Format}") : $"{Width}x{Height} {Format}";
  }
}
