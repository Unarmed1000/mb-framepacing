//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The data every marker carries: the application's frame index, its animation time, the test run, the marker kind and the frame pacer's
//* intended display time and target frame time.
//*
//* (c) 2026 Mana Battery
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
      uint targetFrameTicks = 0
    )
    {
      FrameIndex = frameIndex;
      AnimationTicks = animationTicks;
      RunId = runId;
      Kind = kind;
      IntendedDisplayTicks = intendedDisplayTicks;
      TargetFrameTicks = targetFrameTicks;
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

    /// <summary>The interval the frame pacer aims for between the previous frame and this one, in ticks (100 ns): 166 667 for 60 fps. 0 = unknown.</summary>
    public uint TargetFrameTicks { get; }

    /// <summary>The same payload with another kind.</summary>
    public Payload WithKind(MarkerKind kind) => new Payload(FrameIndex, AnimationTicks, RunId, kind, IntendedDisplayTicks, TargetFrameTicks);

    public bool Equals(Payload other) =>
      FrameIndex == other.FrameIndex
      && AnimationTicks == other.AnimationTicks
      && RunId == other.RunId
      && Kind == other.Kind
      && IntendedDisplayTicks == other.IntendedDisplayTicks
      && TargetFrameTicks == other.TargetFrameTicks;

    public override bool Equals(object obj) => obj is Payload other && Equals(other);

    public override int GetHashCode()
    {
      unchecked
      {
        int hash = (((((FrameIndex.GetHashCode() * 397) ^ AnimationTicks.GetHashCode()) * 397) ^ (int)RunId) * 397) ^ (int)Kind;
        return (((hash * 397) ^ IntendedDisplayTicks.GetHashCode()) * 397) ^ (int)TargetFrameTicks;
      }
    }

    public static bool operator ==(Payload left, Payload right) => left.Equals(right);

    public static bool operator !=(Payload left, Payload right) => !left.Equals(right);

    public override string ToString() =>
      $"{{frame {FrameIndex}, ticks {AnimationTicks}, run {RunId}, {Kind}, intended {IntendedDisplayTicks}, target {TargetFrameTicks}}}";
  }
}
