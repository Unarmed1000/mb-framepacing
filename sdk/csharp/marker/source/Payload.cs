//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The data every marker carries, in the order of the wire format: the marker kind, the test run, the application's frame index, the flags,
//* its animation time, the preferred frame time, the frame pacer's target frame time and intended display time, the CPU start time and CPU
//* busy. Every time is in ticks of 100 ns, in the type that says what it is: a span, a point on the pacer's steady clock, or a 32-bit
//* interval, so every value of a field is valid on the wire. The kind is one of MarkerKind's: the constructor throws for another. The
//* flags are kept as given, reserved bits included.
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

    /// <summary>The target and preferred frame time of a renderer that presents only when something changes: there is no interval to aim for.</summary>
    public static readonly TimeSpan32 OnDemandFrameTime = TimeSpan32.MaxValue;

    /// <summary>
    /// The payload's fields in the order of the wire format (doc/marker-format.md); the timing fields are optional (0 = unknown). Throws
    /// ArgumentOutOfRangeException for a kind that is not a <see cref="MarkerKind"/>.
    /// </summary>
    public Payload(
      MarkerKind kind,
      uint runId,
      ulong frameIndex,
      MarkerFlags flags,
      TimeSpan animationTime,
      TimeSpan32 preferredFrameTime = default,
      TimeSpan32 targetFrameTime = default,
      TickCount64 intendedDisplayTime = default,
      TickCount64 cpuStartTime = default,
      TimeSpan32 cpuBusy = default
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
      PreferredFrameTime = preferredFrameTime;
      TargetFrameTime = targetFrameTime;
      IntendedDisplayTime = intendedDisplayTime;
      CpuStartTime = cpuStartTime;
      CpuBusy = cpuBusy;
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
    public readonly TimeSpan AnimationTime;

    /// <summary>
    /// The interval the application wants to run at: what it would aim for if nothing held it back. It differs from
    /// <see cref="TargetFrameTime"/> only while the pacer runs slower than it wants (a pacer lowered to 30 fps: preferred 166 667 ticks,
    /// target 333 333). A 30 fps lock or a device idle at 1 fps prefers what it runs at. 0 = unknown, <see cref="OnDemandFrameTime"/> =
    /// frames only when something changes.
    /// </summary>
    public readonly TimeSpan32 PreferredFrameTime;

    /// <summary>
    /// The interval the frame pacer aims for between the previous frame and this one: 166 667 ticks for 60 fps. 0 = unknown,
    /// <see cref="OnDemandFrameTime"/> = frames only when something changes.
    /// </summary>
    public readonly TimeSpan32 TargetFrameTime;

    /// <summary>
    /// When the frame pacer intends this frame to become visible, on its steady clock (any epoch, the same clock for the whole run).
    /// 0 = unknown.
    /// </summary>
    public readonly TickCount64 IntendedDisplayTime;

    /// <summary>
    /// CPU start time: when the CPU started working on this frame (PresentMon's CPUStartTime), on the same steady clock as
    /// <see cref="IntendedDisplayTime"/>. Anywhere inside a refresh; frames can overlap. 0 = unknown.
    /// </summary>
    public readonly TickCount64 CpuStartTime;

    /// <summary>
    /// CPU busy: how long the CPU worked on this frame before presenting it (PresentMon's MsCPUBusy), from <see cref="CpuStartTime"/> until
    /// Present is called. The marker is drawn last, so the application measures it as it draws the marker. It does not include the GPU's
    /// work. May span several refreshes. 0 = unknown.
    /// </summary>
    public readonly TimeSpan32 CpuBusy;

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

    /// <summary>Every field, the times in ticks.</summary>
    public override string ToString() =>
      $"{{{Kind}, run {RunId}, frame {FrameIndex}, flags {Flags}, ticks {AnimationTime.Ticks}, preferred {PreferredFrameTime.Ticks}, target {TargetFrameTime.Ticks}, intended {IntendedDisplayTime.Ticks}, cpu start {CpuStartTime.Ticks}, cpu busy {CpuBusy.Ticks}}}";
  }
}
