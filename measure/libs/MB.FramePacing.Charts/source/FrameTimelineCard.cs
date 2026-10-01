//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A stretch of a run as one card (shapes: CardDrawing) in the style of mb-framepacing-explained's timing diagram (tools/timing_diagrams/generate_diagrams.py), drawn from the data: the
//* refreshes (bright where a frame could be aimed at its target rate), each frame's CPU work as a box from its CPU start time for its CPU busy
//* (boxes that overlap go to further lanes), its present arrow, what every refresh showed, and each frame's animation time step, display
//* time step and animation error. The CPU times are on the pacer's clock; they are placed on the capture's clock with the markers' intended
//* display times (on-time frames appear at their intended vsync), or without those so that no frame is presented after it appears.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MB.FramePacing.Analysis;
using static MB.FramePacing.Charts.SvgMarkup;

namespace MB.FramePacing.Charts
{
  public static class FrameTimelineCard
  {
    /// <summary>The most frames one card draws; a longer section asks for a shorter one.</summary>
    public const int MaxFrames = 40;

    private const double Left = 180;
    private const double Right = 40;
    private const double MinWidth = 900;
    private const double MinRefreshPixels = 64;
    private const double VsyncY = 98;
    private const double LaneTop = VsyncY + 14;
    private const double LaneH = 44;
    private const double LaneGap = 6;
    private const double RowStep = 26;
    private const double DisplayH = 40;

    /// <summary>The SVG of <paramref name="section"/>: at most <see cref="MaxFrames"/> frames.</summary>
    public static string Render(RunSection section, string? background = null) => SvgCardWriter.Write(Build(section), background);

