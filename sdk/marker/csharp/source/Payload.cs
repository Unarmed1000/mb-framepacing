//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The data every marker carries: the application's frame index, its animation time, the test run, the marker kind, the frame pacer's
//* intended display time, target frame time and preferred frame time, the CPU start time and CPU busy, and the flags.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FrameMarker
{
  public readonly struct Payload : IEquatable<Payload>
  {
    public Payload(
      ulong frameIndex,
      long animationTicks,
      uint runId = 0,
      MarkerKind kind = MarkerKind.Frame,
      long intendedDisplayTicks = 0,
      uint targetFrameTicks = 0,
      long cpuStartTicks = 0,
      uint cpuBusyTicks = 0,
      uint preferredFrameTicks = 0,
      MarkerFlags flags = MarkerFlags.None
    )
    {
      FrameIndex = frameIndex;
      AnimationTicks = animationTicks;
      RunId = runId;
      Kind = kind;
      IntendedDisplayTicks = intendedDisplayTicks;
      TargetFrameTicks = targetFrameTicks;
      CpuStartTicks = cpuStartTicks;
      CpuBusyTicks = cpuBusyTicks;
      PreferredFrameTicks = preferredFrameTicks;
      Flags = flags;
    }

    /// <summary>The application's own rendered-frame counter. Unrelated to the capture card's frame counter.</summary>
    public ulong FrameIndex { get; }

    /// <summary>Animation time in TimeSpan ticks (100 ns): the time the frame's animation was evaluated for.</summary>
    public long AnimationTicks { get; }

    /// <summary>Identifies one test run. The start marker, every frame marker and the end marker of a run carry the same id.</summary>
    public uint RunId { get; }

    public MarkerKind Kind { get; }

    /// <summary>
    /// When the frame pacer intends this frame to become visible, in ticks (100 ns) on its steady clock (any epoch, the same clock for the
    /// whole run). 0 = unknown.
    /// </summary>
    public long IntendedDisplayTicks { get; }

    /// <summary>
    /// The interval the frame pacer aims for between the previous frame and this one, in ticks (100 ns): 166 667 for 60 fps. 0 = unknown,
    /// <see cref="Marker.OnDemandFrameTicks"/> = frames only when something changes.
    /// </summary>
    public uint TargetFrameTicks { get; }

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

    /// <summary>
    /// The interval the application wants to run at, in ticks (100 ns): what it would aim for if nothing held it back. It differs from
    /// <see cref="TargetFrameTicks"/> only while the pacer runs slower than it wants (Swappy lowered to 30 fps: preferred 166 667, target
    /// 333 333). A 30 fps lock or a device idle at 1 fps prefers what it runs at. 0 = unknown, <see cref="Marker.OnDemandFrameTicks"/> =
    /// frames only when something changes.
    /// </summary>
    public uint PreferredFrameTicks { get; }

    /// <summary>
    /// <see cref="MarkerFlags.StaticAfter"/> when nothing animates while this frame is on screen, <see cref="MarkerFlags.StaticBefore"/> when
    /// nothing animated while the frame before it was; the other bits are reserved (0).
    /// </summary>
    public MarkerFlags Flags { get; }

    /// <summary>The same payload with another kind.</summary>
    public Payload WithKind(MarkerKind kind) =>
      new Payload(
        FrameIndex,
        AnimationTicks,
        RunId,
        kind,
        IntendedDisplayTicks,
        TargetFrameTicks,
        CpuStartTicks,
        CpuBusyTicks,
        PreferredFrameTicks,
        Flags
      );

    public bool Equals(Payload other) =>
      FrameIndex == other.FrameIndex
      && AnimationTicks == other.AnimationTicks
      && RunId == other.RunId
      && Kind == other.Kind
      && IntendedDisplayTicks == other.IntendedDisplayTicks
      && TargetFrameTicks == other.TargetFrameTicks
      && CpuStartTicks == other.CpuStartTicks
      && CpuBusyTicks == other.CpuBusyTicks
      && PreferredFrameTicks == other.PreferredFrameTicks
      && Flags == other.Flags;

    public override bool Equals(object obj) => obj is Payload other && Equals(other);

    public override int GetHashCode()
    {
      unchecked
      {
        int hash = (((((FrameIndex.GetHashCode() * 397) ^ AnimationTicks.GetHashCode()) * 397) ^ (int)RunId) * 397) ^ (int)Kind;
        hash = (((hash * 397) ^ IntendedDisplayTicks.GetHashCode()) * 397) ^ (int)TargetFrameTicks;
        hash = (hash * 397) ^ CpuStartTicks.GetHashCode();
        hash = (hash * 397) ^ (int)CpuBusyTicks;
        hash = (hash * 397) ^ (int)PreferredFrameTicks;
        return (hash * 397) ^ (int)Flags;
      }
    }

    public static bool operator ==(Payload left, Payload right) => left.Equals(right);

    public static bool operator !=(Payload left, Payload right) => !left.Equals(right);

    public override string ToString() =>
      $"{{frame {FrameIndex}, ticks {AnimationTicks}, run {RunId}, {Kind}, intended {IntendedDisplayTicks}, target {TargetFrameTicks}, cpu start {CpuStartTicks}, cpu busy {CpuBusyTicks}, preferred {PreferredFrameTicks}, flags {Flags}}}";
  }
}
