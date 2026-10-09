//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The words of the frame stages charts: a shape's tooltip (what it is, how long it took and when, and whatever else the log knows about
//* it, a line each) and the notes under a chart. Times are milliseconds from the chart's first frame start, as its axis.
//*
//* The texts say what a log says and no more: a frame reported without a display time is never called not shown, and a display time is
//* the graphics driver's report, not a measurement by the tools.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MB.FramePacing.Charts.Stages
{
  internal static class FrameStagesTips
  {
    public const long TicksPerMs = 10_000;

    public const string SlotWaitTip = "A wait for the GPU: for the frame that used the slot before, and its present.";

    public const string UnloggedTip =
      "From the last thing the log has of the frame (its acquire, else its update) to the wait before its start: it is held there, and the log does not say by what.";

    public const string GpuTip = "Begin and end as the application measured them on its CPU clock.";
    public const string GpuBoundTip = "A duration only, an earlier frame's, placed at this frame's present.";
    public const string WaitingTip = "From the end of its GPU work (or its present's return, if that is later) to its display time.";

    public const string HoverNote =
      "A stage the log has no times for is not drawn. In a viewer that shows an SVG's tooltips, a shape under the pointer gives its times and details.";

    public const string StagesNote =
      "A dashed GPU box is the GPU time the application had at that frame (an earlier frame's), placed at the present.";

    public const string LanesNote =
      "The thin lines follow a frame: from the CPU thread (its submit, where the log has one) to its GPU work, from its present call down to its "
      + "row of the wait to be shown, from the end of its GPU work to the start of that wait, and from there to the screen.";

    public const string DisplayNote =
      "A frame reported without a display time may or may not have been shown: the time it would have been on screen is drawn as not known, "
      + "and only a capture settles it.";

    public const string ErrorNote =
      "The animation error of a shown frame is its animation time step minus its display time step from the frame before, "
      + "by the reported display times; it is not known after a frame without one.";

    public const string PacerNote =
      "The pacer's lanes: the work it was given for a frame (CPU plus the newest GPU time the application had) as a length in the frame time it had, "
      + "and the late frames in its frame window as a share of the count the rule slows down at (its default settings).";

    public const string AnimationErrorNote =
      "In that lane a frame's animation error is how far its box moved against its place on the display, compared with the frame before.";

    public static string AnimationTip(long frame) => $"The animation clock is tied to the display at #{Whole(frame)}'s display time.";

    public static string AnimationNote(long frame) =>
      "The lane by animation time has each frame from its animation time to the next frame's: when the animation expected it on screen. "
      + $"The animation clock is tied to the display at #{Whole(frame)}'s display time.";

    /// <summary>Ticks as milliseconds with <paramref name="digits"/> decimals.</summary>
    public static string Ms(long ticks, int digits = 3) => SvgMarkup.Fixed(ticks / (double)TicksPerMs, digits);

    public static string SignedMs(long ticks, int digits = 2)
    {
      string text = Ms(Math.Abs(ticks), digits);
      return IsZero(text) ? text : (ticks > 0 ? "+" : "-") + text;
    }

    /// <summary>How far one moment is from another, to go before the other's name: "0.190 ms before", "exactly at".</summary>
    public static string Apart(long ticks)
    {
      string text = Ms(Math.Abs(ticks));
      return IsZero(text) ? "exactly at" : $"{text} ms {(ticks > 0 ? "after" : "before")}";
    }

    public static string Whole(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A shape's tooltip: what it is, how long it took and when (milliseconds from the chart's first frame start, as the axis), and
    /// whatever else is known about it, a line each.
    /// </summary>
    public static string Tip(long zero, string title, long first, long? last, params string[] more)
    {
      string when = last is { } end ? $"{Ms(end - first)} ms: from {Ms(first - zero)} to {Ms(end - zero)} ms" : $"at {Ms(first - zero)} ms";
      return string.Join('\n', new[] { title, when }.Concat(more.Where(line => line.Length > 0)));
    }

    /// <summary>How the sample held a frame for its time (its holdMethod column): the pattern of the wait.</summary>
    public static string HoldStyle(FrameLogFrame frame) => frame.At("holdMethod") == 1 ? "wait-vsync" : "wait";

    public static string WaitTip(long zero, string title, (long First, long Last) wait, long? target, FrameLogFrame? frame = null)
    {
      string aimed = target is { } aim ? $"It aimed at {Ms(aim - zero)} ms and woke {Apart(wait.Last - aim)} that." : string.Empty;
      string method = frame?.At("holdMethod") switch
      {
        0 => "A sleep on a timer.",
        1 => "A wait for the vertical blank, as the window system reports it.",
        _ => string.Empty,
      };
      return Tip(zero, title, wait.First, wait.Last, method, aimed);
    }

    public static string DisplayTip(long zero, FrameLogFrame frame, FrameOnScreen screen, long period, long? error, bool hasBefore)
    {
      string refreshes = period > 0 ? $"{SvgMarkup.Fixed((screen.End - screen.Shown) / (double)period, 2)} refreshes, " : string.Empty;
      string judged =
        error is { } known ? $"Animation error {SignedMs(known, 3)} ms: its animation time step minus its display time step, from the frame before."
        : hasBefore ? "Animation error not known: the frame before has no display time."
        : string.Empty;
      return Tip(
        zero,
        $"#{Whole(frame.Index)} on screen",
        screen.Shown,
        screen.End,
        $"{refreshes}asked for a swap interval of {Whole(frame.SwapInterval)}.",
        $"Shown {Ms(screen.Shown - frame.Start)} ms after its frame start.",
        judged,
        screen.UnknownEnd != null ? "Then a stretch the log has no display time for." : string.Empty,
        "The display time is what the graphics driver reported, not a capture."
      );
    }

    public static string UnknownTip(long zero, FrameLogFrame frame, FrameOnScreen screen, long lastReported)
    {
      string names = string.Join(", ", screen.Unreported.Select(later => $"#{Whole(later.Index)}"));
      var more = new List<string>(screen.Unreported.Select(later => $"#{Whole(later.Index)}: {FrameLogStages.NoDisplayTime(later, lastReported)}"))
      {
        "What the display showed here takes a capture to know.",
      };
      return Tip(zero, $"Not known: #{Whole(frame.Index)} still, or {names}", screen.End, screen.UnknownEnd, more.ToArray());
    }

    public static string GpuWorkTip(long zero, FrameLogFrame frame, (long First, long Last) work) =>
      Tip(
        zero,
        $"#{Whole(frame.Index)} GPU work",
        work.First,
        work.Last,
        GpuTip,
        frame.At("gpuTimeTicks") is { } measured ? $"GPU time of the frame as measured: {Ms(measured)} ms." : string.Empty
      );

    public static string AcquireTip(long zero, FrameLogFrame frame, (long First, long Last) acquire) =>
      Tip(
        zero,
        $"#{Whole(frame.Index)} acquire (call to return)",
        acquire.First,
        acquire.Last,
        frame.At("imageIndex") is { } image ? $"Swap chain image {Whole(image)}." : string.Empty
      );

    public static string DrawTip(long zero, FrameLogFrame frame, (long First, long Last) draw)
    {
      string animation = frame.At("animationTimeTicks") is { } time
        ? $"Animation time {Ms(time)} ms" + (frame.At("animationStepTicks") is { } step ? $", a step of {Ms(step)} ms." : ".")
        : string.Empty;
      return Tip(
        zero,
        $"#{Whole(frame.Index)} draw: frame start to work done",
        draw.First,
        draw.Last,
        frame.At("markerDrawTicks") is { } drawn ? $"Marker recorded at {Ms(drawn - zero)} ms." : string.Empty,
        animation,
        $"Swap interval {Whole(frame.SwapInterval)}."
      );
    }

    public static string PresentTip(long zero, FrameLogFrame frame, (long First, long Last) present) =>
      Tip(
        zero,
        $"#{Whole(frame.Index)} present call (call to return)",
        present.First,
        present.Last,
        frame.At("presentId") is { } id ? $"Present id {Whole(id)}." : string.Empty,
        frame.At("presentTargetRelativeNs") is { } relative and not 0
          ? $"A timed present: {Ms(FrameLogStages.FloorDivide(relative, 100))} ms after the display time of the frame before."
          : string.Empty
      );

    /// <summary>A number's text is zero: every digit of it is.</summary>
    private static bool IsZero(string text) => text.All(c => c is '0' or '.' or '-');
  }
}
