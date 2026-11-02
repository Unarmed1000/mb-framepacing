//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The period the pacer intended for a frame, as the late verdict measures it (PacingAnalyzer): the marker's target frame time in whole refreshes,
//* else the frame's target (the plan's step per frame index, else the preferred frame time, the target frame rate given to the tools or one
//* refresh). A drop does not change it. None on demand. The charts draw by it too, so the two never disagree.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  public static class IntendedPeriods
  {
    /// <summary>The intended period of <paramref name="frame"/> of an analysed run, with the run's refresh period.</summary>
    public static NanosecondTimeDuration? Of(PresentedFrame frame, NanosecondTimeDuration refresh) =>
      Of(frame.MarkerTargetFrameTime, frame.TargetFrameTime, refresh);

    /// <summary>
    /// The intended period of a frame with that marker target frame time (0: none) and target (<see cref="PresentedFrame.TargetFrameTime"/>).
    /// </summary>
    public static NanosecondTimeDuration? Of(NanosecondTimeDuration markerTarget, NanosecondTimeDuration? target, NanosecondTimeDuration refresh)
    {
      if (markerTarget == MarkerDecoding.MarkerPayload.OnDemandFrameTime)
        return null;
      return markerTarget != NanosecondTimeDuration.Zero ? FrameTimeRounding.WholeRefreshes(markerTarget, refresh) : target;
    }
  }
}
