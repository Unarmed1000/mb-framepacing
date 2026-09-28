//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A run, or a section of one, as one card (shapes: CardDrawing, written as SVG by SvgCardWriter, drawn by the GUI) in the style of mb-framepacing-explained's example charts (generate_charts.py): the headline
//* tiles, the animation error per frame as signed bars, the display time step as held steps (green as planned, red held too long), the share
//* of late frames in the last 2 s, and the refresh strip. The x axis is seconds since the run's first frame, as on the
//* Timeline. A section short enough to give every frame a pixel draws every frame; longer ones draw each pixel column's range (as the GUI's
//* plottables do), so an hour stays a small file. The error and display time step scales follow the GUI charts, including their marks for
//* the few values far beyond the rest.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MB.FramePacing.Analysis;
using static MB.FramePacing.Charts.SvgMarkup;

namespace MB.FramePacing.Charts
{
  public static class ReportCard
  {
    public const int Width = 1180;
    public const double PlotX0 = 110;
    public const double PlotX1 = Width - 40;

    private const double TileH = 64;
    private const double TileGap = 12;
    private const int TilesPerRow = 4;
    private const double ErrorH = 150;
    private const double StepH = 130;
    private const double FrameTimeH = 130;
    private const double LateH = 80;
    private const double StripH = 28;

    // The layout flows: each shown item takes its height. Space above a panel (its label, and the time axis of the panel before), after the
    // header or the tiles, at the top of a card without either, and below the last item
    private const double PanelGap = 70;
    private const double FirstPanelGap = 52;
    private const double TopPanelGap = 36;
    private const double BottomAfterPanel = 64;
    private const double BottomMargin = 20;

    // Refresh cells narrower than this are not drawn: the strip then asks for a shorter section
    private const double MinCellPixels = 2;
    private const double MinBarHeight = 0.8;

    // A pixel column's middle 90 % (holds: its median) is drawn solid and the rest faint only when 5 % is at least one frame; a column with
    // fewer frames draws all of them solid
    private const int MinFramesForTypical = 20;

    /// <summary>
    /// The SVG of <paramref name="section"/>, with the items <paramref name="options"/> shows (all by default). <paramref name="background"/>
    /// puts a page colour behind the card (for previews).
    /// </summary>
    public static string Render(RunSection section, ReportOptions? options = null, string? background = null) =>
      SvgCardWriter.Write(Build(section, options), background);

    /// <summary>The card of <paramref name="section"/> as shapes, with the items <paramref name="options"/> shows (all by default).</summary>
    public static CardDrawing Build(RunSection section, ReportOptions? options = null)
    {
      options ??= ReportOptions.Default;
      var chart = section.Run;
      var run = chart.Run;
      var pacing = run.Pacing;
      double refreshMs = pacing?.RefreshPeriodMs ?? (chart.CapturePeriodTicks / (double)TimeSpan.TicksPerMillisecond);
      long origin = section.OriginTicks;
      double from = section.FromSeconds;
      double to = section.ToSeconds;
      double Seconds(PresentedFrame f) => (f.FirstSeenTicks - origin) / (double)TimeSpan.TicksPerSecond;
      double XOf(double seconds) => PlotX0 + ((PlotX1 - PlotX0) * (seconds - from) / (to - from));
      var frames = section.Section.Run.Frames;
      double pixelsPerFrame = frames.Count > 0 ? (PlotX1 - PlotX0) / frames.Count : double.MaxValue;
      bool perFrame = pixelsPerFrame >= 1;

      string title = RunHeadline.Title(run) + (section.IsWholeRun ? string.Empty : $", {Ms1(from)}–{Ms1(to)} s");
      var description = new List<string>
      {
        (
          section.IsWholeRun
            ? $"The whole run: {Ms1(to)} s"
            : $"A section of {Ms1(to - from)} s of the run's {Ms1(RunSection.Whole(chart).ToSeconds)} s"
        )
          + $", {frames.Count.ToString("N0", CultureInfo.InvariantCulture)} presented frames on a {Hz(pacing)} display"
          + (pacing != null ? $", measured against a {Ms1(pacing.TargetFrameMs)} ms target." : "."),
        $"Resolution {Ms1(chart.CapturePeriodTicks / (double)TimeSpan.TicksPerMillisecond)} ms (one capture period); error threshold "
          + $"{Ms(chart.ErrorThresholdTicks / (double)TimeSpan.TicksPerMillisecond)} ms.",
        perFrame ? "Every frame is drawn." : "Each pixel column shows its frames' range; with 20 or more, the middle 90 % solid and the rest faint.",
      };
      if (RunHeadline.SequenceLine(run) is { } sequence)
        description.Add(sequence);
      var tiles = RunHeadline.Tiles(section.Section).Where(t => options.IsShown(t.Id)).ToList();
      var layout = Layout.For(options, description.Count, tiles.Count);
      var parts = Header(title, options.IsShown(ReportItem.Description) ? description : Array.Empty<string>(), options.IsShown(ReportItem.Title));
      if (options.IsShown(ReportItem.Display))
        DisplayBox(parts, chart, frames, refreshMs);

      Tiles(parts, tiles, layout.TilesY);
      if (layout.ErrorY is { } errorY)
        ErrorPanel(parts, section, frames, Seconds, XOf, perFrame, errorY);
      if (layout.StepY is { } stepY)
        StepPanel(parts, frames, Seconds, XOf, perFrame, refreshMs, from, to, stepY);
      if (layout.FrameTimeY is { } frameTimeY)
        FrameTimePanel(parts, frames, Seconds, XOf, perFrame, refreshMs, from, to, frameTimeY);
      if (layout.LateY is { } lateY)
        LatePanel(parts, section, Seconds, XOf, perFrame, lateY);
      if (layout.StripY is { } stripY)
        StripPanel(parts, frames, Seconds, XOf, refreshMs, from, to, chart.CapturePeriodTicks, stripY);

      return new CardDrawing(title, Width, layout.Height, parts);
    }

