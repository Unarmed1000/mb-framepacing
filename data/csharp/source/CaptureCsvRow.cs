//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One line of captures.csv (doc/analysis-output-format.md): one capture, as the analysis read it. Times are 100 ns ticks; null is an empty
//* cell.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Data
{
  /// <param name="CaptureTicks">When the capture was taken (the analysis's clock); null for a capture the recorder dropped.</param>
  /// <param name="Status">"Decoded", "Undecodable", "Torn" or "NotRecorded".</param>
  /// <param name="Kind">The main marker's kind ("Frame", "SequenceStart", "SequenceEnd", "Sync"), when one was read.</param>
  /// <param name="Payload">The main marker's encoded bytes, when one was read.</param>
  /// <param name="SecondZoneFrameIndex">EXPERIMENTAL camera captures: the frame index the second zone showed.</param>
  public sealed record CaptureCsvRow(
    long CaptureIndex,
    long? CaptureTicks,
    string Status,
    string? Kind,
    uint? RunId,
    ulong? FrameIndex,
    long? AnimationTicks,
    bool SourceDropBefore,
    long? HostTicks,
    long? DeviceTicks,
    byte[]? Payload,
    ulong? SecondZoneFrameIndex = null
  );
}
