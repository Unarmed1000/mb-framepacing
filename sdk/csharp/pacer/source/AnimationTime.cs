//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The time a frame animates for (AnimationClock).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Pacer
{
  public readonly struct AnimationTime : IEquatable<AnimationTime>
  {
    public AnimationTime(long animationTicks, long stepTicks, long stepRefreshes)
    {
      AnimationTicks = animationTicks;
      StepTicks = stepTicks;
      StepRefreshes = stepRefreshes;
    }

    /// <summary>The animation time in ticks: the marker's animation time.</summary>
    public readonly long AnimationTicks;

    /// <summary>The step from the previous frame's animation time.</summary>
    public readonly long StepTicks;

    /// <summary>The step in whole refreshes.</summary>
    public readonly long StepRefreshes;

    public bool Equals(AnimationTime other) =>
      AnimationTicks == other.AnimationTicks && StepTicks == other.StepTicks && StepRefreshes == other.StepRefreshes;

    public override bool Equals(object obj) => obj is AnimationTime other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(AnimationTicks, StepTicks, StepRefreshes);

    public static bool operator ==(AnimationTime left, AnimationTime right) => left.Equals(right);

    public static bool operator !=(AnimationTime left, AnimationTime right) => !left.Equals(right);

    public override string ToString() => $"{{animation {AnimationTicks}, step {StepTicks} ({StepRefreshes} refreshes)}}";
  }
}
