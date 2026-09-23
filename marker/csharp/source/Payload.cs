//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The data every marker carries: the application's frame index, its animation time, the test run and the marker kind.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FrameMarker
{
  public readonly struct Payload : IEquatable<Payload>
  {
    public Payload(ulong frameIndex, long animationTicks, uint runId = 0, MarkerKind kind = MarkerKind.Frame)
    {
      FrameIndex = frameIndex;
      AnimationTicks = animationTicks;
      RunId = runId;
      Kind = kind;
    }

    /// <summary>The application's own rendered-frame counter. Unrelated to the capture card's frame counter.</summary>
    public ulong FrameIndex { get; }

    /// <summary>Animation time in TimeSpan ticks (100 ns): the time the frame's animation was evaluated for.</summary>
    public long AnimationTicks { get; }

    /// <summary>Identifies one test run. The start marker, every frame marker and the end marker of a run carry the same id.</summary>
    public uint RunId { get; }

    public MarkerKind Kind { get; }

    /// <summary>The same payload with another kind.</summary>
    public Payload WithKind(MarkerKind kind) => new Payload(FrameIndex, AnimationTicks, RunId, kind);

    public bool Equals(Payload other) =>
      FrameIndex == other.FrameIndex && AnimationTicks == other.AnimationTicks && RunId == other.RunId && Kind == other.Kind;

    public override bool Equals(object obj) => obj is Payload other && Equals(other);

    public override int GetHashCode()
    {
      unchecked
      {
        return (((((FrameIndex.GetHashCode() * 397) ^ AnimationTicks.GetHashCode()) * 397) ^ (int)RunId) * 397) ^ (int)Kind;
      }
    }

    public static bool operator ==(Payload left, Payload right) => left.Equals(right);

    public static bool operator !=(Payload left, Payload right) => !left.Equals(right);

    public override string ToString() => $"{{frame {FrameIndex}, ticks {AnimationTicks}, run {RunId}, {Kind}}}";
  }
}
