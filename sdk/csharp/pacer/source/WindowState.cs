//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the swap interval rule's window holds: the frames since the last change, of the last WindowTicks. For overlays and logs.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Pacer
{
  public readonly struct WindowState : IEquatable<WindowState>
  {
    public WindowState(uint frames, uint lateFrames, long averageWorkTicks, long spanTicks, bool full)
    {
      Frames = frames;
      LateFrames = lateFrames;
      AverageWorkTicks = averageWorkTicks;
      SpanTicks = spanTicks;
      Full = full;
    }

    public readonly uint Frames;

    public readonly uint LateFrames;

    /// <summary>The frames' average work time (FrameEnd.WorkTicks), 0 without frames.</summary>
    public readonly long AverageWorkTicks;

    /// <summary>From the oldest frame's display time to the newest's.</summary>
    public readonly long SpanTicks;

    /// <summary>The window spans more than WindowTicks: the rule may decide on it.</summary>
    public readonly bool Full;

    public bool Equals(WindowState other) =>
      Frames == other.Frames
      && LateFrames == other.LateFrames
      && AverageWorkTicks == other.AverageWorkTicks
      && SpanTicks == other.SpanTicks
      && Full == other.Full;

    public override bool Equals(object obj) => obj is WindowState other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Frames, LateFrames, AverageWorkTicks, SpanTicks, Full);

    public static bool operator ==(WindowState left, WindowState right) => left.Equals(right);

    public static bool operator !=(WindowState left, WindowState right) => !left.Equals(right);

    public override string ToString() => $"{{{Frames} frames, {LateFrames} late, average work {AverageWorkTicks}, span {SpanTicks}, full {Full}}}";
  }
}
