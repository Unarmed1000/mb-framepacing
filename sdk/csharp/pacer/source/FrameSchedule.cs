//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the pacer plans for a frame (FramePacer.BeginFrame): the values to apply, and the marker's pacing fields. Times are ticks on the steady
//* clock of FrameInput.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Pacer
{
  public readonly struct FrameSchedule : IEquatable<FrameSchedule>
  {
    public FrameSchedule(
      ulong frameIndex,
      long intendedDisplayTicks,
      long earliestPresentTicks,
      uint swapInterval,
      uint targetFrameTicks,
      uint preferredFrameTicks,
      long cpuStartTicks,
      SwapIntervalChange change
    )
    {
      FrameIndex = frameIndex;
      IntendedDisplayTicks = intendedDisplayTicks;
      EarliestPresentTicks = earliestPresentTicks;
      SwapInterval = swapInterval;
      TargetFrameTicks = targetFrameTicks;
      PreferredFrameTicks = preferredFrameTicks;
      CpuStartTicks = cpuStartTicks;
      Change = change;
    }

    /// <summary>The pacer's count of frames, from 0.</summary>
    public readonly ulong FrameIndex;

    /// <summary>
    /// The refresh the pacer aims for this frame to be shown at: the marker's intended display time, and the present time for a scheduled present
    /// (VK_GOOGLE_display_timing's desiredPresentTime, VK_EXT_present_timing, EGL_ANDROID_presentation_time, Metal's present(at:)).
    /// </summary>
    public readonly long IntendedDisplayTicks;

    /// <summary>
    /// The refresh before the intended one: with plain FIFO vsync, a frame presented at or after it (and before the intended one) is shown at the
    /// intended one. Sleep until it, then present.
    /// </summary>
    public readonly long EarliestPresentTicks;

    /// <summary>Refreshes from the previous frame's display to this one's: DXGI's SyncInterval, eglSwapInterval, QualitySettings.vSyncCount.</summary>
    public readonly uint SwapInterval;

    /// <summary>The marker's target frame time: SwapInterval refreshes, rounded to a tick.</summary>
    public readonly uint TargetFrameTicks;

    /// <summary>The marker's preferred frame time: PacerSettings.PreferredSwapInterval refreshes, rounded to a tick.</summary>
    public readonly uint PreferredFrameTicks;

    /// <summary>The marker's CPU start time: FrameInput.NowTicks.</summary>
    public readonly long CpuStartTicks;

    /// <summary>What the swap interval rule decided on the previous frame; this frame is the first at the new interval.</summary>
    public readonly SwapIntervalChange Change;

    public bool Equals(FrameSchedule other) =>
      FrameIndex == other.FrameIndex
      && IntendedDisplayTicks == other.IntendedDisplayTicks
      && EarliestPresentTicks == other.EarliestPresentTicks
      && SwapInterval == other.SwapInterval
      && TargetFrameTicks == other.TargetFrameTicks
      && PreferredFrameTicks == other.PreferredFrameTicks
      && CpuStartTicks == other.CpuStartTicks
      && Change == other.Change;

    public override bool Equals(object obj) => obj is FrameSchedule other && Equals(other);

    public override int GetHashCode() =>
      HashCode.Combine(
        FrameIndex,
        IntendedDisplayTicks,
        EarliestPresentTicks,
        SwapInterval,
        TargetFrameTicks,
        PreferredFrameTicks,
        CpuStartTicks,
        Change
      );

    public static bool operator ==(FrameSchedule left, FrameSchedule right) => left.Equals(right);

    public static bool operator !=(FrameSchedule left, FrameSchedule right) => !left.Equals(right);

    public override string ToString() =>
      $"{{frame {FrameIndex}, intended {IntendedDisplayTicks}, earliest present {EarliestPresentTicks}, swap interval {SwapInterval}, target {TargetFrameTicks}, preferred {PreferredFrameTicks}, cpu start {CpuStartTicks}, {Change}}}";
  }
}
