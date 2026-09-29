//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One row per capture index: what the capture card delivered at that instant and what marker (if any) was read from it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using MB.FramePacing.Marker;

namespace MB.FramePacing.Analysis
{
  /// <param name="CaptureIndex">The capture card's frame counter. Unrelated to <see cref="MarkerPayload.FrameIndex"/>.</param>
  /// <param name="SecondaryFrameIndex">EXPERIMENTAL camera captures: the frame index the second (lower) zone shows, if it decoded.</param>
  /// <param name="CaptureTicks">The capture time used for analysis (device or host clock, TimeSpan ticks). Unknown for NotRecorded rows.</param>
  public readonly record struct CaptureRow(
    long CaptureIndex,
    long CaptureTicks,
    CaptureStatus Status,
    MarkerPayload Payload,
    StartMetadata? Start = null,
    bool SourceDropBefore = false,
    ulong? SecondaryFrameIndex = null
  )
  {
    public bool IsDecoded => Status == CaptureStatus.Decoded;

    /// <summary>The capture data's host clock timestamp (TimeSpan ticks), when the row came from it.</summary>
    public long? HostTicks { get; init; }

    /// <summary>The capture data's device clock timestamp (TimeSpan ticks), when the row came from it and the device gave one.</summary>
    public long? DeviceTicks { get; init; }

    /// <summary>The main marker's encoded bytes as read, when the row came from the capture data.</summary>
    public byte[]? MarkerBytes { get; init; }
  }
}
