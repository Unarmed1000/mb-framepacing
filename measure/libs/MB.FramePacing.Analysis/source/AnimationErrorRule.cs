//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What counts as an animation error. The animation error itself is PresentMon's number: animation time step minus display time step. That sum
//* cancels a stop against a jump: a frame that replaces a dropped one is shown two refreshes after the last shown frame and has moved two
//* refreshes on, so the sum is 0 although the motion stood still for a refresh and then jumped. Against the frame's target it is two parts, the
//* pacing error (display time step minus target: the frame before stayed too long or too short) and the prediction error (animation time step
//* minus target: the animation moved too far or too short), and a frame counts when the sum or either part is beyond the threshold. A part is
//* measured against whole capture periods, which rounded capture times can be off: it must be past the threshold by more than that.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Analysis
{
  public static class AnimationErrorRule
  {
    /// <summary>
    /// What a whole number of capture periods can be off a display's real refreshes by: less than this is no error on arrival, and no part past
    /// the threshold. A target is whole capture periods and a display step the difference of two rounded times: a period of 16 666 667 ns against
    /// a display step of 16 666 666 ns turns an animation step that is exactly the threshold short into one a nanosecond past it.
    /// </summary>
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

    /// <summary>
    /// The frame counts as an animation error: the sum, or either part against its target, is beyond <paramref name="threshold"/>, a part by more
    /// than <see cref="RoundingNanoseconds"/>.
    /// </summary>
    public static bool Counts(PresentedFrame frame, NanosecondTimeDuration threshold)
    {
      if (frame.AnimationError is not { } error)
        return false;
      if (error.Duration() > threshold)
        return true;
      return IsJudgedInParts(frame) && (IsBeyond(frame.PacingError!.Value, threshold) || IsBeyond(frame.PredictionError!.Value, threshold));
    }

    private static bool IsBeyond(NanosecondTimeSpan part, NanosecondTimeDuration threshold) =>
      part.Duration().Nanoseconds - RoundingNanoseconds > threshold.Nanoseconds;

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
