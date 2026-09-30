//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the application knows when it starts a frame (FramePacer.BeginFrame). All times are ticks on the application's steady clock (Stopwatch or
//* the platform's clock, converted to ticks): the pacer never reads a clock. NowTicks is required; everything else is what better platforms report,
//* 0 = unknown (so a steady clock must not give a real time of exactly 0; they count from boot).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Pacer
{
  public readonly struct FrameInput
  {
    public FrameInput(
      long nowTicks,
      long vsyncTicks = 0,
      long previousDisplayTicks = 0,
      long predictedDisplayTicks = 0,
      long refreshPeriodNanoseconds = 0
    )
    {
      NowTicks = nowTicks;
      VsyncTicks = vsyncTicks;
      PreviousDisplayTicks = previousDisplayTicks;
      PredictedDisplayTicks = predictedDisplayTicks;
      RefreshPeriodNanoseconds = refreshPeriodNanoseconds;
    }

    /// <summary>Now: the frame's CPU start time (required).</summary>
    public readonly long NowTicks;

    /// <summary>
    /// A vsync the platform reported, the latest one it knows: DWM's qpcVBlank, Choreographer's frame time, CADisplayLink's timestamp. It puts
    /// the pacer's refresh grid on the display's.
    /// </summary>
    public readonly long VsyncTicks;

    /// <summary>
    /// When the previous frame was really shown, from presentation feedback (VK_GOOGLE_display_timing, VK_EXT_present_timing, DXGI frame
    /// statistics, EGL frame timestamps, Wayland presentation-time). Without it the pacer infers it from the previous Present.
    /// </summary>
    public readonly long PreviousDisplayTicks;

    /// <summary>
    /// The display time the platform predicts for this frame (Choreographer's expected presentation time, OpenXR's predictedDisplayTime,
    /// CADisplayLink's targetTimestamp): the frame aims no earlier.
    /// </summary>
    public readonly long PredictedDisplayTicks;

    /// <summary>
    /// The display's refresh period as the platform reports it now, in nanoseconds (Choreographer's refresh rate callback,
    /// VK_GOOGLE_display_timing's refreshDuration, VK_EXT_present_timing, CADisplayLink's duration). One that rounds to another nanosecond than
    /// the pacer's period is a display mode change: the pacer starts again at it (FramePacer.SetRefreshPeriod).
    /// </summary>
    public readonly long RefreshPeriodNanoseconds;
  }
}
