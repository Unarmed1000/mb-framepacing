//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One line of captures.csv (doc/analysis-output-format.md): one capture, as the analysis read it. Times are 100 ns ticks; null is an empty
//* cell.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Data
{
  /// <param name="CaptureTime">When the capture was taken (the analysis's clock); null for a capture the recorder dropped.</param>
  /// <param name="Status">"Decoded", "Undecodable", "Torn" or "NotRecorded".</param>
  /// <param name="Kind">The main marker's kind ("Frame", "SequenceStart", "SequenceEnd", "Sync"), when one was read.</param>
  /// <param name="Payload">The main marker's encoded bytes, when one was read.</param>
  /// <param name="SourceDropsBefore">How many frames the capture source reported dropping before this capture.</param>
  /// <param name="MissedBefore">
  /// How many refreshes the capture timestamps say were missed since the previous capture (device clock only; 0 on the host clock).
  /// </param>
  /// <param name="SyncRunId">The sync marker's run id, when it was read (a capture card's tearing check, a camera's second zone).</param>
  /// <param name="SyncFrameIndex">The sync marker's frame index, when it was read.</param>
  public sealed record CaptureCsvRow(
    long CaptureIndex,
    TickCount64? CaptureTime,
    string Status,
    string? Kind,
    uint? RunId,
    ulong? FrameIndex,
    TimeSpan? AnimationTime,
    long SourceDropsBefore,
    long MissedBefore,
    uint? SyncRunId,
    ulong? SyncFrameIndex,
    TickCount64? HostTime,
    TickCount64? DeviceTime,
    byte[]? Payload
  );
}
