//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The data every marker carries, in the order of the wire format: the marker kind, the test run, the application's frame index, the flags,
//* its animation time, the preferred frame time, the frame pacer's target frame time and intended display time, the CPU start time and CPU
//* busy. Every time is in nanoseconds, in the type that says what it is: a span, a point on the pacer's steady clock, or a duration, which
//* is never negative.
//*
//* The marker holds its three durations in four bytes each, so a payload holds none longer than a marker can carry: the constructor caps a
//* longer CPU busy at MaxCpuBusy and a longer frame time at MaxFrameTime (never an error: this runs in a frame loop). So every value of a
//* payload is valid on the wire, and a payload decodes to exactly what was encoded. The kind is one of MarkerKind's: the constructor throws
//* for another. The flags are kept as given, reserved bits included.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  public readonly struct Payload : IEquatable<Payload>
  {
    /// <summary>The most bytes a payload encodes to (a start marker's): the buffer size for <see cref="FrameMarker.EncodePayload"/>.</summary>
    public const int MaxEncodedByteCount = 81;

    /// <summary>
    /// The target and preferred frame time of a renderer that presents only when something changes: there is no interval to aim for. The
    /// largest value the field's four bytes hold (0xFFFFFFFF), so no frame time is that long: see <see cref="MaxFrameTime"/>.
    /// </summary>
    public static readonly NanosecondTimeDuration OnDemandFrameTime = NanosecondTimeDuration.FromNanoseconds(4_294_967_295);

    /// <summary>
    /// The longest target and preferred frame time a marker carries, 4.294967294 s (slower than 0.233 fps): one below
    /// <see cref="OnDemandFrameTime"/>. A longer one is held as this.
    /// </summary>
    public static readonly NanosecondTimeDuration MaxFrameTime = NanosecondTimeDuration.FromNanoseconds(4_294_967_294);

    /// <summary>The longest CPU busy a marker carries, 4.294967295 s. A longer one is held as this.</summary>
    public static readonly NanosecondTimeDuration MaxCpuBusy = NanosecondTimeDuration.FromNanoseconds(4_294_967_295);

    /// <summary>
    /// The payload's fields in the order of the wire format (doc/marker-format.md); the timing fields are optional (0 = unknown). A CPU
    /// busy longer than <see cref="MaxCpuBusy"/> is held as that, and a frame time longer than <see cref="MaxFrameTime"/> as that unless it
    /// is <see cref="OnDemandFrameTime"/>. Throws ArgumentOutOfRangeException for a kind that is not a <see cref="MarkerKind"/>.
    /// </summary>
    public Payload(
      MarkerKind kind,
      uint runId,
      ulong frameIndex,
      MarkerFlags flags,
      NanosecondTimeSpan animationTime,
      NanosecondTimeDuration preferredFrameTime = default,
      NanosecondTimeDuration targetFrameTime = default,
      NanosecondTickCount intendedDisplayTime = default,
      NanosecondTickCount cpuStartTime = default,
      NanosecondTimeDuration cpuBusy = default
    )
    {
      if (kind > MarkerKind.Sync)
      {
        throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a MarkerKind");
      }
      Kind = kind;
      RunId = runId;
      FrameIndex = frameIndex;
      Flags = flags;
      AnimationTime = animationTime;
      PreferredFrameTime = CappedFrameTime(preferredFrameTime);
      TargetFrameTime = CappedFrameTime(targetFrameTime);
      IntendedDisplayTime = intendedDisplayTime;
      CpuStartTime = cpuStartTime;
      CpuBusy = NanosecondTimeDuration.Min(cpuBusy, MaxCpuBusy);
    }

    public readonly MarkerKind Kind;

    /// <summary>Identifies one test run. The start marker, every frame marker and the end marker of a run carry the same id.</summary>
    public readonly uint RunId;

    /// <summary>The application's own rendered-frame counter. Unrelated to the capture card's frame counter.</summary>
    public readonly ulong FrameIndex;

    /// <summary>
    /// <see cref="MarkerFlags.StaticAfter"/> when nothing animates while this frame is on screen, <see cref="MarkerFlags.StaticBefore"/> when
    /// nothing animated while the frame before it was; the other bits are reserved (write 0, a decoded payload keeps them).
    /// </summary>
    public readonly MarkerFlags Flags;

    /// <summary>The animation time: the time on the application's animation clock the frame's animation was evaluated for.</summary>
    public readonly NanosecondTimeSpan AnimationTime;

    /// <summary>
    /// The interval the application wants to run at: what it would aim for if nothing held it back. It differs from
    /// <see cref="TargetFrameTime"/> only while the pacer runs slower than it wants (a pacer lowered to 30 fps: preferred 16 666 667 ns,
    /// target 33 333 333). A 30 fps lock or a device idle at 1 fps prefers what it runs at. 0 = unknown, <see cref="OnDemandFrameTime"/> =
    /// frames only when something changes; at most <see cref="MaxFrameTime"/> otherwise.
    /// </summary>
    public readonly NanosecondTimeDuration PreferredFrameTime;

    /// <summary>
    /// The interval the frame pacer aims for between the previous frame and this one: 16 666 667 ns for 60 fps. 0 = unknown,
    /// <see cref="OnDemandFrameTime"/> = frames only when something changes; at most <see cref="MaxFrameTime"/> otherwise.
    /// </summary>
    public readonly NanosecondTimeDuration TargetFrameTime;

    /// <summary>
    /// When the frame pacer intends this frame to become visible, on its steady clock (any epoch, the same clock for the whole run).
    /// 0 = unknown.
    /// </summary>
    public readonly NanosecondTickCount IntendedDisplayTime;

    /// <summary>
    /// CPU start time: when the CPU started working on this frame (PresentMon's CPUStartTime), on the same steady clock as
    /// <see cref="IntendedDisplayTime"/>. Anywhere inside a refresh; frames can overlap. 0 = unknown.
    /// </summary>
    public readonly NanosecondTickCount CpuStartTime;

    /// <summary>
    /// CPU busy: how long the CPU worked on this frame before presenting it (PresentMon's MsCPUBusy), from <see cref="CpuStartTime"/> until
    /// Present is called. The marker is drawn last, so the application measures it as it draws the marker. It does not include the GPU's
    /// work. May span several refreshes. 0 = unknown; at most <see cref="MaxCpuBusy"/>.
    /// </summary>
    public readonly NanosecondTimeDuration CpuBusy;

    /// <summary>
    /// The same payload with another kind: a start or end marker carries the values of the frame that shows it, a sync marker its run id
    /// and frame index.
    /// </summary>
    public Payload WithKind(MarkerKind kind) =>
      new Payload(kind, RunId, FrameIndex, Flags, AnimationTime, PreferredFrameTime, TargetFrameTime, IntendedDisplayTime, CpuStartTime, CpuBusy);

    public bool Equals(Payload other) =>
      Kind == other.Kind
      && RunId == other.RunId
      && FrameIndex == other.FrameIndex
      && Flags == other.Flags
      && AnimationTime == other.AnimationTime
      && PreferredFrameTime == other.PreferredFrameTime
      && TargetFrameTime == other.TargetFrameTime
      && IntendedDisplayTime == other.IntendedDisplayTime
      && CpuStartTime == other.CpuStartTime
      && CpuBusy == other.CpuBusy;

    public override bool Equals(object obj) => obj is Payload other && Equals(other);

    public override int GetHashCode()
    {
      // Ten fields: more than HashCode.Combine takes
      var hash = new HashCode();
      hash.Add(Kind);
      hash.Add(RunId);
      hash.Add(FrameIndex);
      hash.Add(Flags);
      hash.Add(AnimationTime);
      hash.Add(PreferredFrameTime);
      hash.Add(TargetFrameTime);
      hash.Add(IntendedDisplayTime);
      hash.Add(CpuStartTime);
      hash.Add(CpuBusy);
      return hash.ToHashCode();
    }

    public static bool operator ==(Payload left, Payload right) => left.Equals(right);

    public static bool operator !=(Payload left, Payload right) => !left.Equals(right);

    /// <summary>Every field, each time as its type writes it, in nanoseconds: "animation 16666667 ns".</summary>
    public override string ToString() =>
      $"{{{Kind}, run {RunId}, frame {FrameIndex}, flags {Flags}, animation {AnimationTime}, preferred {PreferredFrameTime}, target {TargetFrameTime}, intended {IntendedDisplayTime}, cpu start {CpuStartTime}, cpu busy {CpuBusy}}}";

    /// <summary>
    /// A frame time as a marker carries it: on demand as it is, any other one at most MaxFrameTime, so that only on demand reads as on
    /// demand.
    /// </summary>
    private static NanosecondTimeDuration CappedFrameTime(NanosecondTimeDuration frameTime) =>
      frameTime == OnDemandFrameTime ? frameTime : NanosecondTimeDuration.Min(frameTime, MaxFrameTime);
  }
}