    /// <summary>The card of <paramref name="section"/> as shapes: at most <see cref="MaxFrames"/> frames.</summary>
    public static CardDrawing Build(RunSection section)
    {
      var chart = section.Run;
      var run = chart.Run;
      var frames = section.Section.Run.Frames;
      if (frames.Count == 0)
        throw new InvalidOperationException("The section has no presented frames");
      if (frames.Count > MaxFrames)
        throw new InvalidOperationException(
          $"The section has {frames.Count} presented frames; the frame timeline draws at most {MaxFrames}: choose a shorter section (--from, --to)"
        );

      var refresh = run.Pacing is { RefreshPeriodMs: > 0 } pacing
        ? new TimeSpan((long)Math.Round(pacing.RefreshPeriodMs * TimeSpan.TicksPerMillisecond))
        : chart.CapturePeriod;
      var (offset, alignedBySchedule) = PacerToCapture(run.Frames);

      // The refresh grid starts at the first frame's display time; each frame's first refresh on it
      var first = frames[0].FirstSeenTime;
      TickCount64 RefreshAt(int k) => first + new TimeSpan(k * refresh.Ticks);
      int RefreshOf(TickCount64 time) => (int)Math.Round((time - first).Ticks / (double)refresh.Ticks);
      var shownAt = frames.Select(f => RefreshOf(f.FirstSeenTime)).ToArray();
      var last = frames[^1];
      int endRefresh = shownAt[^1] + Math.Max(1, (int)Math.Round(last.OnScreen.Ticks / (double)refresh.Ticks));

      // The CPU boxes, on the capture's clock, and the lanes that keep overlapping boxes apart
      var boxes = new List<(int Frame, TickCount64 Start, TickCount64 End, int Lane)>();
      if (offset is { } shift)
      {
        var laneEnds = new List<TickCount64>();
        for (int i = 0; i < frames.Count; ++i)
        {
          var frame = frames[i];
          if (frame.CpuStartTime == default || frame.CpuBusy == TimeSpan32.Zero)
            continue;
          // The CPU start time is on the pacer's clock: the shift puts it on the capture's
          var start = frame.CpuStartTime + shift;
          var stop = start + frame.CpuBusy.ToTimeSpan();
          int lane = laneEnds.FindIndex(e => e <= start);
          if (lane < 0)
          {
            lane = laneEnds.Count;
            laneEnds.Add(stop);
          }
          else
            laneEnds[lane] = stop;
          boxes.Add((i, start, stop, lane));
        }
      }
      int lanes = boxes.Count > 0 ? boxes.Max(b => b.Lane) + 1 : 1;

      // Time runs from the refresh the earliest box starts in (at least one refresh before the first display) to the last frame's end
      int startRefresh = -1;
      if (boxes.Count > 0)
        startRefresh = Math.Min(startRefresh, (int)Math.Floor(boxes.Min(b => (b.Start - first).Ticks) / (double)refresh.Ticks));
      var origin = RefreshAt(startRefresh);
      var end = RefreshAt(endRefresh);
      double refreshMs = refresh.TotalMilliseconds;
      double scale = Math.Max(1.2, MinRefreshPixels / refreshMs);
      double width = Math.Max(MinWidth, Left + ((end - origin).TotalMilliseconds * scale) + Right);
      double XOf(TickCount64 time) => Left + ((time - origin).TotalMilliseconds * scale);

      double laneBottom = LaneTop + (lanes * LaneH) + ((lanes - 1) * LaneGap);
      double arrowY0 = laneBottom + 8;
      double displayY = arrowY0 + 34;
      double axisY = displayY + DisplayH + 20;
      double rowsY = axisY + 36;
      double legendY = rowsY + (RowStep * 3) + 22;
      double height = legendY + 122;

      string title =
        RunHeadline.Title(run)
        + $", frames {frames[0].FrameIndex.ToString(CultureInfo.InvariantCulture)}–{last.FrameIndex.ToString(CultureInfo.InvariantCulture)}"
        + $" ({Fixed(section.FromSeconds, 2)}–{Fixed(section.ToSeconds, 2)} s)";
      var description = new List<string>
      {
        $"{frames.Count} presented frames on a {Fixed(1000 / refreshMs, 2)} Hz display ({Ms(refreshMs)} ms per refresh), one capture per refresh.",
        offset == null ? "The markers carry no CPU start time and CPU busy: only the display side is drawn."
        : alignedBySchedule
          ? "The CPU times are on the pacer's clock, placed on the capture's with the markers' intended display times: on-time frames appear at their intended vsync."
        : "The CPU times are on the pacer's clock; without intended display times they are placed so that no frame is presented after it first appears.",
      };
      if (RunHeadline.SequenceLine(run) is { } sequence)
        description.Add(sequence);
      double headerExtra = description.Count > 2 ? 19 * (description.Count - 2) : 0;
      var header = Header(title, description);
      var parts = new List<CardShape>();

      // Refresh lines: bright where a frame could be aimed at its target rate (whole targets after the previous frame appeared), faint
      // where it could not; a vsync where a frame appeared is always bright (a frame shown sooner than its target appears on one its target
      // skips)
      var targetable = new HashSet<int>(shownAt);
      for (int i = 1; i < frames.Count; ++i)
      {
        int step = Math.Max(1, (int)Math.Round((frames[i].TargetFrameTime ?? refresh).Ticks / (double)refresh.Ticks));
        for (int at = shownAt[i - 1] + step; at <= shownAt[i]; at += step)
          targetable.Add(at);
      }
      for (int k = startRefresh; k <= endRefresh; ++k)
      {
        double x = XOf(RefreshAt(k));
        bool target = targetable.Contains(k);
        parts.Add(new LineShape(target ? "vsync" : "vsync-skip", N(x, 1), N(VsyncY + 6, 0), N(x, 1), N(displayY + DisplayH + 5, 1)));
        parts.Add(new TextShape(x, axisY, $"{Ms(k * refreshMs)} ms", target || k < 0 ? "axis" : "vsync-n-skip"));
        if (k >= 0 && k < endRefresh)
          parts.Add(new TextShape(x, VsyncY, $"vsync {k + 1}", target ? "vsync-target" : "vsync-n-skip"));
      }

      // Row labels
      parts.Add(new TextShape(20, LaneTop + (LaneH / 2) - 3, "CPU", "label", "start"));
      parts.Add(new TextShape(20, LaneTop + (LaneH / 2) + 13, "start + CPU busy", "vsync-n", "start"));
      parts.Add(new TextShape(20, displayY + (DisplayH / 2) + 4, "DISPLAY", "label", "start"));
      string[] rowLabels = { "ANIMATION TIME STEP", "DISPLAY TIME STEP", "ANIMATION ERROR" };
      for (int i = 0; i < rowLabels.Length; ++i)
        parts.Add(new TextShape(20, rowsY + (i * RowStep), rowLabels[i], "label", "start"));

      // CPU boxes and present arrows
      var firstAnimation = frames[0].AnimationTime;
      foreach (var (index, start, stop, lane) in boxes)
      {
        double x0 = XOf(start);
        double x1 = XOf(stop);
        double inset = Math.Min(6, (x1 - x0) / 4);
        double y = LaneTop + (lane * (LaneH + LaneGap));
        parts.Add(new RectShape("box", N(x0 + inset, 1), N(y, 1), N(Math.Max(1, x1 - x0 - (2 * inset)), 1), N(LaneH, 0), "8"));
        // The label as far as the box holds it: the frame and its animation time, the frame alone, or the frame in the smaller font
        double cx = (x0 + x1) / 2;
        double boxWidth = x1 - x0 - (2 * inset);
        if (boxWidth >= 60)
        {
          parts.Add(new TextShape(cx, y + 19, Label(frames[index]), "frame"));
          parts.Add(new TextShape(cx, y + 36, $"{Ms((frames[index].AnimationTime - firstAnimation).TotalMilliseconds)} ms", "box-time"));
        }
        else if (boxWidth >= 38)
          parts.Add(new TextShape(cx, y + 27, Label(frames[index]), "frame"));
        else if (boxWidth >= 26)
          parts.Add(new TextShape(cx, y + 26, Label(frames[index]), "box-time"));
        double tip = displayY - 4;
        parts.Add(new LineShape("arrow", N(x1, 1), N(y + LaneH + 8, 1), N(x1, 1), N(tip - 8, 1)));
        parts.Add(
          new PathShape(
            "arrowhead",
            $"M{Fixed(x1 - 5, 1)},{Fixed(tip - 9, 1)} L{Fixed(x1 + 5, 1)},{Fixed(tip - 9, 1)} L{Fixed(x1, 1)},{Fixed(tip, 1)} z"
          )
        );
      }

      // Display cells: what every refresh showed; each frame's values under its first refresh
      var used = new HashSet<string>();
      var threshold = chart.ErrorThreshold;
      for (int i = 0; i < frames.Count; ++i)
      {
        var frame = frames[i];
        int from = shownAt[i];
        int to = i + 1 < frames.Count ? shownAt[i + 1] : endRefresh;
        // How long the frame is meant to stay: the next frame's target (its swap interval), the last frame's own. A frame presented on
        // demand has no interval to aim for: the wait for it is never late
        var next = i + 1 < frames.Count ? frames[i + 1] : frame;
        var intendedHold = OnDemand(next) ? TimeSpan.MaxValue : next.TargetFrameTime ?? refresh;
        // Nothing animates in a static frame: its refreshes are neither on time nor off, held nor late
        bool isStatic = (frame.Flags & PresentedFrameFlags.StaticAfter) != 0;
        for (int k = from; k < to; ++k)
        {
          bool firstRefresh = k == from;
          string kind =
            isStatic ? (i % 2 == 0 ? "strip-static-a" : "strip-static-b")
            : firstRefresh ? (frame.AnimationError is { } e && e.Duration() > threshold ? "off" : "ok")
            : (k - from) * refresh.Ticks < intendedHold.Ticks - (refresh.Ticks / 2) ? "hold"
            : "again";
          used.Add(kind == "strip-static-b" ? "strip-static-a" : kind);
          double x0 = XOf(RefreshAt(k));
          double x1 = XOf(RefreshAt(k + 1));
          parts.Add(new RectShape(kind, N(x0 + 2, 1), N(displayY, 1), N(x1 - x0 - 4, 1), N(DisplayH, 0), "6"));
          parts.Add(new TextShape((x0 + x1) / 2, displayY + (DisplayH / 2) + 5, Label(frame), kind == "again" ? "cell-text dark-text" : "cell-text"));
        }

        double cx = (XOf(RefreshAt(from)) + XOf(RefreshAt(from + 1))) / 2;
        // A static step (from a frame nothing animated after) has no animation error: the steps it has, and "static" for the error
        if (frame.AnimationError is null && (frame.Flags & PresentedFrameFlags.StaticBefore) != 0)
        {
          parts.Add(new TextShape(cx, rowsY, frame.AnimationDelta is { } a ? $"{Ms(a.TotalMilliseconds)} ms" : "–"));
          parts.Add(new TextShape(cx, rowsY + RowStep, frame.DisplayDelta is { } d ? $"{Ms(d.TotalMilliseconds)} ms" : "–"));
          parts.Add(new TextShape(cx, rowsY + (2 * RowStep), "static", "zero"));
          continue;
        }
        if (frame.DisplayDelta is not { } display || frame.AnimationDelta is not { } animation || frame.AnimationError is not { } error)
        {
          for (int row = 0; row < 3; ++row)
            parts.Add(new TextShape(cx, rowsY + (row * RowStep), "–", row == 2 ? "zero" : ""));
          continue;
        }
        parts.Add(new TextShape(cx, rowsY, $"{Ms(animation.TotalMilliseconds)} ms"));
        parts.Add(new TextShape(cx, rowsY + RowStep, $"{Ms(display.TotalMilliseconds)} ms"));
        // Rounded first, so an error of a tick of rounding reads 0, not +0
        string value = $"{Ms(Math.Round(error.TotalMilliseconds, 1), sign: true)} ms";
        double errorY = rowsY + (2 * RowStep);
        if (error.Duration() > threshold)
        {
          double pill = (value.Length * 7.4) + 18;
          parts.Add(new RectShape("err-pill", N(cx - (pill / 2), 1), N(errorY - 15, 1), N(pill, 1), N(21, 0), "10.5"));
          parts.Add(new TextShape(cx, errorY, value, "err"));
        }
        else
          parts.Add(new TextShape(cx, errorY, value, "zero"));
      }

      Key(parts, used, legendY, threshold);
      header.Add(new GroupShape(headerExtra, parts));
      return new CardDrawing(title, width, height + headerExtra, header, Array.Empty<CardPlot>());
    }

