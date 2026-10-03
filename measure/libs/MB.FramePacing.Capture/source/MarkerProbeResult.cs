//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Where a probe found the markers in the source's frames: the main marker, and the sync marker when the source shows one.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Capture
{
  /// <param name="Main">The main marker (frame, start or end), as a frame marker's lock.</param>
  /// <param name="Sync">The sync marker; null when the frames with the main marker showed none.</param>
  public readonly record struct MarkerProbeResult(MarkerLock Main, MarkerLock? Sync);
}
