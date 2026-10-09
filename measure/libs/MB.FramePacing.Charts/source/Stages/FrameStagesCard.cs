//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL. The stages of each frame of a stretch of a frame log, a row per frame: time runs from left to right with the display's
//* refreshes as the grid, and a row has the CPU's part (its update, the wait for its start, a wait for a frame slot, the acquire, its draw,
//* the submit, a hold before the present, the present call), the GPU's work on the frame, the time it waited to be shown, and how long it
//* was on screen with its animation error. A shape's tip has its times and details.
//*
//* What a log does not have is not drawn, and what it does not know is drawn as not known: a frame reported without a display time may or
//* may not have been shown. Display times are what the graphics driver reported, not a measurement by the tools.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using static MB.FramePacing.Charts.Stages.FrameStagesTips;

namespace MB.FramePacing.Charts.Stages
{
  /// <summary>Experimental. A frame log's stages as a chart with a row per frame.</summary>
  public static class FrameStagesCard
  {
    public const int Width = 1692;

    /// <summary>The most frames a chart has: more is no longer a picture (the trace file has the whole run).</summary>
    public const int MaxFrames = 60;

    /// <summary>A stage narrower than this is drawn this wide, so it can be seen.</summary>
    internal const double MinWidth = 1.4;

    private const double Left = 116;
    private const double Right = 286;
    private const double Top = 172;
    private const double RowHeight = 52;

    /// <summary>The chart's SVG.</summary>
    public static string Render(string name, IReadOnlyList<FrameLogFrame> frames, long first, int count, IReadOnlyList<string> notes) =>
      SvgCardWriter.Write(Build(name, frames, first, count, notes));

