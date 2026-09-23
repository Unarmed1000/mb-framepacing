//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Extra data carried by a SequenceStart marker.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  /// <summary>Extra data carried by a <see cref="MarkerKind.SequenceStart"/> marker.</summary>
  /// <param name="UtcTicks">Wall clock start time as <see cref="DateTime"/> UTC ticks, 0 = unknown.</param>
  /// <param name="Name">Test name, at most <see cref="MarkerPayload.MaxStartNameBytes"/> bytes as UTF-8.</param>
  public sealed record StartMetadata(long UtcTicks, string Name)
  {
    public static readonly StartMetadata Empty = new StartMetadata(0, string.Empty);

    public DateTime? StartTimeUtc => UtcTicks > 0 && UtcTicks <= DateTime.MaxValue.Ticks ? new DateTime(UtcTicks, DateTimeKind.Utc) : null;

    public static StartMetadata Create(DateTime startTimeUtc, string name) => new StartMetadata(startTimeUtc.ToUniversalTime().Ticks, name);
  }
}
