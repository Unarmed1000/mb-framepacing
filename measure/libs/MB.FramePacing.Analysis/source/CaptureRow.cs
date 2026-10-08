//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One row per capture index: what the capture card delivered at that instant and what marker (if any) was read from it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Analysis
{
  /// <param name="CaptureIndex">The capture card's frame counter. Unrelated to <see cref="MarkerPayload.FrameIndex"/>.</param>
  /// <param name="Sync">
  /// The sync marker, if it decoded: the run id and frame index of its frame. A capture card's tearing check (already in the status); an
  /// EXPERIMENTAL camera's second (lower) zone, which times the frames.
  /// </param>
  /// <param name="SourceDrops">How many frames the capture source reported dropping before this capture.</param>
  /// <param name="CaptureTime">The capture time used for analysis (device or host clock). Unknown (default) for NotRecorded rows.</param>
  public readonly record struct CaptureRow(
    long CaptureIndex,
    NanosecondTickCount CaptureTime,
    CaptureStatus Status,
    MarkerPayload Payload,
    StartMetadata? Start = null,
    uint SourceDrops = 0,
    MarkerPayload? Sync = null
  )
  {
    public bool IsDecoded => Status == CaptureStatus.Decoded;

    /// <summary>The capture data's host clock timestamp, when the row came from it.</summary>
    public NanosecondTickCount? HostTime { get; init; }

    /// <summary>The capture data's device clock timestamp, when the row came from it and the device gave one.</summary>
    public NanosecondTickCount? DeviceTime { get; init; }

    /// <summary>
    /// How many refreshes the capture's device clock says were missed since the previous capture (<see cref="MissedCaptures"/>); 0 on the
    /// host clock.
    /// </summary>
    public long MissedBefore { get; init; }

    /// <summary>The main marker's encoded bytes as read, when the row came from the capture data.</summary>
    public byte[]? MarkerBytes { get; init; }
  }
}
