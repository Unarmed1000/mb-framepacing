//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The frame marker payload. The wire format (sdk/doc/marker-format.md) is implemented once in C#, by the marker library (MB.FramePacing.Marker).
//*
//* The tools count in nanoseconds, as the marker and the marker library's payload do, with the same types, so every time is the same both
//* ways (ToFrameMarker, FromFrameMarker): a sixtieth of a second, 16 666 667 ns in the marker, is 16 666 667 ns here.
//* - A frame time or CPU busy longer than a marker carries (4.29 s) is held as the longest it carries, by the marker library's payload.
//* - 0 (unknown) is 0, and on demand is the marker library's value (FM.Payload.OnDemandFrameTime).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using FM = MB.FramePacing.Marker;

namespace MB.FramePacing.MarkerDecoding
{
  /// <summary>The header carried by every marker, its fields in the order of the wire format; the timing fields are optional (0 = unknown).</summary>
  /// <param name="Kind">Frame, start, end or sync marker.</param>
  /// <param name="RunId">Identifies one test run: the start marker, every frame marker and the end marker of a run share it.</param>
  /// <param name="FrameIndex">The application's own rendered-frame counter. Unrelated to the capture card's frame counter.</param>
  /// <param name="Flags">
  /// The marker's flags: <see cref="FM.MarkerFlags.StaticAfter"/> when nothing animates while the frame is on screen,
  /// <see cref="FM.MarkerFlags.StaticBefore"/> when nothing animated while the frame before it was.
  /// </param>
  /// <param name="AnimationTime">The animation time the frame was rendered for.</param>
  /// <param name="PreferredFrameTime">
  /// The interval the application wants to run at; 0 = unknown, <see cref="OnDemandFrameTime"/> = frames only when something changes.
  /// </param>
  /// <param name="TargetFrameTime">The interval the frame pacer aims for before this frame; 0 = unknown.</param>
  /// <param name="IntendedDisplayTime">
  /// When the application's frame pacer intends the frame to become visible, on its steady clock; 0 = unknown.
  /// </param>
  /// <param name="CpuStartTime">CPU start time: when the CPU started working on the frame, on the frame pacer's steady clock; 0 = unknown.</param>
  /// <param name="CpuBusy">CPU busy: how long the CPU worked on the frame before presenting it; 0 = unknown.</param>
  public readonly record struct MarkerPayload(
    MarkerKind Kind,
    uint RunId,
    ulong FrameIndex,
    FM.MarkerFlags Flags,
    NanosecondTimeSpan AnimationTime,
    NanosecondTimeDuration PreferredFrameTime = default,
    NanosecondTimeDuration TargetFrameTime = default,
    NanosecondTickCount IntendedDisplayTime = default,
    NanosecondTickCount CpuStartTime = default,
    NanosecondTimeDuration CpuBusy = default
  )
  {
    /// <summary>The most bytes a payload encodes to (a start marker's).</summary>
    public const int MaxEncodedByteCount = FM.Payload.MaxEncodedByteCount;

    /// <summary>
    /// The target and preferred frame time of an application that presents only when something changes: the marker library's
    /// <see cref="FM.Payload.OnDemandFrameTime"/>, 4 294 967 295 ns.
    /// </summary>
    public static readonly NanosecondTimeDuration OnDemandFrameTime = FM.Payload.OnDemandFrameTime;

    /// <summary>Nothing animates while this frame is on screen, until the next frame: the step from it to the next frame is not judged.</summary>
    public bool IsStaticAfter => (Flags & FM.MarkerFlags.StaticAfter) != 0;

    /// <summary>Nothing animated while the frame before this one was on screen: that frame is static after, known one frame later.</summary>
    public bool IsStaticBefore => (Flags & FM.MarkerFlags.StaticBefore) != 0;

    /// <summary>
    /// The same frame as <paramref name="other"/>: the same run id and frame index, which is all a sync marker carries. Frame indices of
    /// different runs are unrelated.
    /// </summary>
    public bool IsSameFrame(in MarkerPayload other) => RunId == other.RunId && FrameIndex == other.FrameIndex;

    /// <summary>Serialize the payload. Start markers append the metadata (empty if null), other kinds ignore it.</summary>
    public byte[] Encode(StartMetadata? metadata = null)
    {
      Span<byte> buffer = stackalloc byte[MaxEncodedByteCount];
      int count = FM.FrameMarker.EncodePayload(ToFrameMarker(), (metadata ?? StartMetadata.Empty).ToFrameMarker(), buffer);
      return buffer.Slice(0, count).ToArray();
    }

    /// <summary>Parse the wire format. Fails on a wrong length, magic, format version, unknown kind or a CRC that does not match.</summary>
    /// <param name="metadata">The start metadata for a start marker, otherwise null.</param>
    public static bool TryDecode(ReadOnlySpan<byte> src, out MarkerPayload payload, out StartMetadata? metadata)
    {
      payload = default;
      metadata = null;
      if (!FM.FrameMarker.TryDecodePayload(src, out var decoded, out var start))
        return false;
      payload = FromFrameMarker(decoded);
      if (decoded.Kind == FM.MarkerKind.SequenceStart)
        metadata = new StartMetadata(start.UtcTicks, start.SequenceId);
      return true;
    }

    public static bool TryDecode(ReadOnlySpan<byte> src, out MarkerPayload payload) => TryDecode(src, out payload, out _);

    /// <summary>
    /// The same payload as the marker library's type, every time as it is. A frame time (other than on demand) or CPU busy longer than a
    /// marker carries comes back as the longest it carries.
    /// </summary>
    public FM.Payload ToFrameMarker() =>
      new FM.Payload(
        (FM.MarkerKind)Kind,
        RunId,
        FrameIndex,
        Flags,
        AnimationTime,
        PreferredFrameTime,
        TargetFrameTime,
        IntendedDisplayTime,
        CpuStartTime,
        CpuBusy
      );

    /// <summary>The marker library's payload as the tools hold it: every time as it is.</summary>
    public static MarkerPayload FromFrameMarker(in FM.Payload payload) =>
      new MarkerPayload(
        (MarkerKind)payload.Kind,
        payload.RunId,
        payload.FrameIndex,
        payload.Flags,
        payload.AnimationTime,
        payload.PreferredFrameTime,
        payload.TargetFrameTime,
        payload.IntendedDisplayTime,
        payload.CpuStartTime,
        payload.CpuBusy
      );
  }
}
