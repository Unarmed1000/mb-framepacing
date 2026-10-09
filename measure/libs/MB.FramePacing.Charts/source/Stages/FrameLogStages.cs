//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a frame log says of a frame's stages: the waits and holds of its CPU side, its time on screen, its animation error, and what the
//* log does not have. Whole ticks of 100 ns, as the log has them: nothing here is a floating point number but a median of two values.
//*
//* What a log does not have is not made up, and what it does not know stays not known: a frame reported without a display time may or may
//* not have been shown. Display times are what the graphics driver reported, not a measurement by the tools.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Charts.Stages
{
  /// <summary>Experimental. The stages of a frame worked out from its row of a frame log.</summary>
  public static class FrameLogStages
  {
    /// <summary>From this size on an animation error stands out in a chart: the tools' error threshold (analyze --error-threshold-ms).</summary>
    public const long ErrorThresholdTicks = 10_000;

    /// <summary>The middle value, or the mean of the two in the middle; the values need not be sorted.</summary>
    public static double Median(IReadOnlyList<long> values)
    {
      var sorted = values.OrderBy(value => value).ToArray();
      int middle = sorted.Length / 2;
      return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    /// <summary>The display's refresh period as the log has it; without one, the frames' target frame time by their swap interval.</summary>
    public static long RefreshPeriod(IReadOnlyList<FrameLogFrame> frames)
    {
      var periods = frames
        .Select(frame => frame.At("displayRefreshPeriodTicks"))
        .Where(period => period is { } known and not 0)
        .Select(p => p!.Value)
        .ToList();
      if (periods.Count > 0)
        return (long)Median(periods);
      var targets = frames
        .Where(frame => frame.At("targetFrameTimeTicks") is { } target and not 0)
        .Select(frame => FloorDivide(frame.At("targetFrameTimeTicks")!.Value, frame.SwapInterval))
        .ToList();
      return targets.Count > 0 ? (long)Median(targets) : 0;
    }

    /// <summary>When the frame left the screen: the next shown frame's display time, or its swap interval's time for the last one.</summary>
    public static long? ScreenEnd(IReadOnlyList<FrameLogFrame> frames, int position, long period)
    {
      if (frames[position].Shown is not { } shown)
        return null;
      for (int later = position + 1; later < frames.Count; ++later)
      {
        if (frames[later].Shown is { } following)
          return following;
      }
      return shown + (frames[position].SwapInterval * period);
    }

    /// <summary>
    /// The stretch of a frame's CPU side the log has no times for: from the last thing it did before its frame start (the acquire, else the
    /// update) to the wait before its frame start, else the frame start. The frame is held there, and the log does not say by what.
    /// </summary>
    public static (long First, long Last)? UnloggedHold(FrameLogFrame frame)
    {
      long? last = null;
      foreach (string key in new[] { "acquireReturnTicks", "hostUpdateEndTicks" })
      {
        if (frame.At(key) is { } value)
          last = last is { } known ? Math.Max(known, value) : value;
      }
      long until = frame.At("frameWaitStartTicks") ?? frame.Start;
      return last is { } from && from < Math.Min(until, frame.Start) ? (from, until) : null;
    }

    /// <summary>
    /// The wait that holds a frame's start: from where it began to the wait for a frame slot where the log has one (the loop goes on with
    /// the slot and the acquire before its frame start), else to the frame start.
    /// </summary>
    public static (long First, long Last)? StartWait(FrameLogFrame frame)
    {
      long end = frame.At("frameSlotWaitBeginTicks") ?? frame.Start;
      return frame.At("frameWaitStartTicks") is { } begin && end >= begin ? (begin, end) : null;
    }

    /// <summary>The hold of the finished frame before its present: the wait the log has, else from its work done to its present call.</summary>
    public static (long First, long Last)? PresentHold(FrameLogFrame frame) =>
      frame.Span("presentWaitBeginTicks", "presentWaitEndTicks") ?? frame.Span("endFrameTicks", "presentCallTicks");

    /// <summary>From the first thing the CPU did for the frame to the last: its whole time on the CPU thread, waits included.</summary>
    public static (long First, long Last)? CpuSpan(FrameLogFrame frame)
    {
      var moments = new[]
      {
        "hostCpuStartTicks",
        "frameWaitStartTicks",
        "frameSlotWaitBeginTicks",
        "acquireCallTicks",
        "frameStartTicks",
        "endFrameTicks",
        "presentReturnTicks",
      }
        .Select(frame.At)
        .Where(value => value != null)
        .Select(value => value!.Value)
        .ToList();
      return moments.Count >= 2 ? (moments.Min(), moments.Max()) : null;
    }

    /// <summary>On screen at least half a refresh longer than its swap interval asked for.</summary>
    public static bool HeldLonger(FrameLogFrame frame, long shown, long end, long period) =>
      period > 0 && (end - shown) * 2 >= ((2 * frame.SwapInterval) + 1) * period;

    /// <summary>The frame's time on screen as far as the log says; null for a frame without a display time.</summary>
    public static FrameOnScreen? OnScreen(IReadOnlyList<FrameLogFrame> frames, int position, long period)
    {
      var frame = frames[position];
      if (frame.Shown is not { } shown)
        return null;
      long own = shown + (frame.SwapInterval * period);
      var unreported = new List<FrameLogFrame>();
      for (int later = position + 1; later < frames.Count; ++later)
      {
        if (frames[later].Shown is not { } following)
          unreported.Add(frames[later]);
        else if (unreported.Count > 0 && following > own)
          return new FrameOnScreen(shown, own, following, unreported);
        else
          return new FrameOnScreen(shown, following, null, Array.Empty<FrameLogFrame>());
      }
      return new FrameOnScreen(shown, own, null, Array.Empty<FrameLogFrame>());
    }

    /// <summary>Why a frame has no display time, as far as the log says. It never says the frame was not shown: only a capture does.</summary>
    public static string NoDisplayTime(FrameLogFrame frame, long lastReported)
    {
      if (frame.At("resultReadAtFrame") is { } readAt)
        return $"The graphics driver reported this present (read at frame {readAt}) without a display time.";
      if (frame.At("presentTimingRequested") == 0)
        return "No display time was asked for with this present.";
      if (frame.Index > lastReported)
        return "Its result had not come back when the run ended.";
      return "The log has no result for this present.";
    }

    /// <summary>
    /// The animation error of the step into a frame, as the tools define it: its animation time step minus its display time step, both
    /// from the frame presented before it. Known when both frames have a display time: here the driver's, not a capture's.
    /// </summary>
    public static long? AnimationError(IReadOnlyList<FrameLogFrame> frames, int position)
    {
      if (position == 0)
        return null;
      var frame = frames[position];
      var before = frames[position - 1];
      if (
        frame.Shown is not { } shown
        || before.Shown is not { } shownBefore
        || frame.At("animationTimeTicks") is not { } animation
        || before.At("animationTimeTicks") is not { } animationBefore
      )
        return null;
      return (animation - animationBefore) - (shown - shownBefore);
    }

    /// <summary>
    /// What to add to an animation time to place it on the CPU clock: the two are tied at the first of these frames that has a display
    /// time (its animation time is put at its display time). The frame, and the offset. A chart ties at the frame before its first row
    /// where it can: tied at its first row, that frame would sit exactly on its display whatever its animation error.
    /// </summary>
    public static (FrameLogFrame Frame, long Offset)? AnimationTie(IEnumerable<FrameLogFrame> rows)
    {
      foreach (var frame in rows)
      {
        if (frame.Shown is { } shown && frame.At("animationTimeTicks") is { } animation)
          return (frame, shown - animation);
      }
      return null;
    }

    /// <summary>The present was given a target time: the presentation engine holds the frame until then.</summary>
    public static bool IsTimed(FrameLogFrame frame) => frame.At("presentTargetRelativeNs") is { } relative and not 0;

    /// <summary>The quotient rounded down, also for a negative value (the divisor is more than zero).</summary>
    public static long FloorDivide(long value, long divisor)
    {
      long quotient = value / divisor;
      return value % divisor != 0 && ((value < 0) != (divisor < 0)) ? quotient - 1 : quotient;
    }
  }
}