    /// <summary>
    /// The shift from the pacer's clock to the capture's: from the analysis's on-time alignment of the intended display times (display time -
    /// intended display time - lateness), else so that no frame is presented after it first appears; null without CPU start times.
    /// </summary>
    public static (TimeSpan? Offset, bool BySchedule) PacerToCapture(IReadOnlyList<PresentedFrame> frames)
    {
      foreach (var frame in frames)
      {
        if (frame.IntendedDisplayTime != default && frame.Lateness is { } lateness)
          return (frame.FirstSeenTime - frame.IntendedDisplayTime - lateness, true);
      }
      var presented = frames
        .Where(f => f.CpuStartTime != default && f.CpuBusy != TimeSpan32.Zero)
        .Select(f => f.FirstSeenTime - (f.CpuStartTime + f.CpuBusy.ToTimeSpan()))
        .ToList();
      return presented.Count > 0 ? (presented.Min(), false) : (null, false);
    }

    /// <summary>The frame is presented on demand: its target (or, without one, its preferred frame time) says so, as the analysis reads it.</summary>
    private static bool OnDemand(PresentedFrame frame)
    {
      var onDemand = MB.FramePacing.MarkerDecoding.MarkerPayload.OnDemandFrameTime;
      return frame.MarkerTargetFrameTime == onDemand
        || (frame.MarkerTargetFrameTime == TimeSpan32.Zero && frame.MarkerPreferredFrameTime == onDemand);
    }

