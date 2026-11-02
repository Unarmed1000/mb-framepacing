//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What counts as an animation error. The animation error itself is PresentMon's number: animation time step minus display time step. That sum
//* cancels a stop against a jump: a frame that replaces a dropped one is shown two refreshes after the last shown frame and has moved two
//* refreshes on, so the sum is 0 although the motion stood still for a refresh and then jumped. Against the frame's target it is two parts, the
//* pacing error (display time step minus target: the frame before stayed too long or too short) and the prediction error (animation time step
//* minus target: the animation moved too far or too short), and a frame counts when the sum or either part is beyond the threshold.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Analysis
{
  public static class AnimationErrorRule
  {
    /// <summary>What a whole number of capture periods can be off a display's real refreshes by: less than this is no error on arrival.</summary>
    public const long RoundingNanoseconds = 1000;

    /// <summary>An application that presents on demand, by what its marker carries: it has no target to be off.</summary>
    public static bool IsOnDemand(NanosecondTimeDuration markerTarget, NanosecondTimeDuration markerPreferred) =>
      markerTarget == MarkerPayload.OnDemandFrameTime
      || (markerTarget == NanosecondTimeDuration.Zero && markerPreferred == MarkerPayload.OnDemandFrameTime);

    /// <summary>
    /// The frame's step is judged against a target the application gave: it has an animation error and both parts, and it does not present
    /// on demand. A static or uncertain step has no error, and without a target the application gave there are no parts (a game at 30 fps
    /// with no pacing fields would read as off on every frame): those frames keep the sum.
    /// </summary>
    public static bool IsJudgedInParts(PresentedFrame frame) =>
      frame.AnimationError.HasValue
      && frame.PacingError.HasValue
      && frame.PredictionError.HasValue
      && !IsOnDemand(frame.MarkerTargetFrameTime, frame.MarkerPreferredFrameTime);

    /// <summary>The frame counts as an animation error: the sum, or either part against its target, is beyond <paramref name="threshold"/>.</summary>
    public static bool Counts(PresentedFrame frame, NanosecondTimeDuration threshold)
    {
      if (frame.AnimationError is not { } error)
        return false;
      if (error.Duration() > threshold)
        return true;
      return IsJudgedInParts(frame) && (frame.PacingError!.Value.Duration() > threshold || frame.PredictionError!.Value.Duration() > threshold);
    }

    /// <summary>
    /// The stop: how far the motion fell behind while the frame before stayed on screen beyond its target (negative), or null when it did not
    /// stay half a refresh or more too long. A display changes frames on refreshes, so anything less is not a refresh that repeated.
    /// </summary>
    public static NanosecondTimeSpan? Stop(PresentedFrame frame, NanosecondTimeDuration refresh)
    {
      if (!IsJudgedInParts(frame))
        return null;
      var pacingError = frame.PacingError!.Value;
      return pacingError.Nanoseconds >= refresh.Nanoseconds / 2 ? new NanosecondTimeSpan(-pacingError.Nanoseconds) : null;
    }

    /// <summary>
    /// What is left of the frame's animation error when it arrives: the error, less its <see cref="Stop"/>. The stop and this add up to the
    /// animation error. Null when the frame's step is not judged.
    /// </summary>
    public static NanosecondTimeSpan? OnArrival(PresentedFrame frame, NanosecondTimeDuration refresh)
    {
      if (frame.AnimationError is not { } error)
        return null;
      if (Stop(frame, refresh) is not { } stop)
        return error;
      // The target is whole capture periods, each off the display's real one by a rounding: nothing but that is no error
      long left = error.Nanoseconds - stop.Nanoseconds;
      return new NanosecondTimeSpan(System.Math.Abs(left) < RoundingNanoseconds ? 0 : left);
    }
  }
}
