//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Extra data carried by a SequenceStart marker.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using FM = MB.FrameMarker;

namespace MB.FramePacing.Marker
{
  /// <summary>Extra data carried by a <see cref="MarkerKind.SequenceStart"/> marker.</summary>
  /// <param name="UtcTicks">Wall clock start time as <see cref="DateTime"/> UTC ticks, 0 = unknown.</param>
  /// <param name="SequenceId">Identifies the capture sequence: 16 opaque bytes, any content unique to it (shown as text or hex).</param>
  public sealed record StartMetadata(long UtcTicks, FM.SequenceId SequenceId)
  {
    public static readonly StartMetadata Empty = new StartMetadata(0, default);

    public DateTime? StartTimeUtc => UtcTicks > 0 && UtcTicks <= DateTime.MaxValue.Ticks ? new DateTime(UtcTicks, DateTimeKind.Utc) : null;

    public static StartMetadata Create(DateTime startTimeUtc, FM.SequenceId sequenceId) =>
      new StartMetadata(startTimeUtc.ToUniversalTime().Ticks, sequenceId);

    /// <summary>Metadata whose sequence id is a text tag of at most 16 printable ASCII characters.</summary>
    public static StartMetadata FromTag(long utcTicks, string tag) =>
      FM.SequenceId.TryFromText(tag, out var sequenceId)
        ? new StartMetadata(utcTicks, sequenceId)
        : throw new ArgumentException($"The sequence tag '{tag}' is not 1 to 16 printable ASCII characters", nameof(tag));

    /// <summary>The sequence id as the tools show it: its text when it is printable ASCII, otherwise the hex form of a UUID; null when empty.</summary>
    public string? SequenceText => SequenceId.IsEmpty ? null : SequenceId.ToString();

    /// <summary>The same metadata as the marker library's type.</summary>
    public FM.StartMetadata ToFrameMarker() => new FM.StartMetadata(UtcTicks, SequenceId);
  }
}
