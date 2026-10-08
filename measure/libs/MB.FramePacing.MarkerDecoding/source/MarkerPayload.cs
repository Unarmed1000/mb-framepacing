//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The frame marker payload. The wire format (sdk/doc/marker-format.md) is implemented once in C#, by the marker library (MB.FramePacing.Marker).
//*
//* The tools still count in ticks of 100 ns; the marker, and the marker library's payload, count in nanoseconds. This type is the one place
//* where the two meet (ToFrameMarker and FromFrameMarker), and the conversion goes when the tools move to nanoseconds:
//* - to a marker (ticks to nanoseconds) is exact. A time more than 292 years from zero does not fit nanoseconds and throws; a frame time
//*   or CPU busy longer than a marker carries (4.29 s) is held as the longest it carries, by the marker library's payload;
//* - from a marker (nanoseconds to ticks) is the nearest tick, a tie the even one: a time an application rounded to the nanosecond
//*   (a sixtieth of a second, 16 666 667 ns) then is the tick the tools' own times are on, which they round from the recording
//*   (166 667), and a frame shown on time has an error of nothing. The core types' own conversions cut to the tick (16 666 667 ns is
//*   166 666), which showed as errors of a tick in every report; so this one conversion is written here, by the user's choice, and
//*   not with a type's helper;
//* - 0 (unknown) is 0 both ways, and on demand is each side's own value (OnDemandFrameTime here, FM.Payload.OnDemandFrameTime there).
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
    TimeSpan AnimationTime,
    TimeSpan32 PreferredFrameTime = default,
    TimeSpan32 TargetFrameTime = default,
    TickCount64 IntendedDisplayTime = default,
    TickCount64 CpuStartTime = default,
    TimeSpan32 CpuBusy = default
  )
  {
    /// <summary>The most bytes a payload encodes to (a start marker's).</summary>
    public const int MaxEncodedByteCount = FM.Payload.MaxEncodedByteCount;

    /// <summary>
    /// The target and preferred frame time of an application that presents only when something changes, as the tools hold it in ticks
    /// (the marker's own value is <see cref="FM.Payload.OnDemandFrameTime"/>, in nanoseconds).
    /// </summary>
    public static readonly TimeSpan32 OnDemandFrameTime = TimeSpan32.MaxValue;

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
    /// The same payload as the marker library's type, its times in nanoseconds (exact). Throws ArgumentOutOfRangeException or
    /// OverflowException for a time more than 292 years from zero, which nanoseconds can not hold. A frame time (other than on demand) or
    /// CPU busy longer than a marker carries comes back as the longest it carries.
    /// </summary>
    public FM.Payload ToFrameMarker() =>
      new FM.Payload(
        (FM.MarkerKind)Kind,
        RunId,
        FrameIndex,
        Flags,
        NanosecondTimeSpan.FromTimeSpan(AnimationTime),
        ToMarkerFrameTime(PreferredFrameTime),
        ToMarkerFrameTime(TargetFrameTime),
        NanosecondTickCount.FromTickCount64(IntendedDisplayTime),
        NanosecondTickCount.FromTickCount64(CpuStartTime),
        ToMarkerDuration(CpuBusy)
      );

    /// <summary>
    /// The marker library's payload as the tools hold it, each of its times the nearest tick (a tie the even one).
    /// </summary>
    public static MarkerPayload FromFrameMarker(in FM.Payload payload) =>
      new MarkerPayload(
        (MarkerKind)payload.Kind,
        payload.RunId,
        payload.FrameIndex,
        payload.Flags,
        new TimeSpan(NearestTick(payload.AnimationTime.Nanoseconds)),
        FromMarkerFrameTime(payload.PreferredFrameTime),
        FromMarkerFrameTime(payload.TargetFrameTime),
        new TickCount64(NearestTick(payload.IntendedDisplayTime.Nanoseconds)),
        new TickCount64(NearestTick(payload.CpuStartTime.Nanoseconds)),
        FromMarkerDuration(payload.CpuBusy)
      );

    private static NanosecondTimeDuration ToMarkerFrameTime(TimeSpan32 frameTime) =>
      frameTime == OnDemandFrameTime ? FM.Payload.OnDemandFrameTime : ToMarkerDuration(frameTime);

    private static NanosecondTimeDuration ToMarkerDuration(TimeSpan32 duration) =>
      NanosecondTimeDuration.FromTimeDuration(TimeDuration.From(duration));

    private static TimeSpan32 FromMarkerFrameTime(NanosecondTimeDuration frameTime) =>
      frameTime == FM.Payload.OnDemandFrameTime ? OnDemandFrameTime : FromMarkerDuration(frameTime);

    // A marker's duration is at most 4.294967295 s, so its ticks always fit
    private static TimeSpan32 FromMarkerDuration(NanosecondTimeDuration duration) => new TimeSpan32((uint)NearestTick(duration.Nanoseconds));

    /// <summary>
    /// The tick nearest to a time in nanoseconds, a tie the even one (as the clips' generator rounds). Whole numbers only: the tools' one
    /// conversion from the marker's unit, which goes when they count in nanoseconds.
    /// </summary>
    private static long NearestTick(long nanoseconds)
    {
      long ticks = Math.DivRem(nanoseconds, NanosecondTimeSpan.NanosecondsPerTick, out long rest);
      if (rest < 0)
      {
        --ticks;
        rest += NanosecondTimeSpan.NanosecondsPerTick;
      }
      long twice = 2 * rest;
      bool up = twice > NanosecondTimeSpan.NanosecondsPerTick || (twice == NanosecondTimeSpan.NanosecondsPerTick && (ticks & 1) != 0);
      return up ? ticks + 1 : ticks;
    }
  }
}