    /// <summary>Where the shown items go, top to bottom, and the card's height.</summary>
    private sealed record Layout(double TilesY, double? ErrorY, double? StepY, double? FrameTimeY, double? LateY, double? StripY, double Height)
    {
      public static Layout For(ReportOptions options, int descriptionLines, int tiles)
      {
        bool title = options.IsShown(ReportItem.Title);
        bool description = options.IsShown(ReportItem.Description) && descriptionLines > 0;
        bool display = options.IsShown(ReportItem.Display);
        double cursor = BottomMargin;
        string previous = "start";
        if (title || description || display)
        {
          // The last text line's baseline (description lines follow the title, or take its place), and the display box
          double text =
            description ? (title ? 54 : 30) + ((descriptionLines - 1) * 19)
            : title ? 30
            : 0;
          cursor = Math.Max(text > 0 ? text + 31 : 0, display ? 14 + 68 + 20 : 0);
          previous = "header";
        }
        double tilesY = cursor;
        if (tiles > 0)
        {
          int rows = (tiles + TilesPerRow - 1) / TilesPerRow;
          cursor = tilesY + (rows * TileH) + ((rows - 1) * TileGap);
          previous = "tiles";
        }
        double? Panel(string id, double height)
        {
          if (!options.IsShown(id))
            return null;
          double top =
            cursor
            + (
              previous == "panel" ? PanelGap
              : previous == "start" ? TopPanelGap
              : FirstPanelGap
            );
          cursor = top + height;
          previous = "panel";
          return top;
        }
        var errorY = Panel(ReportItem.AnimationError, ErrorH);
        var stepY = Panel(ReportItem.DisplayTimeStep, StepH);
        var frameTimeY = Panel(ReportItem.FrameTime, FrameTimeH);
        var lateY = Panel(ReportItem.LateShare, LateH);
        var stripY = Panel(ReportItem.RefreshStrip, StripH);
        double height = previous == "panel" ? cursor + BottomAfterPanel : cursor + BottomMargin;
        return new Layout(tilesY, errorY, stepY, frameTimeY, lateY, stripY, height);
      }
    }

    /// <summary>
    /// The display in the top right corner: its refresh rate, whether it is the fixed refresh a capture card captures at (vsync) or calculated
    /// from a camera's frames, the time per refresh, and what the frames targeted in whole refreshes.
    /// </summary>
    private static void DisplayBox(List<CardShape> parts, ChartRun chart, IReadOnlyList<PresentedFrame> frames, double refreshMs)
    {
      const double BoxW = 300;
      const double BoxH = 68;
      const double X = Width - 20 - BoxW;
      const double Y = 14;
      var pacing = chart.Run.Pacing;
      parts.Add(new RectShape("tile", N(X, 1), N(Y, 0), N(BoxW, 0), N(BoxH, 0), "10"));
      parts.Add(new TextShape(X + 14, Y + 19, "DISPLAY", "label", "start"));
      string rate = Hz(pacing);
      bool mismatch = pacing?.MatchesExpectedRefresh == false;
      parts.Add(new TextShape(X + 14, Y + 42, rate, mismatch ? "tile-value warn" : "tile-value", "start"));
      string kind =
        pacing == null ? "unknown"
        : pacing.RefreshCalculated ? "calculated from the camera"
        : "fixed refresh (vsync)";
      if (pacing?.ExpectedRefreshHz is { } expected && mismatch)
        kind += $", expected {expected.ToString("0.##", CultureInfo.InvariantCulture)} Hz";
      parts.Add(new TextShape(X + 14 + (rate.Length * 11.5) + 8, Y + 42, kind, "vsync-n", "start"));

      // What the frames targeted, in whole refreshes: the target frame time the marker carries (a pacer that adapts its rate, like Swappy,
      // targets several), else the target each frame was measured against. Not the schedule's step, which is longer after a late frame.
      var refreshes = frames
        .Where(f => f.DisplayDeltaTicks.HasValue && (f.MarkerTargetFrameTicks > 0 || f.TargetTicks.HasValue))
        .Select(f =>
          (int)
            Math.Round(
              (f.MarkerTargetFrameTicks > 0 ? f.MarkerTargetFrameTicks : f.TargetTicks!.Value) / (double)TimeSpan.TicksPerMillisecond / refreshMs
            )
        )
        .Distinct()
        .Order()
        .ToArray();
      string target =
        refreshes.Length == 0 ? string.Empty
        : refreshes.Length == 1
          ? $", target {Refreshes(refreshes[0])} ({(1000 / (refreshes[0] * refreshMs)).ToString("0.#", CultureInfo.InvariantCulture)} fps)"
        : $", target {refreshes[0]}\u2013{Refreshes(refreshes[^1])}";
      parts.Add(new TextShape(X + 14, Y + 60, $"{Ms1(refreshMs)} ms per refresh{target}", "vsync-n", "start"));
    }

