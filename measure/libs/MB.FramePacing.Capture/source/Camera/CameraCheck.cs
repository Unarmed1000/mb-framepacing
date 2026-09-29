//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One result of calibrating or verifying a camera rig, such as "module size" or "exposure" (EXPERIMENTAL camera support).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture.Camera
{
  /// <summary>One result of calibrating or verifying a camera rig, such as "module size" or "exposure".</summary>
  public sealed record CameraCheck(string Name, CameraCheckLevel Level, string Message)
  {
    public override string ToString() => $"{Level.ToString().ToUpperInvariant(), -4} {Name}: {Message}";
  }
}
