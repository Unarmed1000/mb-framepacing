//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Targets a display can not show. At a fixed refresh rate a frame stays a whole number of refreshes, so a pacer can only aim for one: a target
//* frame time that is not (25 ms on a 60 Hz display), or that is shorter than a refresh (120 fps on 60 Hz), is invalid. The frames are judged
//* by the real refresh periods all the same, the target in whole refreshes (FrameTimeRounding), and the report says so. What decides is the
//* target the frame carries, else its plan's step per frame index, never the preferred frame time: that is what the application wants, and
//* its pacer may be holding a valid target.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MB.FramePacing.Analysis
{
  public static class InvalidTargetCheck
  {
    /// <summary>A target frame time that is not a whole number of refreshes of <paramref name="refresh"/>, within the rounding's slack, or under one.</summary>
    public static bool IsInvalid(NanosecondTimeDuration target, NanosecondTimeDuration refresh)
    {
      if (refresh <= NanosecondTimeDuration.Zero || target <= NanosecondTimeDuration.Zero)
        return false;
      double refreshes = target.Nanoseconds / (double)refresh.Nanoseconds;
      double whole = Math.Round(refreshes);
      return whole < 1 || Math.Abs(refreshes - whole) > FrameTimeRounding.Slack;
    }

    /// <summary>The target frame <paramref name="index"/> aims at, as it carries it: 0 when none is known, or on demand.</summary>
    public static NanosecondTimeDuration TargetOf(IReadOnlyList<PresentedFrame> frames, int index)
    {
      var frame = frames[index];
      if (AnimationErrorRule.IsOnDemand(frame.MarkerTargetFrameTime, frame.MarkerPreferredFrameTime))
        return NanosecondTimeDuration.Zero;
      if (frame.MarkerTargetFrameTime != NanosecondTimeDuration.Zero)
        return frame.MarkerTargetFrameTime;
      if (index == 0 || frames[index - 1].Segment != frame.Segment)
        return NanosecondTimeDuration.Zero;
      var previous = frames[index - 1];
      if (frame.IntendedDisplayTime == default || previous.IntendedDisplayTime == default)
        return NanosecondTimeDuration.Zero;
      long planned = (frame.IntendedDisplayTime - previous.IntendedDisplayTime).Nanoseconds;
      return planned > 0
        ? NanosecondTimeDuration.FromNanoseconds(planned / (long)(frame.FrameIndex - previous.FrameIndex))
        : NanosecondTimeDuration.Zero;
    }

    /// <summary>Per frame, whether it aims at a target the display can not show.</summary>
    public static bool[] Frames(IReadOnlyList<PresentedFrame> frames, NanosecondTimeDuration refresh)
    {
      var invalid = new bool[frames.Count];
      for (int i = 0; i < frames.Count; ++i)
        invalid[i] = IsInvalid(TargetOf(frames, i), refresh);
      return invalid;
    }

    /// <summary>The warning for <paramref name="frames"/> as its lines (the facts, then what follows), or none when every target can be shown.</summary>
    public static IReadOnlyList<string> Lines(IReadOnlyList<PresentedFrame> frames, NanosecondTimeDuration refresh)
    {
      var invalid = Frames(frames, refresh);
      int count = invalid.Count(i => i);
      if (count == 0)
        return Array.Empty<string>();
      var targets = Enumerable
        .Range(0, frames.Count)
        .Where(i => invalid[i])
        .Select(i => TargetOf(frames, i).Value.TotalMilliseconds)
        .Distinct()
        .Order()
        .ToArray();
      string listed = string.Join(", ", targets.Take(3).Select(ms => ms.ToString("0.##", CultureInfo.InvariantCulture)));
      string more = targets.Length > 3 ? ", ..." : string.Empty;
      return new[]
      {
        string.Create(
          CultureInfo.InvariantCulture,
          $"{count} frame(s) aim at a target frame time of {listed}{more} ms, which is not a whole number of this display's {refresh.Value.TotalMilliseconds:0.##} ms refreshes."
        ),
        "A frame stays on screen for whole refreshes: those frames are judged against the target in whole refreshes, so holding such a target reads as off.",
      };
    }

    /// <summary>The warning for <paramref name="frames"/> as one text, or null (<see cref="Lines"/>).</summary>
    public static string? Warning(IReadOnlyList<PresentedFrame> frames, NanosecondTimeDuration refresh) =>
      Lines(frames, refresh) is { Count: > 0 } lines ? string.Join(" ", lines) : null;
  }
}