    private static string Refreshes(int count) => count == 1 ? "1 refresh" : $"{count} refreshes";

    private static void Tiles(List<CardShape> parts, IReadOnlyList<HeadlineTile> tiles, double tilesY)
    {
      double tileW = (Width - 40 - ((TilesPerRow - 1) * TileGap)) / TilesPerRow;
      for (int i = 0; i < tiles.Count; ++i)
      {
        var tile = tiles[i];
        double x = 20 + ((i % TilesPerRow) * (tileW + TileGap));
        double y = tilesY + ((i / TilesPerRow) * (TileH + TileGap));
        parts.Add(new RectShape("tile", N(x, 1), N(y, 0), N(tileW, 1), N(TileH, 0), "10"));
        parts.Add(new TextShape(x + 14, y + 20, tile.Caption.ToUpperInvariant(), "label", "start"));
        parts.Add(new TextShape(x + 14, y + 44, tile.Value, tile.Warning ? "tile-value warn" : "tile-value", "start"));
        if (tile.Detail.Length > 0)
          parts.Add(new TextShape(x + 14 + (tile.Value.Length * 11.5) + 8, y + 44, tile.Detail, "vsync-n", "start"));
      }
    }

    private static void ErrorPanel(
      List<CardShape> parts,
      RunSection section,
      IReadOnlyList<PresentedFrame> frames,
      Func<PresentedFrame, double> seconds,
      Func<double, double> xOf,
      bool perFrame,
      double errorY
    )
    {
      var withError = frames.Where(f => f.AnimationErrorTicks.HasValue).ToArray();
      var errorsMs = withError.Select(f => f.AnimationErrorTicks!.Value / (double)TimeSpan.TicksPerMillisecond).ToArray();
      double limit = AnimationErrorBarsPlottable.Limit(errorsMs);
      double zeroY = errorY + (ErrorH / 2);
      double YOf(double value) => zeroY - (Math.Clamp(value, -limit, limit) / limit * ErrorH / 2);

      parts.Add(new TextShape(20, errorY - 16, "ANIMATION ERROR PER FRAME", "label", "start"));
      parts.Add(new TextShape(PlotX1, errorY - 16, "+ shown too soon, − shown too late; the band is within the error threshold", "vsync-n", "end"));
      double threshold = section.Run.ErrorThresholdTicks / (double)TimeSpan.TicksPerMillisecond;
      parts.Add(new RectShape("band", N(PlotX0, 1), N(YOf(threshold), 1), N(PlotX1 - PlotX0, 1), N(YOf(-threshold) - YOf(threshold), 1)));
      foreach (var (position, _) in AnimationErrorBarsPlottable.Ticks(limit).Where(t => t.Position != 0))
      {
        double y = YOf(position);
        parts.Add(GridLine(y));
        parts.Add(new TextShape(PlotX0 - 10, y + 4, $"{Ms(position, sign: true)} ms", "vsync-n", "end"));
      }

      var clipped = new List<(double X, double Value, bool Top)>();
      if (perFrame)
      {
        double barW = Math.Max(1.0, ((PlotX1 - PlotX0) / Math.Max(1, frames.Count)) - 0.6);
        for (int i = 0; i < withError.Length; ++i)
        {
          double value = errorsMs[i];
          if (Math.Abs(value) < 1e-9)
            continue;
          double x = xOf(seconds(withError[i]));
          var (y0, y1) = (Math.Min(zeroY, YOf(value)), Math.Max(zeroY, YOf(value)));
          parts.Add(new RectShape("bar", N(x, 2), N(y0, 1), N(barW, 2), N(Math.Max(MinBarHeight, y1 - y0), 1)));
          if (Math.Abs(value) > limit)
            clipped.Add((x + (barW / 2), value, value > 0));
        }
      }
      else
      {
        // Per pixel column: its whole range faint, from the most negative to the most positive error, and the middle 90 % of its frames
        // solid, so a stretch with a late frame every so often still reads as mostly on time
        var range = new StringBuilder();
        var typical = new StringBuilder();
        void Bar(StringBuilder path, int column, double low, double high)
        {
          if (high <= 1e-9 && low >= -1e-9)
            return;
          double top = YOf(Math.Max(0, high));
          double bottom = YOf(Math.Min(0, low));
          if (bottom - top < MinBarHeight)
            (top, bottom) = high > 1e-9 ? (zeroY - MinBarHeight, zeroY) : (zeroY, zeroY + MinBarHeight);
          path.Append($"M{Fixed(column, 0)} {Fixed(top, 1)}h1V{Fixed(bottom, 1)}h-1Z");
        }
        foreach (var column in ColumnValues(withError.Select(f => xOf(seconds(f))).ToArray(), errorsMs))
        {
          if (column.Count >= MinFramesForTypical)
          {
            Bar(range, column.Column, column.Min, column.Max);
            Bar(typical, column.Column, column.P05, column.P95);
          }
          else
          {
            Bar(typical, column.Column, column.Min, column.Max);
          }
          if (column.Max > limit)
            clipped.Add((column.Column + 0.5, column.Max, true));
          if (column.Min < -limit)
            clipped.Add((column.Column + 0.5, column.Min, false));
        }
        AddPath(parts, "bar-range", range);
        AddPath(parts, "bar", typical);
      }
      parts.Add(new LineShape("zero-line", N(PlotX0, 0), N(zeroY, 0), N(PlotX1, 0), N(zeroY, 0)));
      parts.Add(new TextShape(PlotX0 - 10, zeroY + 4, "0", "vsync-n", "end"));
      ClipMarks(parts, clipped, errorY, errorY + ErrorH, v => $"{Ms(v, sign: true)} ms");
      TimeTicks(parts, section.FromSeconds, section.ToSeconds, xOf, errorY + ErrorH);
    }