    /// <summary>
    /// The chart of <paramref name="count"/> frames of the log from frame <paramref name="first"/> on. <paramref name="notes"/> are what
    /// reading the log found (FrameLog.Read).
    /// </summary>
    public static CardDrawing Build(string name, IReadOnlyList<FrameLogFrame> frames, long first, int count, IReadOnlyList<string> notes)
    {
      var stretch = FrameStretch.Of(frames, first, count, Left, Width - Left - Right);
      int position = stretch.Position;
      var rows = stretch.Rows;
      long period = stretch.Period;
      long timeFirst = stretch.TimeFirst;
      long timeLast = stretch.TimeLast;
      double scale = stretch.Scale;
      long lastReported = frames[^1].Index - FrameLog.SkipLast;
      long zero = rows[0].Start;

      var after = new List<string> { HoverNote, StagesNote, DisplayNote, ErrorNote };
      after.AddRange(notes);
      double height = Top + (rows.Count * RowHeight) + 76 + (20 * after.Count);
      var canvas = new FrameStagesCanvas(Width, height, $"Frame stages: {name}", stretch.Subtitle("A row per frame"));
      canvas.Key(
        28,
        92,
        new[]
        {
          ("update", "update"),
          ("wait", "sleep, or a wait the log does not name"),
          ("wait-vsync", "vertical blank wait"),
          ("wait-slot", "frame slot wait"),
          ("unlogged", "held, no times logged"),
          ("acquire", "acquire"),
          ("draw", "draw (to work done)"),
          ("submit", "submit"),
        }
      );
      canvas.Key(
        28,
        113,
        new[]
        {
          ("present", "present call (with an arrow)"),
          ("gpu", "GPU work"),
          ("queue", "waiting to be shown"),
          ("screen-b", "on screen"),
          ("screen-long", "on screen longer than asked"),
          ("unknown", "not known"),
        }
      );
      canvas.Polygon(new[] { (28.0, 125.0), (38.0, 125.0), (33.0, 133.0) }, "expected");
      canvas.Text(46, 134, "where the pacer said to start the frame (CPU) and to show it (display)", "axis");
      canvas.Polygon(new[] { (520.0, 125.0), (530.0, 125.0), (525.0, 133.0) }, "timed-arrow");
      canvas.Text(538, 134, "a timed present, and (display lane) the time it asked for", "axis");
      canvas.Line(938, 123, 938, 135, "marker-tick");
      canvas.Text(946, 134, "marker recorded (CPU)", "axis");

      double top = Top - 8;
      double bottom = Top + (rows.Count * RowHeight);
      string grid = stretch.DrawGrid(canvas, top, bottom);
      stretch.DrawAxis(canvas, bottom);

      double X(long ticks) => stretch.X(ticks);

      // The frame's number on one of its boxes: inside it where it fits, else right after it
      void Identify(string tag, long firstTick, long lastTick, double y)
      {
        double width = Math.Max(X(lastTick) - X(firstTick), MinWidth);
        if (width >= (6.2 * tag.Length) + 6)
          canvas.Text(X(firstTick) + (width / 2), y, tag, "id-in", "middle");
        else
          canvas.Text(X(firstTick) + width + 3, y, tag, "id-by");
      }

      void Box(long firstTick, long lastTick, double y, double boxHeight, string style, string about = "") =>
        canvas.Rect(X(firstTick), y, Math.Max(X(lastTick) - X(firstTick), MinWidth), boxHeight, style, tip: about);

      void Mark(long ticks, double y, string style, string about)
      {
        double at = X(ticks);
        canvas.Polygon(new[] { (at - 5, y - 8), (at + 5, y - 8), (at, y) }, style, about);
      }

      for (int offset = 0; offset < rows.Count; ++offset)
      {
        var frame = rows[offset];
        double y = Top + (offset * RowHeight);
        double cpu = y + 9;
        double gpu = y + 22;
        double screenY = y + 37;
        string tag = $"#{Whole(frame.Index)}";
        canvas.Line(Left, y + RowHeight, Width - Right, y + RowHeight, "row-line");
        canvas.Text(28, y + 30, tag, "frame-label");
        if (offset == 0)
        {
          foreach (var (lane, label) in new[] { (cpu, "CPU"), (gpu, "GPU"), (screenY, "display") })
            canvas.Text(Left - 8, lane + 9, label, "lane-label", "end");
        }

        // The CPU's part: update, the wait for the frame start, acquire, draw, a hold before the present, the present call
        if (frame.Span("hostCpuStartTicks", "hostUpdateEndTicks") is { } update)
          Box(update.First, update.Last, cpu, 9, "update", Tip(zero, $"{tag} update", update.First, update.Last));
        if (FrameLogStages.StartWait(frame) is { } wait && (wait.Last - wait.First) * scale >= MinWidth)
          Box(
            wait.First,
            wait.Last,
            cpu,
            9,
            HoldStyle(frame),
            WaitTip(zero, $"{tag} wait for its frame start", wait, frame.At("frameWaitTargetTicks"), frame)
          );
        if (frame.Span("frameSlotWaitBeginTicks", "frameSlotWaitEndTicks") is { } slot && (slot.Last - slot.First) * scale >= MinWidth)
          Box(slot.First, slot.Last, cpu, 9, "wait-slot", Tip(zero, $"{tag} wait for a frame slot", slot.First, slot.Last, SlotWaitTip));
        if (frame.Span("acquireCallTicks", "acquireReturnTicks") is { } acquire)
          Box(acquire.First, acquire.Last, cpu, 9, "acquire", AcquireTip(zero, frame, acquire));
        if (FrameLogStages.UnloggedHold(frame) is { } held && (held.Last - held.First) * scale >= 3)
          Box(held.First, held.Last, cpu + 1.5, 6, "unlogged", Tip(zero, $"{tag} held, no times logged", held.First, held.Last, UnloggedTip));
        var draw = frame.Span("frameStartTicks", "endFrameTicks");
        if (draw is { } drawn)
          Box(drawn.First, drawn.Last, cpu, 9, "draw", DrawTip(zero, frame, drawn));
        var submit = frame.Span("submitCallTicks", "submitReturnTicks");
        if (submit is { } submitted)
          Box(
            submitted.First,
            submitted.Last,
            cpu,
            9,
            "submit",
            Tip(zero, $"{tag} submit of its GPU work (call to return)", submitted.First, submitted.Last)
          );
        if (FrameLogStages.PresentHold(frame) is { } hold && (hold.Last - hold.First) * scale >= MinWidth)
          Box(
            hold.First,
            hold.Last,
            cpu,
            9,
            HoldStyle(frame),
            WaitTip(zero, $"{tag} hold before its present", hold, frame.At("presentWaitTargetTicks"), frame)
          );
        if (frame.Span("presentCallTicks", "presentReturnTicks") is { } present)
        {
          double at = X(present.First);
          Box(present.First, present.Last, cpu, 9, "present", PresentTip(zero, frame, present));
          canvas.Line(at, cpu - 5, at, cpu + 11, "present-line");
          string arrow = FrameLogStages.IsTimed(frame) ? "timed-arrow" : "present-arrow";
          canvas.Polygon(new[] { (at - 4.5, cpu + 10), (at + 4.5, cpu + 10), (at, cpu + 16) }, arrow, PresentTip(zero, frame, present));
        }
        if (frame.At("gpuWorkBeginTicks") is { } began)
          canvas.Polyline(new[] { (X(submit?.First ?? began), cpu + 9), (X(began), gpu) }, "join-line");
        if (frame.At("markerDrawTicks") is { } marker)
          canvas.Line(X(marker), cpu, X(marker), cpu + 9, "marker-tick", Tip(zero, $"{tag} marker recorded", marker, null));

        // Where the pacer said to start this frame: the time the frame before was given for its next frame's start
        var before = stretch.Before(offset);
        if (before?.At("nextFrameStartTicks") is { } expected && timeFirst <= expected && expected <= timeLast)
        {
          string started = $"The frame started {Apart(frame.Start - expected)} it.";
          Mark(expected, cpu, "expected", Tip(zero, $"{tag}: the frame start the pacer gave", expected, null, started));
        }

        // The GPU's work, as measured on the CPU clock; without a start and an end, the measured duration from the present on, as a bound
        var screen = FrameLogStages.OnScreen(frames, position + offset, period);
        var work = frame.Span("gpuWorkBeginTicks", "gpuWorkEndTicks");
        long? ready = frame.At("presentReturnTicks");
        if (work is { } worked)
        {
          Box(worked.First, worked.Last, gpu, 12, "gpu", GpuWorkTip(zero, frame, worked));
          Identify(tag, worked.First, worked.Last, gpu + 9.5);
          ready = Math.Max(worked.Last, ready is { } returned and not 0 ? returned : worked.Last);
        }
        else if (frame.At("workGpuTicks") is { } duration and not 0 && frame.At("presentCallTicks") is { } called)
        {
          Box(
            called,
            called + duration,
            gpu + 0.5,
            11,
            "gpu-bound",
            Tip(zero, $"{tag}: the GPU time the application had", called, called + duration, GpuBoundTip)
          );
        }
        if (ready is { } readyAt && screen != null && (screen.Shown - readyAt) * scale >= MinWidth)
          Box(readyAt, screen.Shown, gpu + 2, 8, "queue", Tip(zero, $"{tag} waiting to be shown", readyAt, screen.Shown, WaitingTip));

        // On screen, from the display time the driver reported to the next frame's
        var numbers = new List<string>();
        if (draw is { } drawTime)
          numbers.Add($"CPU {Ms(drawTime.Last - drawTime.First, 2)}");
        if (work is { } workTime)
          numbers.Add($"GPU {Ms(workTime.Last - workTime.First, 2)}");
        canvas.Text(Width - Right + 14, y + 24, string.Join(", ", numbers), "numbers");
        long? error = FrameLogStages.AnimationError(frames, position + offset);
        if (screen != null)
        {
          string style =
            FrameLogStages.HeldLonger(frame, screen.Shown, screen.End, period) ? "screen-long"
            : offset % 2 == 0 ? "screen-a"
            : "screen-b";
          Box(
            screen.Shown,
            Math.Min(screen.End, timeLast),
            screenY,
            12,
            style,
            DisplayTip(zero, frame, screen, period, error, position + offset > 0)
          );
          Identify(tag, screen.Shown, Math.Min(screen.End, timeLast), screenY + 9.5);
          if (screen.UnknownEnd is { } unknownEnd && screen.End < timeLast)
            Box(screen.End, Math.Min(unknownEnd, timeLast), screenY + 0.5, 11, "unknown", UnknownTip(zero, frame, screen, lastReported));
          string judged = error is { } known ? $", animation error {SignedMs(known)} ms" : string.Empty;
          canvas.Text(Width - Right + 14, y + 38, $"shown after {Ms(screen.Shown - frame.Start, 2)} ms{judged}", "numbers");
        }
        else if (frame.At("presentTimingRequested") == 1)
        {
          canvas.Text(Width - Right + 14, y + 38, frame.Index <= lastReported ? "no display time reported" : "not reported yet", "not-shown");
        }
        if (frame.At("intendedDisplayTicks") is { } intended && timeFirst <= intended && intended <= timeLast)
        {
          string landed = screen != null ? $"Shown {Apart(screen.Shown - intended)} it." : string.Empty;
          Mark(intended, screenY, "expected", Tip(zero, $"{tag}: the pacer's intended display time", intended, null, landed));
        }
        if (frame.At("presentTargetRelativeNs") is { } relative and not 0 && before?.Shown is { } shownBefore)
        {
          long asked = shownBefore + FrameLogStages.FloorDivide(relative, 100);
          Mark(
            asked,
            screenY,
            "timed",
            Tip(
              zero,
              $"{tag}: the time its timed present asked for",
              asked,
              null,
              $"{Ms(FrameLogStages.FloorDivide(relative, 100))} ms after #{Whole(before.Index)}'s display time."
            )
          );
        }
      }

      canvas.Text(
        28,
        bottom + 50,
        $"The refresh grid is {grid}. Display times are what the graphics driver reported, not a measurement by the tools.",
        "sub"
      );
      for (int line = 0; line < after.Count; ++line)
        canvas.Text(28, bottom + 70 + (20 * line), after[line], "sub");
      return canvas.Drawing(FrameStagesStyle.Style, FrameStagesStyle.Definitions);
    }
  }
}
