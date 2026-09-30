//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Extra data carried by a SequenceStart marker: the wall clock start time and the sequence id.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  public readonly struct StartMetadata
  {
    public StartMetadata(long utcTicks, SequenceId sequenceId)
    {
      UtcTicks = utcTicks;
      SequenceId = sequenceId;
    }

    /// <summary>Wall clock start time as DateTime UTC ticks (100 ns since 0001-01-01), 0 = unknown.</summary>
    public long UtcTicks { get; }

    /// <summary>Identifies the capture sequence: 16 opaque bytes, any content as long as it is unique to it.</summary>
    public SequenceId SequenceId { get; }

    /// <summary>Metadata for a run starting at <paramref name="startTime"/> (converted to UTC).</summary>
    public static StartMetadata Create(DateTime startTime, SequenceId sequenceId) =>
      new StartMetadata(FrameMarker.ToDateTimeTicks(startTime), sequenceId);
  }
}