    private static void StepPanel(
      List<CardShape> parts,
      IReadOnlyList<PresentedFrame> frames,
      Func<PresentedFrame, double> seconds,
      Func<double, double> xOf,
      bool perFrame,
      double refreshMs,
      double from,
      double to,
      double stepY
    )
    {
      // Each frame's hold: from its first sighting to the next frame of its segment, at the next frame's display time step
      var holds = new List<(double X0, double X1, double Level, bool Late)>();
      for (int i = 0; i + 1 < frames.Count; ++i)
      {
        var next = frames[i + 1];
        if (next.Segment != frames[i].Segment || next.DisplayDeltaTicks is not { } display)
          continue;
        holds.Add(
          (
            xOf(seconds(frames[i])),
            Math.Min(PlotX1, xOf(seconds(next))),
            display / (double)TimeSpan.TicksPerMillisecond,
            (next.Flags & PresentedFrameFlags.Late) != 0
          )
        );
      }
      double top = DisplayTimeStepsPlottable.Top(holds.Select(h => h.Level).ToArray(), refreshMs);
      double YOf(double ms) => stepY + StepH - (Math.Min(ms, top) / top * StepH);

      parts.Add(new TextShape(20, stepY - 16, "DISPLAY TIME STEP: HOW LONG EACH FRAME STAYED ON SCREEN", "label", "start"));
      parts.Add(new TextShape(PlotX1, stepY - 16, "green as planned, red held too long (the next frame was late)", "vsync-n", "end"));
      foreach (var (position, _) in DisplayTimeStepsPlottable.Ticks(refreshMs, top).Where(t => t.Position > 0))
      {
        double y = YOf(position);
        parts.Add(GridLine(y));
        parts.Add(new TextShape(PlotX0 - 10, y + 4, $"{Ms(Math.Round(position, 1))} ms", "vsync-n", "end"));
      }
      parts.Add(GridLine(YOf(0)));
      parts.Add(new TextShape(PlotX0 - 10, YOf(0) + 4, "0", "vsync-n", "end"));

      var clipped = new List<(double X, double Value, bool Top)>();
      if (perFrame)
      {
        var risers = new StringBuilder();
        var onTime = new StringBuilder();
        var late = new StringBuilder();
        for (int i = 0; i < holds.Count; ++i)
        {
          var hold = holds[i];
          double y = YOf(hold.Level);
          if (i > 0 && Math.Abs(holds[i - 1].X1 - hold.X0) < 1e-6 && Math.Abs(holds[i - 1].Level - hold.Level) > 1e-9)
            risers.Append($"M{Fixed(hold.X0, 1)} {Fixed(YOf(holds[i - 1].Level), 1)}V{Fixed(y, 1)}");
          (hold.Late ? late : onTime).Append($"M{Fixed(hold.X0, 1)} {Fixed(y, 1)}H{Fixed(hold.X1, 1)}");
          if (hold.Level > top)
            clipped.Add(((hold.X0 + hold.X1) / 2, hold.Level, true));
        }
        AddPath(parts, "riser", risers);
        AddPath(parts, "held", onTime);
        AddPath(parts, "held-late", late);
      }
      else
      {
        // Per pixel column: the range of its holds faint (red if any was held too long), and its median hold as the solid line
        var onTimeRange = new StringBuilder();
        var lateRange = new StringBuilder();
        var median = new StringBuilder();
        var medianLate = new StringBuilder();
        var solid = new StringBuilder();
        var solidLate = new StringBuilder();
        var (order, columns) = PixelColumns.Of(holds.Select(h => h.X0).ToArray());
        var buffer = new double[order.Length];
        foreach (var (key, start, count) in columns)
        {
          var column = order.AsSpan(start, count);
          var levels = buffer.AsSpan(0, count);
          double end = key + 1;
          bool anyLate = false;
          for (int k = 0; k < count; ++k)
          {
            var hold = holds[column[k]];
            levels[k] = hold.Level;
            end = Math.Max(end, hold.X1);
            anyLate |= hold.Late;
          }
          levels.Sort();
          string box = $"M{Fixed(key, 0)} {Fixed(YOf(levels[^1]) - 1.25, 1)}H{Fixed(end, 1)}V{Fixed(YOf(levels[0]) + 1.25, 1)}H{Fixed(key, 0)}Z";
          if (count < MinFramesForTypical)
          {
            // Few holds: all of them solid
            (anyLate ? solidLate : solid).Append(box);
            if (levels[^1] > top)
              clipped.Add((key + 0.5, levels[^1], true));
            continue;
          }
          (anyLate ? lateRange : onTimeRange).Append(box);
          // A hold that happened (the lower middle one), not an interpolation between two levels
          double middle = levels[(count - 1) / 2];
          bool middleLate = false;
          foreach (int index in column)
            middleLate |= Math.Abs(holds[index].Level - middle) < 1e-9 && holds[index].Late;
          (middleLate ? medianLate : median).Append($"M{Fixed(key, 0)} {Fixed(YOf(middle), 1)}H{Fixed(end, 1)}");
          if (levels[^1] > top)
            clipped.Add((key + 0.5, levels[^1], true));
        }
        AddPath(parts, "held-range", onTimeRange);
        AddPath(parts, "held-range-late", lateRange);
        AddPath(parts, "held", median);
        AddPath(parts, "held-late", medianLate);
        AddPath(parts, "held-fill", solid);
        AddPath(parts, "held-fill-late", solidLate);
      }
      ClipMarks(parts, clipped, stepY, stepY + StepH, v => $"{Ms(v)} ms");
      TimeTicks(parts, from, to, xOf, stepY + StepH);
    }

