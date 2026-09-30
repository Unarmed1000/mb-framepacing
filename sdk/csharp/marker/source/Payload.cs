//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The data every marker carries, in the order of the wire format: the marker kind, the test run, the application's frame index, the flags,
//* its animation time, the preferred frame time, the frame pacer's target frame time and intended display time, the CPU start time and CPU
//* busy.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  public readonly struct Payload : IEquatable<Payload>
  {
    /// <summary>The payload's fields in the order of the wire format (doc/marker-format.md); the timing fields are optional (0 = unknown).</summary>
    public Payload(
      MarkerKind kind,
      uint runId,
      ulong frameIndex,
      MarkerFlags flags,
      long animationTicks,
      uint preferredFrameTicks = 0,
      uint targetFrameTicks = 0,
      long intendedDisplayTicks = 0,
      long cpuStartTicks = 0,
      uint cpuBusyTicks = 0
    )
    {
      Kind = kind;
      RunId = runId;
      FrameIndex = frameIndex;
      Flags = flags;
      AnimationTicks = animationTicks;
      PreferredFrameTicks = preferredFrameTicks;
      TargetFrameTicks = targetFrameTicks;
      IntendedDisplayTicks = intendedDisplayTicks;
      CpuStartTicks = cpuStartTicks;
      CpuBusyTicks = cpuBusyTicks;
    }

    public MarkerKind Kind { get; }

    /// <summary>Identifies one test run. The start marker, every frame marker and the end marker of a run carry the same id.</summary>
    public uint RunId { get; }

    /// <summary>The application's own rendered-frame counter. Unrelated to the capture card's frame counter.</summary>
    public ulong FrameIndex { get; }

    /// <summary>
    /// <see cref="MarkerFlags.StaticAfter"/> when nothing animates while this frame is on screen, <see cref="MarkerFlags.StaticBefore"/> when
    /// nothing animated while the frame before it was; the other bits are reserved (0).
    /// </summary>
    public MarkerFlags Flags { get; }

    /// <summary>Animation time in TimeSpan ticks (100 ns): the time the frame's animation was evaluated for.</summary>
    public long AnimationTicks { get; }

    /// <summary>
    /// The interval the application wants to run at, in ticks (100 ns): what it would aim for if nothing held it back. It differs from
    /// <see cref="TargetFrameTicks"/> only while the pacer runs slower than it wants (Swappy lowered to 30 fps: preferred 166 667, target
    /// 333 333). A 30 fps lock or a device idle at 1 fps prefers what it runs at. 0 = unknown, <see cref="FrameMarker.OnDemandFrameTicks"/> =
    /// frames only when something changes.
    /// </summary>
    public uint PreferredFrameTicks { get; }

    /// <summary>
    /// The interval the frame pacer aims for between the previous frame and this one, in ticks (100 ns): 166 667 for 60 fps. 0 = unknown,
    /// <see cref="FrameMarker.OnDemandFrameTicks"/> = frames only when something changes.
    /// </summary>
    public uint TargetFrameTicks { get; }

    /// <summary>
    /// When the frame pacer intends this frame to become visible, in ticks (100 ns) on its steady clock (any epoch, the same clock for the
    /// whole run). 0 = unknown.
    /// </summary>
    public long IntendedDisplayTicks { get; }

    /// <summary>
    /// CPU start time: when the CPU started working on this frame (PresentMon's CPUStartTime), in ticks (100 ns) on the same steady clock
    /// as <see cref="IntendedDisplayTicks"/>. Anywhere inside a refresh; frames can overlap. 0 = unknown.
    /// </summary>
    public long CpuStartTicks { get; }

    /// <summary>
    /// CPU busy: how long the CPU worked on this frame before presenting it (PresentMon's MsCPUBusy), from <see cref="CpuStartTicks"/> until
    /// Present is called, in ticks (100 ns). The marker is drawn last, so the application measures it as it draws the marker. It does not
    /// include the GPU's work. May span several refreshes. 0 = unknown.
    /// </summary>
    public uint CpuBusyTicks { get; }

    /// <summary>The same payload with another kind.</summary>
    public Payload WithKind(MarkerKind kind) =>
      new Payload(
        kind,
        RunId,
        FrameIndex,
        Flags,
        AnimationTicks,
        PreferredFrameTicks,
        TargetFrameTicks,
        IntendedDisplayTicks,
        CpuStartTicks,
        CpuBusyTicks
      );

    public bool Equals(Payload other) =>
      Kind == other.Kind
      && RunId == other.RunId
      && FrameIndex == other.FrameIndex
      && Flags == other.Flags
      && AnimationTicks == other.AnimationTicks
      && PreferredFrameTicks == other.PreferredFrameTicks
      && TargetFrameTicks == other.TargetFrameTicks
      && IntendedDisplayTicks == other.IntendedDisplayTicks
      && CpuStartTicks == other.CpuStartTicks
      && CpuBusyTicks == other.CpuBusyTicks;

    public override bool Equals(object obj) => obj is Payload other && Equals(other);

    public override int GetHashCode()
    {
      unchecked
      {
        int hash = (((((int)Kind * 397) ^ (int)RunId) * 397) ^ FrameIndex.GetHashCode()) * 397;
        hash = (((hash ^ (int)Flags) * 397) ^ AnimationTicks.GetHashCode()) * 397;
        hash = (((hash ^ (int)PreferredFrameTicks) * 397) ^ (int)TargetFrameTicks) * 397;
        hash = (((hash ^ IntendedDisplayTicks.GetHashCode()) * 397) ^ CpuStartTicks.GetHashCode()) * 397;
        return hash ^ (int)CpuBusyTicks;
      }
    }

    public static bool operator ==(Payload left, Payload right) => left.Equals(right);

    public static bool operator !=(Payload left, Payload right) => !left.Equals(right);

    public override string ToString() =>
      $"{{{Kind}, run {RunId}, frame {FrameIndex}, flags {Flags}, ticks {AnimationTicks}, preferred {PreferredFrameTicks}, target {TargetFrameTicks}, intended {IntendedDisplayTicks}, cpu start {CpuStartTicks}, cpu busy {CpuBusyTicks}}}";
  }
}