    /// <summary>A frame's short name in the boxes and cells: the last three digits of its frame index.</summary>
    private static string Label(PresentedFrame frame) => "#" + (frame.FrameIndex % 1000).ToString("000", CultureInfo.InvariantCulture);

    private static readonly (string Kind, string Text)[] g_colours =
    {
      ("ok", "the frame's first refresh, within the error threshold"),
      ("off", "the frame's first refresh, off by more than the error threshold"),
      ("hold", "held as intended (the next frame's target)"),
      ("again", "held longer: the next frame is late"),
      ("strip-static-a", "static: nothing animates"),
    };

    private static void Key(List<CardShape> parts, HashSet<string> used, double legendY, TimeSpan threshold)
    {
      parts.Add(new RectShape("box", N(20, 0), N(legendY - 12, 1), N(30, 0), N(16, 0), "4"));
      parts.Add(
        new TextShape(
          58,
          legendY + 1,
          "CPU: from the CPU start time for CPU busy, labelled with the frame (#last digits of its index) and its animation time",
          "sub",
          "start"
        )
      );
      double arrowX = 35;
      double arrowY = legendY + 26;
      parts.Add(new LineShape("arrow", N(arrowX, 1), N(arrowY - 13, 1), N(arrowX, 1), N(arrowY - 2, 1)));
      parts.Add(
        new PathShape(
          "arrowhead",
          $"M{Fixed(arrowX - 4, 1)},{Fixed(arrowY - 3, 1)} L{Fixed(arrowX + 4, 1)},{Fixed(arrowY - 3, 1)} L{Fixed(arrowX, 1)},{Fixed(arrowY + 4, 1)} z"
        )
      );
      const string present = "present: the frame is handed over and waits for its vsync";
      parts.Add(new TextShape(58, arrowY + 1, present, "sub", "start"));
      double keyX = 58 + (present.Length * 6.9) + 36;
      parts.Add(new LineShape("vsync", N(keyX, 1), N(arrowY - 13, 1), N(keyX, 1), N(arrowY + 4, 1)));
      parts.Add(new LineShape("vsync-skip", N(keyX + 8, 1), N(arrowY - 13, 1), N(keyX + 8, 1), N(arrowY + 4, 1)));
      parts.Add(new TextShape(keyX + 22, arrowY + 1, "vsync: bright can be aimed at the frame's target, faint is skipped", "sub", "start"));
      double coloursY = legendY + 52;
      double x = 20;
      foreach (var (kind, label) in g_colours.Where(c => used.Contains(c.Kind)))
      {
        parts.Add(new RectShape(kind, N(x, 1), N(coloursY - 11, 1), N(14, 0), N(14, 0), "4"));
        parts.Add(new TextShape(x + 22, coloursY + 1, label, "sub", "start"));
        x += 22 + (label.Length * 6.9) + 28;
      }
      parts.Add(
        new TextShape(
          20,
          coloursY + 26,
          $"Animation error = animation time step − display time step: + shown too soon, − shown too late; error threshold {Ms(threshold.TotalMilliseconds)} ms",
          "sub",
          "start"
        )
      );
    }
  }
}