    /// <summary>
    /// The application side, from the markers, on the display time step's whole-refresh grid: each frame's frametime (from its CPU start to the
    /// next frame's) as a step, and its CPU busy (from its CPU start until it was presented) as a faint bar, held from the frame's display time
    /// to the next frame's.
    /// </summary>
    private static void FrameTimePanel(
      List<CardShape> parts,
      IReadOnlyList<PresentedFrame> frames,
      Func<PresentedFrame, double> seconds,
      Func<double, double> xOf,
      bool perFrame,
      double refreshMs,
      double from,
      double to,
      double frameTimeY
    )
    {
      parts.Add(new TextShape(20, frameTimeY - 16, "FRAMETIME AND CPU BUSY: THE APPLICATION SIDE, FROM THE MARKERS", "label", "start"));
      var spans = new List<(double X0, double X1, double FrameTime, double CpuBusy)>();
      for (int i = 0; i < frames.Count; ++i)
      {
        var frame = frames[i];
        double frameTime = frame.FrameTimeTicks is { } ticks ? ticks / (double)TimeSpan.TicksPerMillisecond : 0;
        double cpuBusy = frame.CpuBusyTicks / (double)TimeSpan.TicksPerMillisecond;
        if (frameTime <= 0 && cpuBusy <= 0)
          continue;
        double x0 = xOf(seconds(frame));
        double x1 =
          i + 1 < frames.Count && frames[i + 1].Segment == frame.Segment
            ? xOf(seconds(frames[i + 1]))
            : xOf(seconds(frame) + (frame.OnScreenTicks / (double)TimeSpan.TicksPerSecond));
        spans.Add((x0, Math.Min(PlotX1, x1), frameTime, cpuBusy));
      }
      if (spans.Count == 0)
      {
        parts.Add(new TextShape(PlotX0, frameTimeY + (FrameTimeH / 2), "the markers carry no CPU start time or CPU busy", "vsync-n", "start"));
        return;
      }
      parts.Add(
        new TextShape(PlotX1, frameTimeY - 16, "blue: frametime (CPU start to the next); faint: CPU busy (until presented)", "vsync-n", "end")
      );
      double top = DisplayTimeStepsPlottable.Top(spans.SelectMany(s => new[] { s.FrameTime, s.CpuBusy }).Where(v => v > 0).ToArray(), refreshMs);
      double YOf(double ms) => frameTimeY + FrameTimeH - (Math.Min(ms, top) / top * FrameTimeH);
      foreach (var (position, _) in DisplayTimeStepsPlottable.Ticks(refreshMs, top).Where(t => t.Position > 0))
      {
        double y = YOf(position);
        parts.Add(GridLine(y));
        parts.Add(new TextShape(PlotX0 - 10, y + 4, $"{Ms(Math.Round(position, 1))} ms", "vsync-n", "end"));
      }
      parts.Add(GridLine(YOf(0)));
      parts.Add(new TextShape(PlotX0 - 10, YOf(0) + 4, "0", "vsync-n", "end"));

      var clipped = new List<(double X, double Value, bool Top)>();
      var busy = new StringBuilder();
      double bottom = YOf(0);
      if (perFrame)
      {
        var steps = new StringBuilder();
        foreach (var span in spans)
        {
          if (span.CpuBusy > 0)
            busy.Append($"M{Fixed(span.X0, 1)} {Fixed(YOf(span.CpuBusy), 1)}H{Fixed(span.X1, 1)}V{Fixed(bottom, 1)}H{Fixed(span.X0, 1)}Z");
          if (span.FrameTime > 0)
            steps.Append($"M{Fixed(span.X0, 1)} {Fixed(YOf(span.FrameTime), 1)}H{Fixed(span.X1, 1)}");
          double highest = Math.Max(span.FrameTime, span.CpuBusy);
          if (highest > top)
            clipped.Add(((span.X0 + span.X1) / 2, highest, true));
        }
        AddPath(parts, "cpu-busy", busy);
        AddPath(parts, "frametime", steps);
      }
      else
      {
        // Per pixel column: the median CPU busy as a faint bar; the frametimes' range faint and their median solid (all solid when few)
        var range = new StringBuilder();
        var median = new StringBuilder();
        var solid = new StringBuilder();
        var (order, columns) = PixelColumns.Of(spans.Select(s => s.X0).ToArray());
        var busyBuffer = new double[order.Length];
        var levelBuffer = new double[order.Length];
        foreach (var (key, start, count) in columns)
        {
          double end = key + 1;
          int busyCount = 0;
          int levelCount = 0;
          foreach (int index in order.AsSpan(start, count))
          {
            var span = spans[index];
            end = Math.Max(end, span.X1);
            if (span.CpuBusy > 0)
              busyBuffer[busyCount++] = span.CpuBusy;
            if (span.FrameTime > 0)
              levelBuffer[levelCount++] = span.FrameTime;
          }
          var busyLevels = busyBuffer.AsSpan(0, busyCount);
          var levels = levelBuffer.AsSpan(0, levelCount);
          busyLevels.Sort();
          levels.Sort();
          if (busyCount > 0)
          {
            double middleBusy = busyLevels[(busyCount - 1) / 2];
            busy.Append($"M{Fixed(key, 0)} {Fixed(YOf(middleBusy), 1)}H{Fixed(end, 1)}V{Fixed(bottom, 1)}H{Fixed(key, 0)}Z");
          }
          double highest = Math.Max(levelCount > 0 ? levels[^1] : 0, busyCount > 0 ? busyLevels[^1] : 0);
          if (highest > top)
            clipped.Add((key + 0.5, highest, true));
          if (levelCount == 0)
            continue;
          string box = $"M{Fixed(key, 0)} {Fixed(YOf(levels[^1]) - 1.25, 1)}H{Fixed(end, 1)}V{Fixed(YOf(levels[0]) + 1.25, 1)}H{Fixed(key, 0)}Z";
          if (levelCount < MinFramesForTypical)
          {
            solid.Append(box);
            continue;
          }
          range.Append(box);
          median.Append($"M{Fixed(key, 0)} {Fixed(YOf(levels[(levelCount - 1) / 2]), 1)}H{Fixed(end, 1)}");
        }
        AddPath(parts, "cpu-busy", busy);
        AddPath(parts, "frametime-range", range);
        AddPath(parts, "frametime", median);
        AddPath(parts, "frametime-fill", solid);
      }
      ClipMarks(parts, clipped, frameTimeY, frameTimeY + FrameTimeH, v => $"{Ms(v)} ms");
      TimeTicks(parts, from, to, xOf, frameTimeY + FrameTimeH);
    }

