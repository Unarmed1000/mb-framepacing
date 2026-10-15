//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A stretch of a run as one card (shapes: CardDrawing) in the style of mb-framepacing-explained's timing diagram (tools/timing_diagrams/generate_diagrams.py), drawn from the data: the
//* refreshes (bright where a frame could be aimed at its target rate), each frame's CPU work as a box from its CPU start time for its CPU busy
//* (boxes that overlap go to further lanes), its present arrow, what every refresh showed, and each frame's animation time step, display
//* time step and animation error. The CPU times are on the pacer's clock; they are placed on the capture's clock with the markers' intended
//* display times (on-time frames appear at their intended vsync), or without those so that no frame is presented after it appears. A
//* frame's first refresh is coloured as the report's refresh strip colours it (late red, delta time jitter yellow, else green) and its
//* error as the report's error bars are; a card for a document takes its own title, description lines and frame labels
//* (FrameTimelineOptions).
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

    /// <summary>The SVG of <paramref name="section"/>: at most <see cref="MaxFrames"/> frames, laid out as <paramref name="options"/> say.</summary>
    public static string Render(RunSection section, FrameTimelineOptions? options = null, string? background = null) =>
      SvgCardWriter.Write(Build(section, options), background);

    /// <summary>The card of <paramref name="section"/> as shapes: at most <see cref="MaxFrames"/> frames, laid out as <paramref name="options"/> say.</summary>
    public static CardDrawing Build(RunSection section, FrameTimelineOptions? options = null)
    {
      options ??= FrameTimelineOptions.Default;
      var chart = section.Run;
      var run = chart.Run;
      var frames = section.Section.Run.Frames;
      if (frames.Count == 0)
        throw new InvalidOperationException("The section has no presented frames");
      if (frames.Count > MaxFrames)
        throw new InvalidOperationException(
          $"The section has {frames.Count} presented frames; the frame timeline draws at most {MaxFrames}: choose a shorter section (--from, --to)"
        );
      if (options.RefreshPixels is { } asked && (asked < FrameTimelineOptions.MinRefreshPixels || asked > FrameTimelineOptions.MaxRefreshPixels))
        throw new InvalidOperationException(
          $"A refresh {asked} pixels wide: give {FrameTimelineOptions.MinRefreshPixels} to {FrameTimelineOptions.MaxRefreshPixels}"
        );
      if (options.FrameLabels is { } labels && labels.Count != frames.Count)
        throw new InvalidOperationException(
          $"{labels.Count} frame labels for a section of {frames.Count} presented frames: give one label per frame, in order"
        );
      string LabelOf(int index) => options.FrameLabels is { } given ? given[index] : Label(frames[index]);

      var refresh = run.Pacing is { } pacing && pacing.RefreshPeriod > NanosecondTimeDuration.Zero ? pacing.RefreshPeriod : chart.CapturePeriod;
      var (offset, alignedBySchedule) = PacerToCapture(run.Frames);

      // The refresh grid starts at the first frame's display time; each frame's first refresh on it
      var first = frames[0].FirstSeenTime;
      NanosecondTickCount RefreshAt(int k) => first + new NanosecondTimeSpan(k * refresh.Nanoseconds);
      int RefreshOf(NanosecondTickCount time) => (int)Math.Round((time - first).Nanoseconds / (double)refresh.Nanoseconds);
      var shownAt = frames.Select(f => RefreshOf(f.FirstSeenTime)).ToArray();
      var last = frames[^1];
      int endRefresh = shownAt[^1] + Math.Max(1, (int)Math.Round(last.OnScreen.Nanoseconds / (double)refresh.Nanoseconds));

      // The CPU boxes, on the capture's clock, and the lanes that keep overlapping boxes apart
      var boxes = new List<(int Frame, NanosecondTickCount Start, NanosecondTickCount End, int Lane)>();
      if (offset is { } shift)
      {
        var laneEnds = new List<NanosecondTickCount>();
        for (int i = 0; i < frames.Count; ++i)
        {
          var frame = frames[i];
          if (frame.CpuStartTime == default || frame.CpuBusy == NanosecondTimeDuration.Zero)
            continue;
          // The CPU start time is on the pacer's clock: the shift puts it on the capture's
          var start = frame.CpuStartTime + shift;
          var stop = start + frame.CpuBusy;
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
        startRefresh = Math.Min(startRefresh, (int)Math.Floor(boxes.Min(b => (b.Start - first).Nanoseconds) / (double)refresh.Nanoseconds));
      var origin = RefreshAt(startRefresh);
      var end = RefreshAt(endRefresh);
      double refreshMs = refresh.Value.TotalMilliseconds;
      // Pixels per millisecond: a refresh 64 pixels wide or more, or as wide as the card for a document asks
      double scale = options.RefreshPixels is { } pixels ? pixels / refreshMs : Math.Max(1.2, MinRefreshPixels / refreshMs);
      double width = Math.Max(MinWidth, Left + ((end - origin).TotalMilliseconds * scale) + Right);
      double XOf(NanosecondTickCount time) => Left + ((time - origin).TotalMilliseconds * scale);

      double laneBottom = LaneTop + (lanes * LaneH) + ((lanes - 1) * LaneGap);
      double arrowY0 = laneBottom + 8;
      double displayY = arrowY0 + 34;
      double axisY = displayY + DisplayH + 20;
      double rowsY = axisY + 36;
      double legendY = rowsY + (RowStep * 3) + 22;
      double height = legendY + 122;

      string title =
        (options.Title ?? RunHeadline.Title(run))
        + (
          options.TitleRange
            ? $", frames {frames[0].FrameIndex.ToString(CultureInfo.InvariantCulture)}–{last.FrameIndex.ToString(CultureInfo.InvariantCulture)}"
              + $" ({N(section.FromSeconds, 2)}–{N(section.ToSeconds, 2)} s)"
            : string.Empty
        );
      var description = new List<string>
      {
        $"{frames.Count} presented frames on a {N(1000 / refreshMs, 2)} Hz display ({Ms(refreshMs)} ms per refresh), one capture per refresh.",
        offset == null ? "The markers carry no CPU start time and CPU busy: only the display side is drawn."
        : alignedBySchedule
          ? "The CPU times are on the pacer's clock, placed on the capture's with the markers' intended display times: on-time frames appear at their intended vsync."
        : "The CPU times are on the pacer's clock; without intended display times they are placed so that no frame is presented after it first appears.",
      };
      if (RunHeadline.SequenceLine(run) is { } sequence)
        description.Add(sequence);
      // A card in a document says what the document wants instead: the diagrams have their title and their own lines only
      if (options.Description is { } lines)
        description = lines.ToList();
      double headerExtra = description.Count > 2 ? 19 * (description.Count - 2) : 0;
      var header = Header(title, description);
      var parts = new List<CardShape>();

      // Each frame's intended period, as the late verdict measures it (IntendedPeriods: the marker's target frame time
      // first), from the frames the target dropped before each frame of the whole run
      var dropped = DroppedFrames.Before(run.Frames);
      NanosecondTimeDuration? IntendedPeriod(int index) => IntendedPeriods.Of(frames[index], dropped[section.Start + index], refresh);

      // Refresh lines: bright where a frame could be aimed at its intended period (whole periods after the previous frame appeared), faint
      // where it could not; a vsync where a frame appeared is always bright (a frame shown sooner than its period appears on one the period
      // skips)
      var targetable = new HashSet<int>(shownAt);
      for (int i = 1; i < frames.Count; ++i)
      {
        int step = Math.Max(1, (int)Math.Round((IntendedPeriod(i) ?? refresh).Nanoseconds / (double)refresh.Nanoseconds));
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
      parts.Add(new TextShape(20, LaneTop + (LaneH / 2) - 3, options.CpuRowLabel ?? "CPU", "label", "start"));
      parts.Add(new TextShape(20, LaneTop + (LaneH / 2) + 13, options.CpuRowSubLabel ?? "start + CPU busy", "vsync-n", "start"));
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
          parts.Add(new TextShape(cx, y + 19, LabelOf(index), "frame"));
          parts.Add(new TextShape(cx, y + 36, $"{Ms((frames[index].AnimationTime - firstAnimation).TotalMilliseconds)} ms", "box-time"));
        }
        else if (boxWidth >= 38)
          parts.Add(new TextShape(cx, y + 27, LabelOf(index), "frame"));
        else if (boxWidth >= 26)
          parts.Add(new TextShape(cx, y + 26, LabelOf(index), "box-time"));
        double tip = displayY - 4;
        parts.Add(new LineShape("arrow", N(x1, 1), N(y + LaneH + 8, 1), N(x1, 1), N(tip - 8, 1)));
        parts.Add(new PathShape("arrowhead", $"M{N(x1 - 5, 1)},{N(tip - 9, 1)} L{N(x1 + 5, 1)},{N(tip - 9, 1)} L{N(x1, 1)},{N(tip, 1)} z"));
      }

      // Display cells: what every refresh showed; each frame's values under its first refresh
      var used = new HashSet<string>();
      var threshold = chart.ErrorThreshold;
      for (int i = 0; i < frames.Count; ++i)
      {
        var frame = frames[i];
        int from = shownAt[i];
        int to = i + 1 < frames.Count ? shownAt[i + 1] : endRefresh;
        // How long the frame is meant to stay: as long as it does, unless the next frame is late. Then it is the next frame's intended
        // period (what its lateness is measured against), and the refreshes beyond it are the ones it was held longer
        var next = i + 1 < frames.Count ? frames[i + 1] : null;
        var intendedHold =
          next != null && (next.Flags & PresentedFrameFlags.Late) != 0 && IntendedPeriod(i + 1) is { } period
            ? period.Value
            : NanosecondTimeSpan.MaxValue;
        // Nothing animates in a static frame: its refreshes are neither on time nor off, held nor late
        bool isStatic = (frame.Flags & PresentedFrameFlags.StaticAfter) != 0;
        // The first refresh is coloured as the report's refresh strip colours it: late red, off by delta time jitter (the display was even,
        // the timer was not, by less than the intended period) yellow, any other frame green; its error as the report's error bars are
        bool isLate = (frame.Flags & PresentedFrameFlags.Late) != 0;
        bool isJitter = (frame.Flags & PresentedFrameFlags.DeltaTimeJitter) != 0;
        for (int k = from; k < to; ++k)
        {
          bool firstRefresh = k == from;
          string kind =
            isStatic ? (i % 2 == 0 ? "strip-static-a" : "strip-static-b")
            : firstRefresh
              ? (
                isLate ? "off"
                : isJitter ? "off-jitter"
                : "ok"
              )
            : (k - from) * refresh.Nanoseconds < intendedHold.Nanoseconds - (refresh.Nanoseconds / 2) ? "hold"
            : "again";
          used.Add(kind == "strip-static-b" ? "strip-static-a" : kind);
          double x0 = XOf(RefreshAt(k));
          double x1 = XOf(RefreshAt(k + 1));
          parts.Add(new RectShape(kind, N(x0 + 2, 1), N(displayY, 1), N(x1 - x0 - 4, 1), N(DisplayH, 0), "6"));
          parts.Add(
            new TextShape(
              (x0 + x1) / 2,
              displayY + (DisplayH / 2) + 5,
              LabelOf(i),
              kind is "again" or "off-jitter" ? "cell-text dark-text" : "cell-text"
            )
          );
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
        // Rounded first, so an error that is only the rounding of the times reads 0, not +0
        string value = $"{Ms(Math.Round(error.TotalMilliseconds, 1), sign: true)} ms";
        double errorY = rowsY + (2 * RowStep);
        if (error.Duration() > threshold)
        {
          double pill = (value.Length * 7.4) + 18;
          parts.Add(new RectShape(isJitter ? "err-pill-jitter" : "err-pill", N(cx - (pill / 2), 1), N(errorY - 15, 1), N(pill, 1), N(21, 0), "10.5"));
          parts.Add(new TextShape(cx, errorY, value, isJitter ? "err-jitter" : "err"));
        }
        else
          parts.Add(new TextShape(cx, errorY, value, "zero"));
      }

      double keyExtra = Key(parts, used, legendY, width, options.ThresholdInKey ? threshold : null, options.FrameLabels != null);
      header.Add(new GroupShape(headerExtra, parts));
      return new CardDrawing(title, width, height + headerExtra + keyExtra, header, Array.Empty<CardPlot>());
    }

    /// <summary>
    /// The shift from the pacer's clock to the capture's: from the analysis's on-time alignment of the intended display times (display time -
    /// intended display time - lateness), else so that no frame is presented after it first appears; null without CPU start times.
    /// </summary>
    public static (NanosecondTimeSpan? Offset, bool BySchedule) PacerToCapture(IReadOnlyList<PresentedFrame> frames)
    {
      foreach (var frame in frames)
      {
        if (frame.IntendedDisplayTime != default && frame.Lateness is { } lateness)
          return (frame.FirstSeenTime - frame.IntendedDisplayTime - lateness, true);
      }
      var presented = frames
        .Where(f => f.CpuStartTime != default && f.CpuBusy != NanosecondTimeDuration.Zero)
        .Select(f => f.FirstSeenTime - (f.CpuStartTime + f.CpuBusy))
        .ToList();
      return presented.Count > 0 ? (presented.Min(), false) : (null, false);
    }

    /// <summary>A frame's short name in the boxes and cells: the last three digits of its frame index.</summary>
    private static string Label(PresentedFrame frame) => "#" + (frame.FrameIndex % 1000).ToString("000", CultureInfo.InvariantCulture);

    private static readonly (string Kind, string Text)[] g_colours =
    {
      ("ok", "the frame's first refresh, neither late nor off by delta time jitter"),
      ("off", "the frame's first refresh, late: longer than animated for and than intended"),
      ("off-jitter", "the frame's first refresh, off by delta time jitter: less than the intended period"),
      ("hold", "held as intended"),
      ("again", "held longer: the next frame is late"),
      ("strip-static-a", "static: nothing animates"),
    };

    /// <summary>
    /// The key under the card, its entries going on to a further line where the card is too narrow for them: returns how much taller that
    /// makes it than a key of one line per row.
    /// </summary>
    private static double Key(
      List<CardShape> parts,
      HashSet<string> used,
      double legendY,
      double width,
      NanosecondTimeDuration? threshold,
      bool labelsGiven
    )
    {
      // An entry's text is about 6.9 pixels a character in the key's font, as the rest of the card reckons it
      const double CharWidth = 6.9;
      const double LineStep = 22;
      double right = width - 20;
      parts.Add(new RectShape("box", N(20, 0), N(legendY - 12, 1), N(30, 0), N(16, 0), "4"));
      parts.Add(
        new TextShape(
          58,
          legendY + 1,
          labelsGiven
            ? "CPU: from the CPU start time for CPU busy, labelled with the frame and its animation time"
            : "CPU: from the CPU start time for CPU busy, labelled with the frame (#last digits of its index) and its animation time",
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
          $"M{N(arrowX - 4, 1)},{N(arrowY - 3, 1)} L{N(arrowX + 4, 1)},{N(arrowY - 3, 1)} L{N(arrowX, 1)},{N(arrowY + 4, 1)} z"
        )
      );
      const string present = "present: the frame is handed over and waits for its vsync";
      parts.Add(new TextShape(58, arrowY + 1, present, "sub", "start"));
      const string vsync = "vsync: bright can be aimed at the frame's target, faint is skipped";
      double keyX = 58 + (present.Length * CharWidth) + 36;
      double vsyncY = arrowY;
      if (keyX + 22 + (vsync.Length * CharWidth) > right)
      {
        // On a line of its own, its lines under the present arrow's
        keyX = arrowX - 4;
        vsyncY = arrowY + 26;
      }
      parts.Add(new LineShape("vsync", N(keyX, 1), N(vsyncY - 13, 1), N(keyX, 1), N(vsyncY + 4, 1)));
      parts.Add(new LineShape("vsync-skip", N(keyX + 8, 1), N(vsyncY - 13, 1), N(keyX + 8, 1), N(vsyncY + 4, 1)));
      parts.Add(new TextShape(keyX + 22, vsyncY + 1, vsync, "sub", "start"));
      double coloursY = vsyncY + 26;
      double x = 20;
      foreach (var (kind, label) in g_colours.Where(c => used.Contains(c.Kind)))
      {
        double entry = 22 + (label.Length * CharWidth);
        if (x > 20 && x + entry > right)
        {
          coloursY += LineStep;
          x = 20;
        }
        parts.Add(new RectShape(kind, N(x, 1), N(coloursY - 11, 1), N(14, 0), N(14, 0), "4"));
        parts.Add(new TextShape(x + 22, coloursY + 1, label, "sub", "start"));
        x += entry + 28;
      }
      const string error = "Animation error = animation time step − display time step: + shown too soon, − shown too late";
      // The diagrams' line alone, where the card's threshold is left out of the key
      string? thresholdText = threshold is { } shown ? $"error threshold {Ms(shown.Value.TotalMilliseconds)} ms" : null;
      double errorY = coloursY + 26;
      if (thresholdText == null)
        parts.Add(new TextShape(20, errorY, error, "sub", "start"));
      else if (20 + ((error.Length + 2 + thresholdText.Length) * CharWidth) <= right)
        parts.Add(new TextShape(20, errorY, $"{error}; {thresholdText}", "sub", "start"));
      else
      {
        parts.Add(new TextShape(20, errorY, error, "sub", "start"));
        errorY += LineStep;
        parts.Add(new TextShape(20, errorY, char.ToUpperInvariant(thresholdText[0]) + thresholdText[1..], "sub", "start"));
      }
      // The original layout has the error line 78 below the key's first line
      return errorY - (legendY + 78);
    }
  }
}
