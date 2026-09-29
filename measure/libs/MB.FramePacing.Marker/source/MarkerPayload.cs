//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The frame marker payload. The wire format (doc/marker-format.md) is implemented once in C#, by the marker library (MB.FrameMarker).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using FM = MB.FrameMarker;

namespace MB.FramePacing.Marker
{
  /// <summary>The header carried by every marker.</summary>
  /// <param name="FrameIndex">The application's own rendered-frame counter. Unrelated to the capture card's frame counter.</param>
  /// <param name="AnimationTicks">The animation time the frame was rendered for, in <see cref="TimeSpan"/> ticks (100ns).</param>
  /// <param name="RunId">Identifies one test run: the start marker, every frame marker and the end marker of a run share it.</param>
  /// <param name="IntendedDisplayTicks">
  /// When the application's frame pacer intends the frame to become visible, in ticks (100ns) on its steady clock; 0 = unknown.
  /// </param>
  /// <param name="TargetFrameTicks">The interval the frame pacer aims for before this frame, in ticks (100ns); 0 = unknown.</param>
  /// <param name="CpuStartTicks">CPU start time: when the CPU started working on the frame, in ticks (100ns) on the frame pacer's steady clock; 0 = unknown.</param>
  /// <param name="CpuBusyTicks">CPU busy: how long the CPU worked on the frame before presenting it, in ticks (100ns); 0 = unknown.</param>
  public readonly record struct MarkerPayload(
    ulong FrameIndex,
    long AnimationTicks,
    uint RunId = 0,
    MarkerKind Kind = MarkerKind.Frame,
    long IntendedDisplayTicks = 0,
    uint TargetFrameTicks = 0,
    long CpuStartTicks = 0,
    uint CpuBusyTicks = 0
  )
  {
    /// <summary>Size of the header, which is the complete payload of frame and end markers.</summary>
    public const int ByteCount = FM.Marker.PayloadByteCount;
    public const int StartByteCount = FM.Marker.StartPayloadByteCount;
    public const int MaxEncodedByteCount = FM.Marker.MaxEncodedPayloadByteCount;
    public const byte Magic0 = FM.Marker.PayloadMagic0;
    public const byte Magic1 = FM.Marker.PayloadMagic1;
    public const byte FormatVersion = FM.Marker.PayloadFormatVersion;

    public TimeSpan AnimationTime => TimeSpan.FromTicks(AnimationTicks);

    /// <summary>Serialize the payload. Start markers append the metadata (empty if null), other kinds ignore it.</summary>
    public byte[] Encode(StartMetadata? metadata = null)
    {
      Span<byte> buffer = stackalloc byte[MaxEncodedByteCount];
      int count = FM.Marker.EncodePayload(ToFrameMarker(), (metadata ?? StartMetadata.Empty).ToFrameMarker(), buffer);
      return buffer.Slice(0, count).ToArray();
    }

    /// <summary>Parse the wire format. Fails on a wrong length, magic, format version or unknown kind.</summary>
    /// <param name="metadata">The start metadata for a start marker, otherwise null.</param>
    public static bool TryDecode(ReadOnlySpan<byte> src, out MarkerPayload payload, out StartMetadata? metadata)
    {
      payload = default;
      metadata = null;
      if (!FM.Marker.TryDecodePayload(src, out var decoded, out var start))
        return false;
      payload = new MarkerPayload(
        decoded.FrameIndex,
        decoded.AnimationTicks,
        decoded.RunId,
        (MarkerKind)decoded.Kind,
        decoded.IntendedDisplayTicks,
        decoded.TargetFrameTicks,
        decoded.CpuStartTicks,
        decoded.CpuBusyTicks
      );
      if (decoded.Kind == FM.MarkerKind.SequenceStart)
        metadata = new StartMetadata(start.UtcTicks, start.SequenceId);
      return true;
    }

    public static bool TryDecode(ReadOnlySpan<byte> src, out MarkerPayload payload) => TryDecode(src, out payload, out _);

    /// <summary>The same payload as the marker library's type.</summary>
    public FM.Payload ToFrameMarker() =>
      new FM.Payload(FrameIndex, AnimationTicks, RunId, (FM.MarkerKind)Kind, IntendedDisplayTicks, TargetFrameTicks, CpuStartTicks, CpuBusyTicks);
  }
}