    private static void LatePanel(
      List<CardShape> parts,
      RunSection section,
      Func<PresentedFrame, double> seconds,
      Func<double, double> xOf,
      bool perFrame,
      double lateY
    )
    {
      var whole = section.Run.Run.Frames;
      var pacing = section.Run.Run.Pacing;
      parts.Add(new TextShape(20, lateY - 16, $"SHARE OF LATE FRAMES IN THE LAST {LateShare.WindowSeconds:0} S", "label", "start"));
      if (pacing == null || whole.Count == 0)
      {
        parts.Add(new TextShape(PlotX0, lateY + (LateH / 2), "no pacing information", "vsync-n", "start"));
        return;
      }
      // The window reaches back before the section's start, so the share is the run's own
      var shares = LateShare.Rolling(whole, LateShare.WindowTicks);
      var (adapted, usualTicks) = Adapted(whole);
      var points = new List<(double X, double Share, bool Adapted)>();
      for (int i = 0; i < whole.Count; ++i)
      {
        double t = seconds(whole[i]);
        if (t >= section.FromSeconds && t <= section.ToSeconds)
          points.Add((xOf(t), shares[i] * 100, adapted[i]));
      }
      string note = $"whole run {Percent(pacing.LateShare)}";
      if (points.Any(p => p.Adapted))
        note =
          $"amber: the frames' marker target frame time is above the run's usual {Ms1(usualTicks / (double)TimeSpan.TicksPerMillisecond)} ms; "
          + $"whole run {Percent(pacing.LateShare)}";
      parts.Add(new TextShape(PlotX1, lateY - 16, note, "vsync-n", "end"));
      double max = points.Count > 0 ? points.Max(p => p.Share) : 0;
      double topShare = NiceCeiling(Math.Max(5, max * 1.25));
      double YOf(double share) => lateY + LateH - (share / topShare * LateH);
      foreach (double tick in new[] { 0, topShare / 2, topShare })
      {
        parts.Add(GridLine(YOf(tick)));
        parts.Add(
          new TextShape(PlotX0 - 10, YOf(tick) + 4, tick == 0 ? "0" : $"{tick.ToString("0.##", CultureInfo.InvariantCulture)} %", "vsync-n", "end")
        );
      }
      // Green where no frame in the window was late, red where some were, amber where the markers' target frame time is above the run's
      // usual one; each stretch starts where the previous one ended, so the line is whole
      var paths = new Dictionary<string, StringBuilder>
      {
        ["late-line-none"] = new StringBuilder(),
        ["late-line"] = new StringBuilder(),
        ["late-line-adapted"] = new StringBuilder(),
      };
      (double X, double Y)? last = null;
      string? lastStyle = null;
      void Point(double x, double y, string style)
      {
        var path = paths[style];
        if (lastStyle != style)
        {
          path.Append(last is { } previous ? $"M{Fixed(previous.X, 1)} {Fixed(previous.Y, 1)}L" : "M");
          lastStyle = style;
        }
        else
        {
          path.Append('L');
        }
        path.Append($"{Fixed(x, 1)} {Fixed(y, 1)}");
        last = (x, y);
      }
      static string Style(double share, bool isAdapted) =>
        share <= 0 ? "late-line-none"
        : isAdapted ? "late-line-adapted"
        : "late-line";
      if (perFrame)
      {
        foreach (var (x, share, isAdapted) in points)
          Point(x, YOf(share), Style(share, isAdapted));
      }
      else
      {
        var (order, columns) = PixelColumns.Of(points.Select(p => p.X).ToArray());
        foreach (var (key, start, count) in columns)
        {
          int adaptedCount = 0;
          double low = double.MaxValue;
          double high = double.MinValue;
          foreach (int index in order.AsSpan(start, count))
          {
            var point = points[index];
            adaptedCount += point.Adapted ? 1 : 0;
            low = Math.Min(low, point.Share);
            high = Math.Max(high, point.Share);
          }
          bool isAdapted = adaptedCount * 2 > count;
          Point(key + 0.5, YOf(low), Style(high, isAdapted));
          if (high > low)
            Point(key + 0.5, YOf(high), Style(high, isAdapted));
        }
      }
      foreach (var (style, path) in paths)
        AddPath(parts, style, path);
      TimeTicks(parts, section.FromSeconds, section.ToSeconds, xOf, lateY + LateH);
    }

    private static void StripPanel(
      List<CardShape> parts,
      IReadOnlyList<PresentedFrame> frames,
      Func<PresentedFrame, double> seconds,
      Func<double, double> xOf,
      double refreshMs,
      double from,
      double to,
      long capturePeriodTicks,
      double stripY
    )
    {
      double cellW = (PlotX1 - PlotX0) * refreshMs / 1000 / (to - from);
      parts.Add(new TextShape(20, stripY - 16, "REFRESH STRIP", "label", "start"));
      if (cellW < MinCellPixels)
      {
        double longest = (PlotX1 - PlotX0) / MinCellPixels * refreshMs / 1000;
        parts.Add(new TextShape(PlotX1, stripY - 16, "one cell per refresh, a new shade with every new frame", "vsync-n", "end"));
        parts.Add(
          new TextShape(PlotX0, stripY + (StripH / 2) + 4, $"render a section of at most {Ms1(longest)} s to see the refreshes", "vsync-n", "start")
        );
        return;
      }
      parts.Add(new TextShape(PlotX1, stripY - 16, "one cell per refresh, a new shade with every new frame, late frames red", "vsync-n", "end"));
      long refreshTicks = (long)Math.Round(refreshMs * TimeSpan.TicksPerMillisecond);
      for (int i = 0; i < frames.Count; ++i)
      {
        var frame = frames[i];
        long onScreen = frame.OnScreenTicks > 0 ? frame.OnScreenTicks : capturePeriodTicks;
        int cells = refreshTicks > 0 ? (int)Math.Max(1, (onScreen + (refreshTicks / 2)) / refreshTicks) : 1;
        string cls =
          (frame.Flags & PresentedFrameFlags.Late) != 0 ? "strip-late"
          : i % 2 == 0 ? "strip-a"
          : "strip-b";
        double x0 = xOf(seconds(frame));
        for (int c = 0; c < cells; ++c)
        {
          double x = x0 + (c * cellW);
          if (x >= PlotX1)
            break;
          parts.Add(new RectShape(cls, N(x + 0.5, 1), N(stripY, 0), N(Math.Max(0.5, Math.Min(cellW - 1, PlotX1 - x - 0.5)), 1), N(StripH, 0), "2"));
        }
      }
      TimeTicks(parts, from, to, xOf, stripY + StripH);
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Per frame: the target frame time in its marker is longer than the run's usual one (its most common), and that usual target. Only what
    /// the markers say; a run that always carries the same target has no such frames.
    /// </summary>
    private static (bool[] Adapted, uint UsualTicks) Adapted(IReadOnlyList<PresentedFrame> frames)
    {
      var targets = frames.Where(f => f.MarkerTargetFrameTicks > 0).Select(f => f.MarkerTargetFrameTicks).ToArray();
      if (targets.Length == 0)
        return (new bool[frames.Count], 0);
      uint usual = targets.GroupBy(t => t).MaxBy(g => g.Count())!.Key;
      // Half a percent of slack: targets are whole ticks of the pacer's clock
      return (frames.Select(f => f.MarkerTargetFrameTicks > usual * 1.005).ToArray(), usual);
    }

    /// <summary>The values of each pixel column the points fall in: the lowest and highest, and the 5th and 95th percentile.</summary>
    private static List<(int Column, int Count, double Min, double P05, double P95, double Max)> ColumnValues(double[] xs, double[] values)
    {
      var (order, columns) = PixelColumns.Of(xs);
      var buffer = new double[order.Length];
      var result = new List<(int Column, int Count, double Min, double P05, double P95, double Max)>(columns.Count);
      foreach (var (column, start, count) in columns)
      {
        var sorted = buffer.AsSpan(0, count);
        for (int k = 0; k < count; ++k)
          sorted[k] = values[order[start + k]];
        sorted.Sort();
        result.Add((column, count, sorted[0], Statistics.Percentile(sorted, 0.05), Statistics.Percentile(sorted, 0.95), sorted[^1]));
      }
      return result;
    }

    /// <summary>Values beyond the panel's scale: a triangle at its edge, and the value beside it, largest first and where it does not overlap.</summary>
    private static void ClipMarks(
      List<CardShape> parts,
      List<(double X, double Value, bool Top)> clipped,
      double top,
      double bottom,
      Func<double, string> format
    )
    {
      var placed = new List<(double Left, double Right, bool Top)>();
      foreach (var (x, value, atTop) in clipped.OrderByDescending(c => Math.Abs(c.Value)))
      {
        double edge = atTop ? top : bottom;
        double inside = atTop ? edge + 6 : edge - 6;
        parts.Add(new PathShape("clip-mark", $"M{Fixed(x, 1)} {Fixed(edge, 1)}L{Fixed(x - 4, 1)} {Fixed(inside, 1)}H{Fixed(x + 4, 1)}Z"));
        string text = format(value);
        double left = x + 6;
        double right = left + (text.Length * 6.2);
        if (right > PlotX1)
          (left, right) = (x - 6 - (text.Length * 6.2), x - 6);
        if (placed.Any(p => p.Top == atTop && left < p.Right + 4 && right > p.Left - 4))
          continue;
        placed.Add((left, right, atTop));
        parts.Add(new TextShape(left, atTop ? edge + 17 : edge - 8, text, "clip-text", "start"));
      }
    }

    private static readonly double[] g_timeSteps = { 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600 };

    /// <summary>The time axis under a panel: a label every "nice" step, at most a dozen.</summary>
    private static void TimeTicks(List<CardShape> parts, double from, double to, Func<double, double> xOf, double bottom)
    {
      double length = to - from;
      double step = g_timeSteps.FirstOrDefault(s => length / s <= 12, 3600);
      bool minutes = step >= 60;
      for (double t = Math.Ceiling(from / step) * step; t <= to + 1e-9; t += step)
      {
        string label = minutes
          ? $"{(t / 60).ToString("0.##", CultureInfo.InvariantCulture)} min"
          : $"{t.ToString("0.##", CultureInfo.InvariantCulture)} s";
        parts.Add(new TextShape(xOf(t), bottom + 18, label, "vsync-n"));
      }
    }

    private static LineShape GridLine(double y) => new LineShape("grid", N(PlotX0, 0), N(y, 1), N(PlotX1, 0), N(y, 1));

    private static void AddPath(List<CardShape> parts, string cls, StringBuilder d)
    {
      if (d.Length > 0)
        parts.Add(new PathShape(cls, d.ToString()));
    }

    private static double NiceCeiling(double value)
    {
      foreach (double nice in new[] { 5.0, 10, 20, 25, 50, 100 })
      {
        if (value <= nice)
          return nice;
      }
      return 100;
    }

    private static string Ms1(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Percent(double share) => share.ToString("P1", CultureInfo.InvariantCulture);

    private static string Hz(RunPacing? pacing) => pacing != null ? $"{pacing.RefreshHz.ToString("0.##", CultureInfo.InvariantCulture)} Hz" : "?? Hz";
  }
}
