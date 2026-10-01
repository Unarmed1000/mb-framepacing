//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* summary.json's runs[].statistics: the run's display and animation time steps, animation error, drift, frame rates and the CPU side.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.Text.Json.Serialization;

namespace MB.FramePacing.Data
{
  /// <param name="FramesWithAnimationError">Frames whose |animation error| exceeds the error threshold.</param>
  /// <param name="ErrorPerFrameMs">The mean |animation error| of the frames with one.</param>
  /// <param name="PercentError">The sum of |animation error| as a percentage of the display time steps of those frames.</param>
  /// <param name="OnePercentLowFps">The frame rate at the 99th percentile display time step (nearest rank); null with fewer than 100 frames.</param>
  /// <param name="DisplayDeltaMs">The display time steps that count toward the frame rate (a static frame's time on screen does not).</param>
  /// <param name="AverageFps">Those frames over the time their display time steps cover.</param>
  /// <param name="PointOnePercentLowFps">The same at the 99.9th percentile; null with fewer than 1000 frames.</param>
  /// <param name="ExcludedStaticFrames">The display time steps the frame rate numbers leave out: each is a static frame's time on screen.</param>
  /// <param name="UncertainSteps">
  /// The display time steps a capture gap made uncertain (flag UncertainStep): not judged, left out of the frame rate numbers.
  /// </param>
  /// <param name="CpuBusyMs">From the markers: CPU busy (PresentMon's MsCPUBusy); null in output written before it existed.</param>
  /// <param name="FrameTimeMs">The frametime, from one frame's CPU start to the next one's (PresentMon's MsBetweenAppStart).</param>
  /// <param name="CpuWaitMs">The frametime minus CPU busy (PresentMon's MsCPUWait).</param>
  public sealed record SummaryStatistics(
    [property: JsonRequired] ValueStatistics DisplayDeltaMs,
    [property: JsonRequired] ValueStatistics AnimationDeltaMs,
    [property: JsonRequired] ValueStatistics AnimationErrorMs,
    [property: JsonRequired] ValueStatistics AbsoluteAnimationErrorMs,
    [property: JsonRequired] ValueStatistics DriftMs,
    [property: JsonRequired] ValueStatistics OnScreenMs,
    [property: JsonRequired] long FramesWithAnimationError,
    [property: JsonRequired] double ErrorPerFrameMs,
    [property: JsonRequired] double PercentError,
    double AverageFps,
    double? OnePercentLowFps,
    double? PointOnePercentLowFps,
    [property: JsonRequired] long ExcludedStaticFrames,
    [property: JsonRequired] long UncertainSteps,
    ValueStatistics? CpuBusyMs,
    ValueStatistics? FrameTimeMs,
    ValueStatistics? CpuWaitMs
  );
}
