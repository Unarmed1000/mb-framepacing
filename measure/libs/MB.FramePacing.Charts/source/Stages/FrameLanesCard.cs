//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL. The stages of a stretch of a frame log, a lane per resource (the pacer's expectation, the CPU thread, the GPU, the wait
//* to be shown, the display, the animation clock) and a colour per frame, the same in every lane, so a frame is followed from its start to
//* the screen. Above the CPU thread, where the log has a pacer that was on: the work it was given and the late frames in its frame window.
//* A shape's tip has its times and details.
//*
//* What a log does not have is not drawn, and what it does not know is drawn as not known. Display times are what the graphics driver
//* reported, not a measurement by the tools.
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
  /// <summary>Experimental. A frame log's stages as a chart with a lane per resource and a colour per frame.</summary>
  public static class FrameLanesCard
  {
    private const double Width = FrameStagesCard.Width;
    private const double MinWidth = FrameStagesCard.MinWidth;
    private const double LanesLeft = 168;
    private const double LanesTop = 190;
    private const double ScreenHeight = 34;
    private const double AnimationHeight = 18;

    // Frames wait to be shown at the same time: the waiting lane has a few rows, and a wait takes the first that is free
    private const int QueueRows = 3;
    private const double QueueRowHeight = 11;

    // The rule's defaults (the pacer's settings), which the logs do not state: the frame window's length and the share of late frames it
    // slows down beyond
    private const long FrameWindowTicks = 20_000_000;
    private const long SlowDownLatePercent = 10;

    /// <summary>The chart's SVG.</summary>
    public static string Render(string name, IReadOnlyList<FrameLogFrame> frames, long first, int count, IReadOnlyList<string> notes) =>
      SvgCardWriter.Write(Build(name, frames, first, count, notes));

    /// <summary>
    /// The chart of <paramref name="count"/> frames of the log from frame <paramref name="first"/> on. <paramref name="notes"/> are what
    /// reading the log found (FrameLog.Read).
    /// </summary>
    public static CardDrawing Build(string name, IReadOnlyList<FrameLogFrame> frames, long first, int count, IReadOnlyList<string> notes)
    {
      var stretch = FrameStretch.Of(frames, first, count, LanesLeft, Width - LanesLeft - 28);
      var rows = stretch.Rows;
      long period = stretch.Period;
      double scale = stretch.Scale;
      long lastReported = frames[^1].Index - FrameLog.SkipLast;
      long zero = rows[0].Start;
      int hues = FrameStagesStyle.Hues;
      double X(long ticks) => stretch.X(ticks);

      // The pacer's lanes are there when the log has a pacer that was on, the animation lane when a frame of the chart was shown
      bool paced = rows.Any(frame => frame.At("pacerOn") == 1);
      var rowBefore = stretch.Before(0);
      var tied = FrameLogStages.AnimationTie(rowBefore != null ? rows.Prepend(rowBefore) : rows);
      double expectedY = LanesTop;
      double workY = expectedY + 18;
      double lateY = expectedY + 40;
      double cpuY = paced ? lateY + 58 : expectedY + 34;
      double gpuY = cpuY + 48;
      double queueY = cpuY + 82;
      double screenY = queueY + (QueueRows * QueueRowHeight) + 10;
      double animationY = screenY + ScreenHeight + 8;
      double bottom = tied != null ? animationY + AnimationHeight + 8 : animationY;
      var after = new List<string> { HoverNote, LanesNote, DisplayNote, ErrorNote };
      if (paced)
        after.Add(PacerNote);
      if (tied is { } tie)
      {
        after.Add(AnimationNote(tie.Frame.Index));
        after.Add(AnimationErrorNote);
      }
      after.AddRange(notes);
      double height = bottom + 102 + (20 * after.Count);
      var canvas = new FrameStagesCanvas(Width, height, $"Frame stages: {name}", stretch.Subtitle("A lane per resource, a colour per frame"));

      // The key, left to right in as many lines as it takes: each entry draws its sample at the place it is given
      double place = 28.0;
      double keyY = 92.0;

      // Writes an entry's text and moves on; returns where its sample goes (its left, and the text's baseline)
      (double At, double Line) Entry(string text)
      {
        double needed = 18 + (7.2 * text.Length) + 20;
        if (place + needed > Width - 20)
        {
          place = 28.0;
          keyY += 22;
        }
        double at = place;
        canvas.Text(at + 18, keyY, text, "axis");
        place += needed;
        return (at, keyY);
      }

      foreach (
        var (style, text) in new[]
        {
          ("f0", "one colour per frame"),
          ("f0 soft", "update, acquire"),
          ("wait", "a sleep, or a wait the log does not name"),
          ("wait-vsync", "a vertical blank wait"),
          ("wait-slot", "a frame slot wait"),
          ("u0", "held, not logged"),
          ("submit", "submit"),
        }
      )
      {
        var (at, line) = Entry(text);
        canvas.Rect(at, line - 10, 12, 12, style);
      }
      {
        var (at, line) = Entry("the present call");
        canvas.Line(at + 5, line - 14, at + 5, line + 1, "p0");
        canvas.Polygon(new[] { (at, line), (at + 10, line), (at + 5, line + 6) }, "f0 call");
        (at, line) = Entry("a timed present");
        canvas.Polygon(new[] { (at, line - 6), (at + 10, line - 6), (at + 5, line) }, "timed-call");
      }
      foreach (
        var (style, text) in new[]
        {
          ("q0", "waiting to be shown"),
          ("f0 long", "on screen longer than asked"),
          ("unknown", "not known who was on screen"),
          ("no-time", "no display time reported"),
        }
      )
      {
        var (at, line) = Entry(text);
        canvas.Rect(at, line - 10, 12, 12, style);
      }
      {
        var (at, line) = Entry("animation error of 1 ms or more");
        canvas.Rect(at - 2, line - 11, 16, 13, "err-back", radius: 3);
        canvas.Text(at + 6, line - 1, "±", "err-big", "middle");
        (at, line) = Entry("the time a timed present asked for");
        canvas.Polygon(new[] { (at + 1, line - 8), (at + 11, line - 8), (at + 6, line) }, "timed");
        (at, line) = Entry("marker recorded (CPU)");
        canvas.Line(at + 6, line - 10, at + 6, line + 2, "marker-tick");
      }

      var lanes = new List<(double Y, double Height, string Name)> { (expectedY, 12, "Pacer: expected") };
      if (paced)
      {
        lanes.Add((workY, 16, "Pacer: work it was given"));
        lanes.Add((lateY, 16, "Pacer: late frames in window"));
      }
      lanes.Add((cpuY, 26, "CPU, main thread"));
      lanes.Add((gpuY, 26, "GPU"));
      lanes.Add((queueY, QueueRows * QueueRowHeight, "Waiting to be shown"));
      lanes.Add((screenY, ScreenHeight, "Display"));
      if (tied != null)
        lanes.Add((animationY, AnimationHeight, "By its animation time"));
      foreach (var (y, laneHeight, laneName) in lanes)
      {
        canvas.Rect(LanesLeft, y, stretch.PlotWidth, laneHeight, "lane");
        canvas.Text(LanesLeft - 12, y + (laneHeight / 2) + 4, laneName, "lane-name", "end");
      }
      string grid = stretch.DrawGrid(canvas, LanesTop - 8, bottom);
      stretch.DrawAxis(canvas, bottom);

      // Draws the box and returns its width
      double Box(long firstTick, long lastTick, double y, double boxHeight, string style, string about = "")
      {
        double width = Math.Max(X(lastTick) - X(firstTick), MinWidth);
        canvas.Rect(X(firstTick), y, width, boxHeight, style, tip: about);
        return width;
      }

      void Label(string text, long firstTick, double width, double y, string style = "on")
      {
        if (width >= (7 * text.Length) + 6)
          canvas.Text(X(firstTick) + (width / 2), y, text, style, "middle");
      }

      // The animation error in a shown frame's box, with its unit where that fits; one that stands out on a dark plate
      void ErrorLabel(long error, long firstTick, double width, double y)
      {
        string? text = new[] { $"{SignedMs(error)} ms", SignedMs(error) }.FirstOrDefault(candidate => width >= (6 * candidate.Length) + 8);
        if (text == null)
          return;
        double middle = X(firstTick) + (width / 2);
        bool large = Math.Abs(error) >= FrameLogStages.ErrorThresholdTicks;
        if (large)
        {
          double plate = (6 * text.Length) + 8;
          canvas.Rect(middle - (plate / 2), y - 10, plate, 13, "err-back", radius: 3);
        }
        canvas.Text(middle, y, text, large ? "err-big" : "err", "middle");
      }

      var freeFrom = Enumerable.Repeat(stretch.TimeFirst, QueueRows).ToArray();

      // The y of the waiting lane's first row that is free at firstTick (else the one free soonest), taken until lastTick
      double QueueRow(long firstTick, long lastTick)
      {
        int row = Array.FindIndex(freeFrom, free => free <= firstTick);
        if (row < 0)
          row = Array.IndexOf(freeFrom, freeFrom.Min());
        freeFrom[row] = lastTick;
        return queueY + (row * QueueRowHeight);
      }

      double spacing = (rows[^1].Start - rows[0].Start) * scale / Math.Max(rows.Count - 1, 1);
      long nameEvery = spacing > 0 ? Math.Max(1, (long)Math.Round(34 / spacing)) : 1;
      var cpuWork = new List<long>();
      var gpuWork = new List<long>();
      var toShown = new List<long>();
      var errors = new List<long>();
      int unreported = 0;
      int longer = 0;
      for (int offset = 0; offset < rows.Count; ++offset)
      {
        var frame = rows[offset];
        int position = stretch.Position + offset;
        // A frame's colour is its index's: the same in every chart of the run
        int hue = (int)(((frame.Index % hues) + hues) % hues);
        string tag = $"#{Whole(frame.Index)}";

        // What the pacer expected: from the start it gave the frame to the display time it aimed for
        var before = stretch.Before(offset);
        long? expected = before?.At("nextFrameStartTicks");
        long? intended = frame.At("intendedDisplayTicks");
        if (expected is { } from && intended is { } aim && aim > from && stretch.Inside(from))
        {
          string started = $"The frame started {Apart(frame.Start - from)} the start the pacer gave it.";
          string about = Tip(zero, $"{tag} as the pacer expected it: from its frame start to its display time", from, aim, started);
          Box(from, Math.Min(aim, stretch.TimeLast), expectedY, 12, $"e{hue}", about);
        }

        // What the pacer was told the frame needed, as a length of time from the frame's start, in the frame time it had (the box)
        long? frameTime = frame.At("targetFrameTimeTicks") is { } target and not 0 ? target : null;
        if (paced && frameTime is { } time && frame.At("workCpuTicks") is { } workCpu)
        {
          long workGpu = frame.At("workGpuTicks") ?? 0;
          long given = workCpu + workGpu;
          string percent = Whole(FrameLogStages.FloorDivide(100 * given, time));
          string about = Tip(
            zero,
            $"{tag}: the work the pacer was given, {percent} % of its frame time",
            frame.Start,
            frame.Start + given,
            $"CPU {Ms(workCpu)} ms + GPU {Ms(workGpu)} ms (the newest GPU time the application had: "
              + (frame.At("workGpuFrameIndex") is { } measuredOn ? $"frame #{Whole(measuredOn)}'s)." : "an earlier frame's)."),
            $"Frame time {Ms(time)} ms (the outline)."
          );
          Box(frame.Start, Math.Min(frame.Start + time, stretch.TimeLast), workY, 16, "slot");
          double width = Box(frame.Start, Math.Min(frame.Start + given, stretch.TimeLast), workY + 3, 10, given > time ? "over" : $"f{hue}", about);
          Label($"{percent} %", frame.Start, width, workY + 12);
        }

        // How many late frames its frame window held when this frame began, against the count that makes the rule slow down
        long following = position + 1 < frames.Count ? frames[position + 1].Start : frame.Start + (frameTime ?? period);
        if (paced && frame.At("pacerWindowLateFrames") is { } late && period > 0)
        {
          long preferred = Math.Max(frame.At("preferredSwapInterval") is { } wanted and not 0 ? wanted : 1, 1);
          long limit =
            FrameLogStages.FloorDivide(
              SlowDownLatePercent
                * FrameLogStages.FloorDivide(FrameWindowTicks + FrameLogStages.FloorDivide(period * preferred, 2), period * preferred),
              100
            ) + 1;
          string style =
            late >= limit ? "late-full"
            : 2 * late >= limit ? "late-high"
            : "late-low";
          double bar = Math.Max(16 * Math.Min(late / (double)limit, 1.0), 1.0);
          string about = Tip(
            zero,
            $"{tag}: {Whole(late)} late frames in the pacer's frame window of {Whole(frame.At("pacerWindowFrames") ?? 0)} frames",
            frame.Start,
            null,
            $"The rule slows down at {Whole(limit)} (its default settings, at this swap interval)."
          );
          canvas.Rect(
            X(frame.Start),
            lateY + 16 - bar,
            Math.Max(X(Math.Min(following, stretch.TimeLast)) - X(frame.Start), MinWidth),
            bar,
            style,
            tip: about
          );
          if (offset == rows.Count - 1)
            canvas.Text(X(Math.Min(following, stretch.TimeLast)) + 6, lateY + 12, $"{Whole(late)} of {Whole(limit)}", "limit-text");
        }
        if (paced && frame.At("pacerChange") is { } change and not 0)
        {
          double at = X(frame.Start);
          canvas.Line(at, expectedY - 4, at, lateY + 34, "change");
          canvas.Text(at + 5, lateY + 30, $"{(change == 1 ? "slower" : "faster")}: swap interval {Whole(frame.SwapInterval)}", "change-text");
        }

        // The CPU thread: update, the wait for the frame start, acquire, draw, a hold before the present, the present call
        if (frame.Span("hostCpuStartTicks", "hostUpdateEndTicks") is { } update)
          Box(update.First, update.Last, cpuY, 26, $"f{hue} soft", Tip(zero, $"{tag} update", update.First, update.Last));
        if (FrameLogStages.StartWait(frame) is { } wait && (wait.Last - wait.First) * scale >= MinWidth)
          Box(
            wait.First,
            wait.Last,
            cpuY + 7,
            12,
            HoldStyle(frame),
            WaitTip(zero, $"{tag} wait for its frame start", wait, frame.At("frameWaitTargetTicks"), frame)
          );
        if (frame.Span("frameSlotWaitBeginTicks", "frameSlotWaitEndTicks") is { } slot && (slot.Last - slot.First) * scale >= MinWidth)
          Box(slot.First, slot.Last, cpuY + 7, 12, "wait-slot", Tip(zero, $"{tag} wait for a frame slot", slot.First, slot.Last, SlotWaitTip));
        if (frame.Span("acquireCallTicks", "acquireReturnTicks") is { } acquire)
          Box(acquire.First, acquire.Last, cpuY, 26, $"f{hue} soft", AcquireTip(zero, frame, acquire));
        if (FrameLogStages.UnloggedHold(frame) is { } held && (held.Last - held.First) * scale >= 3)
          Box(held.First, held.Last, cpuY + 7, 12, $"u{hue}", Tip(zero, $"{tag} held, no times logged", held.First, held.Last, UnloggedTip));
        // The frame's whole time on the CPU thread, as a strip over the lane: it shows a frame whose work is in two parts
        if (FrameLogStages.CpuSpan(frame) is { } span)
          Box(
            span.First,
            span.Last,
            cpuY - 4,
            3,
            $"s{hue}",
            Tip(zero, $"{tag} on the CPU thread, from the first thing done for it to the last", span.First, span.Last)
          );
        if (frame.Span("frameStartTicks", "endFrameTicks") is { } draw)
        {
          Label(tag, draw.First, Box(draw.First, draw.Last, cpuY, 26, $"f{hue}", DrawTip(zero, frame, draw)), cpuY + 17);
          cpuWork.Add(draw.Last - draw.First);
        }
        var submit = frame.Span("submitCallTicks", "submitReturnTicks");
        if (submit is { } submitted)
          Box(
            submitted.First,
            submitted.Last,
            cpuY,
            26,
            "submit",
            Tip(zero, $"{tag} submit of its GPU work (call to return)", submitted.First, submitted.Last)
          );
        if (FrameLogStages.PresentHold(frame) is { } hold && (hold.Last - hold.First) * scale >= MinWidth)
          Box(
            hold.First,
            hold.Last,
            cpuY + 7,
            12,
            HoldStyle(frame),
            WaitTip(zero, $"{tag} hold before its present", hold, frame.At("presentWaitTargetTicks"), frame)
          );
        (double X, double Y)? handedOver = null;
        var present = frame.Span("presentCallTicks", "presentReturnTicks");
        if (present is { } presented)
        {
          double at = X(presented.First);
          string call = FrameLogStages.IsTimed(frame) ? "timed-call" : $"f{hue} call";
          Box(presented.First, presented.Last, cpuY, 26, call, PresentTip(zero, frame, presented));
          canvas.Line(at, cpuY - 6, at, cpuY + 30, $"p{hue}");
          canvas.Polygon(new[] { (at - 5.5, cpuY + 29), (at + 5.5, cpuY + 29), (at, cpuY + 37) }, call, PresentTip(zero, frame, presented));
          handedOver = (at, cpuY + 37);
        }
        if (frame.At("markerDrawTicks") is { } marker)
          canvas.Line(X(marker), cpuY + 3, X(marker), cpuY + 23, "marker-tick", Tip(zero, $"{tag} marker recorded", marker, null));
        if (offset % nameEvery == 0)
          canvas.Text(X(frame.Start), cpuY - 6, tag, $"t{hue}", "middle");

        // The GPU, and the wait to be shown. The thin lines follow the frame, by one rule each. To its GPU work: from the CPU thread, at
        // its submit, and in a log without one straight down where the GPU began. To its place in the wait to be shown: from its present
        // call, straight down, and along its row to where the wait begins. To the start of that wait: from the end of its GPU work
        var screen = FrameLogStages.OnScreen(frames, position, period);
        var work = frame.Span("gpuWorkBeginTicks", "gpuWorkEndTicks");
        long? ready = frame.At("presentReturnTicks");
        if (work is { } worked)
        {
          Label(tag, worked.First, Box(worked.First, worked.Last, gpuY, 26, $"f{hue}", GpuWorkTip(zero, frame, worked)), gpuY + 17);
          gpuWork.Add(worked.Last - worked.First);
          ready = Math.Max(worked.Last, ready is { } returned and not 0 ? returned : worked.Last);
          canvas.Polyline(new[] { (X(submit?.First ?? worked.First), cpuY + 26), (X(worked.First), gpuY) }, $"l{hue}");
        }
        // The frame's row of the waiting lane, taken from its present call (it is queued from then on) to its display time; a frame
        // without a display time takes the room its mark needs
        long? queued = present is { } call1 && ready is { } readyNow ? Math.Min(call1.First, readyNow) : ready;
        bool noTime = screen == null && frame.At("presentTimingRequested") == 1 && frame.Index <= lastReported;
        double? waiting = null;
        if (ready is { } readyAt && queued is { } queuedAt && screen != null && (screen.Shown - readyAt) * scale >= MinWidth)
        {
          waiting = QueueRow(queuedAt, screen.Shown);
          string about = Tip(zero, $"{tag} waiting to be shown", readyAt, screen.Shown, WaitingTip);
          Box(readyAt, screen.Shown, waiting.Value + 1, QueueRowHeight - 2, $"q{hue}", about);
          if (work is { } done)
            canvas.Polyline(new[] { (X(done.Last), gpuY + 26), (X(readyAt), waiting.Value + 1) }, $"l{hue}");
        }
        else if (noTime && ready is { } readyNoTime && queued is { } queuedNoTime)
        {
          waiting = QueueRow(queuedNoTime, readyNoTime + (long)Math.Round(56 / scale));
        }
        else if (work is { } done && screen != null)
        {
          canvas.Polyline(new[] { (X(done.Last), gpuY + 26), (X(done.Last), queueY + 1) }, $"l{hue}");
        }
        if (handedOver is { } handed && ready is { } readyLine && (waiting != null || screen != null))
        {
          double middle = (waiting ?? queueY) + (QueueRowHeight / 2);
          canvas.Polyline(new[] { handed, (handed.X, middle), (Math.Max(X(readyLine), handed.X), middle) }, $"l{hue}");
        }

        // The display: on screen from the reported display time to the next frame's, and the animation error of the step into it
        long? error = FrameLogStages.AnimationError(frames, position);
        if (screen != null)
        {
          bool isLonger = FrameLogStages.HeldLonger(frame, screen.Shown, screen.End, period);
          longer += isLonger ? 1 : 0;
          string about = DisplayTip(zero, frame, screen, period, error, position > 0);
          double width = Box(
            screen.Shown,
            Math.Min(screen.End, stretch.TimeLast),
            screenY,
            ScreenHeight,
            isLonger ? $"f{hue} long" : $"f{hue}",
            about
          );
          Label(tag, screen.Shown, width, screenY + 14);
          if (error is { } known)
          {
            ErrorLabel(known, screen.Shown, width, screenY + 28);
            errors.Add(known);
          }
          if (screen.UnknownEnd is { } unknownEnd && screen.End < stretch.TimeLast)
          {
            about = UnknownTip(zero, frame, screen, lastReported);
            width = Box(screen.End, Math.Min(unknownEnd, stretch.TimeLast), screenY + 0.5, ScreenHeight - 1, "unknown", about);
            Label("?", screen.End, width, screenY + 21, "unknown-text");
          }
          canvas.Polyline(new[] { (X(screen.Shown), (waiting ?? queueY) + QueueRowHeight - 1), (X(screen.Shown), screenY) }, $"l{hue}");
          toShown.Add(screen.Shown - frame.Start);
        }
        else if (noTime && ready is { } readyMark && waiting is { } lost)
        {
          ++unreported;
          // A mark where its wait would have begun, with room for its number
          double at = X(readyMark) + 3;
          string about = Tip(
            zero,
            $"{tag}: no display time reported",
            readyMark,
            null,
            FrameLogStages.NoDisplayTime(frame, lastReported),
            "Whether it was shown takes a capture to know."
          );
          canvas.Rect(at, lost + 1.5, 8, 8, "no-time", tip: about);
          canvas.Text(at + 12, lost + 9.5, tag, "not-shown");
        }
        if (
          frame.At("presentTargetRelativeNs") is { } relative and not 0
          && before?.Shown is { } shownBefore
          && stretch.Inside(shownBefore + FrameLogStages.FloorDivide(relative, 100))
        )
        {
          long asked = shownBefore + FrameLogStages.FloorDivide(relative, 100);
          string about = Tip(
            zero,
            $"{tag}: the time its timed present asked for",
            asked,
            null,
            $"{Ms(FrameLogStages.FloorDivide(relative, 100))} ms after #{Whole(before.Index)}'s display time."
          );
          canvas.Polygon(new[] { (X(asked) - 5, screenY - 8), (X(asked) + 5, screenY - 8), (X(asked), screenY) }, "timed", about);
        }

        // Where the animation clock has the frame: from its animation time to the next frame's
        if (tied is { } tying && frame.At("animationTimeTicks") is { } animation)
        {
          long? later = position + 1 < frames.Count ? frames[position + 1].At("animationTimeTicks") : null;
          long begins = animation + tying.Offset;
          long ends = later is { } next ? next + tying.Offset : begins + (frame.SwapInterval * period);
          if (ends > stretch.TimeFirst && begins < stretch.TimeLast)
          {
            long beginsInside = Math.Max(begins, stretch.TimeFirst);
            long endsInside = Math.Min(ends, stretch.TimeLast);
            string landed = screen != null ? $"Shown {Apart(screen.Shown - begins)} this time." : "The log has no display time for it.";
            string about = Tip(
              zero,
              $"{tag} by its animation time: when the animation expected it on screen",
              begins,
              ends,
              landed,
              AnimationTip(tying.Frame.Index)
            );
            Label(tag, beginsInside, Box(beginsInside, endsInside, animationY, AnimationHeight, $"e{hue}", about), animationY + 13, $"t{hue}");
          }
        }
      }

      int large = errors.Count(error => Math.Abs(error) >= FrameLogStages.ErrorThresholdTicks);
      string summary =
        $"In this stretch, at the median: the draw takes {MedianMs(cpuWork)} ms on the CPU, the GPU works {MedianMs(gpuWork)} ms on a frame, "
        + $"and a frame is shown {MedianMs(toShown)} ms after its start. {longer} on screen longer than asked, {large} of {errors.Count} animation errors "
        + $"of 1 ms or more, {unreported} reported without a display time.";
      canvas.Text(28, bottom + 50, summary, "axis");
      canvas.Text(
        28,
        bottom + 72,
        $"The refresh grid is {grid}. Display times are what the graphics driver reported, not a measurement by the tools.",
        "sub"
      );
      for (int line = 0; line < after.Count; ++line)
        canvas.Text(28, bottom + 92 + (20 * line), after[line], "sub");
      return canvas.Drawing(
        FrameStagesStyle.Style + FrameStagesStyle.LaneStyle,
        FrameStagesStyle.Definitions.Concat(FrameStagesStyle.LaneDefinitions).ToList()
      );
    }

    private static string MedianMs(IReadOnlyList<long> values) => values.Count > 0 ? Ms((long)FrameLogStages.Median(values), 2) : "-";
  }
}
