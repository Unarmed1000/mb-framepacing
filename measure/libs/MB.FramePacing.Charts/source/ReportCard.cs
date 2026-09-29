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
using System.Threading.Tasks;
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

    /// <summary>
    /// The card of <paramref name="section"/> as shapes, with the items <paramref name="options"/> shows (all by default). With
    /// <paramref name="wholeRunScales"/> the panels keep the whole run's scales (the GUI, so the axes stay while zooming and scrolling);
    /// otherwise each scales to the section.
    /// </summary>
    public static CardDrawing Build(RunSection section, ReportOptions? options = null, bool wholeRunScales = false, double width = Width)
    {
      double plotX1 = width - (Width - PlotX1);
      options ??= ReportOptions.Default;
      var chart = section.Run;
      var run = chart.Run;
      var pacing = run.Pacing;
      double refreshMs = pacing?.RefreshPeriodMs ?? (chart.CapturePeriodTicks / (double)TimeSpan.TicksPerMillisecond);
      double from = section.FromSeconds;
      double to = section.ToSeconds;
      var data = section.Data;
      double XOf(double seconds) => PlotX0 + ((plotX1 - PlotX0) * (seconds - from) / (to - from));
      double XOfFrame(int index) => XOf(data.Seconds(index));
      int frameCount = section.FrameCount;
      double pixelsPerFrame = frameCount > 0 ? (plotX1 - PlotX0) / frameCount : double.MaxValue;
      bool perFrame = pixelsPerFrame >= 1;

      string title = RunHeadline.Title(run) + (section.IsWholeRun ? string.Empty : $", {Ms1(from)}–{Ms1(to)} s");
      var description = new List<string>
      {
        (
          section.IsWholeRun
            ? $"The whole run: {Ms1(to)} s"
            : $"A section of {Ms1(to - from)} s of the run's {Ms1(RunSection.Whole(chart).ToSeconds)} s"
        )
          + $", {frameCount.ToString("N0", CultureInfo.InvariantCulture)} presented frames on a {Hz(pacing)} display"
          + (pacing != null ? $", measured against a {Ms1(pacing.TargetFrameMs)} ms target." : "."),
        $"Resolution {Ms1(chart.CapturePeriodTicks / (double)TimeSpan.TicksPerMillisecond)} ms (one capture period); error threshold "
          + $"{Ms(chart.ErrorThresholdTicks / (double)TimeSpan.TicksPerMillisecond)} ms.",
        perFrame ? "Every frame is drawn." : "Each pixel column shows its frames' range; with 20 or more, the middle 90 % solid and the rest faint.",
      };
      if (RunHeadline.SequenceLine(run) is { } sequence)
        description.Add(sequence);
      // The section's own numbers only when a tile shows them
      var tiles = ReportItem.TileIds.Any(options.IsShown)
        ? RunHeadline.Tiles(section.Section).Where(t => options.IsShown(t.Id)).ToList()
        : new List<HeadlineTile>();
      var layout = Layout.For(options, description.Count, tiles.Count);
      var parts = Header(title, options.IsShown(ReportItem.Description) ? description : Array.Empty<string>(), options.IsShown(ReportItem.Title));
      if (options.IsShown(ReportItem.Display))
        DisplayBox(parts, chart, section, refreshMs, width);

      Tiles(parts, tiles, layout.TilesY, width);
      // The panels only read the section: each draws into shapes of its own, on the thread pool, joined in the card's order
      var panels = new List<Action<List<CardShape>, List<CardPlot>>>();
      var view = new PanelView(section, XOf, XOfFrame, perFrame, wholeRunScales, plotX1);
      if (layout.ErrorY is { } errorY)
        panels.Add((shapes, plots) => ErrorPanel(shapes, plots, view, errorY));
      if (layout.StepY is { } stepY)
        panels.Add((shapes, plots) => StepPanel(shapes, plots, view, refreshMs, stepY));
      if (layout.FrameTimeY is { } frameTimeY)
        panels.Add((shapes, plots) => FrameTimePanel(shapes, plots, view, refreshMs, frameTimeY));
      if (layout.LateY is { } lateY)
        panels.Add((shapes, plots) => LatePanel(shapes, plots, view, lateY));
      if (layout.StripY is { } stripY)
        panels.Add((shapes, plots) => StripPanel(shapes, plots, view, refreshMs, chart, stripY));
      var drawn = panels.Select(_ => (Shapes: new List<CardShape>(), Plots: new List<CardPlot>())).ToArray();
      Parallel.For(0, panels.Count, i => panels[i](drawn[i].Shapes, drawn[i].Plots));
      foreach (var (shapes, _) in drawn)
        parts.AddRange(shapes);

      return new CardDrawing(title, width, layout.Height, parts, drawn.SelectMany(d => d.Plots).ToList());
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
    private static void DisplayBox(List<CardShape> parts, ChartRun chart, RunSection section, double refreshMs, double width)
    {
      var frames = Enumerable.Range(section.Start, section.FrameCount).Select(i => section.Data.Frames[i]);
      const double BoxW = 300;
      const double BoxH = 68;
      double X = width - 20 - BoxW;
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

    private static void Tiles(List<CardShape> parts, IReadOnlyList<HeadlineTile> tiles, double tilesY, double width)
    {
      double tileW = (width - 40 - ((TilesPerRow - 1) * TileGap)) / TilesPerRow;
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

    /// <summary>What every panel reads: the section, its frames' x, whether frames are drawn one by one, and which scales to use.</summary>
    private sealed record PanelView(
      RunSection Section,
      Func<double, double> XOf,
      Func<int, double> XOfFrame,
      bool PerFrame,
      bool WholeRunScales,
      double PlotX1
    )
    {
      public RunChartData Data => Section.Data;

      public double From => Section.FromSeconds;

      public double To => Section.ToSeconds;

      /// <summary>The frames the scales cover: the whole run's, or the section's.</summary>
      public (int Start, int End) ScaleFrames => WholeRunScales ? (0, Data.Frames.Count) : (Section.Start, Section.End);

      /// <summary>The section's frames by pixel column: its column, and its frames' range.</summary>
      public IEnumerable<(int Column, int Start, int End)> Columns(int start, int end) => PixelColumns.Walk(start, end, XOfFrame);
    }

    private static double TicksMs(long ticks) => ticks / (double)TimeSpan.TicksPerMillisecond;

    private static double MaxMs(WaveletMatrix values, int start, int end) => TicksMs(values.KthSmallest(start, end, end - start - 1));

    private static void ErrorPanel(List<CardShape> parts, List<CardPlot> plots, PanelView view, double errorY)
    {
      var section = view.Section;
      var data = view.Data;
      var absolute = data.AbsoluteErrors;
      var (scaleStart, scaleEnd) = absolute.Of(view.ScaleFrames.Start, view.ScaleFrames.End);
      double limit =
        scaleEnd > scaleStart
          ? ChartScale.ErrorLimit(
            MaxMs(absolute.Values, scaleStart, scaleEnd),
            absolute.Values.PercentileMs(scaleStart, scaleEnd, ChartScale.BulkPercentile)
          )
          : ChartScale.MinErrorLimitMs;
      double zeroY = errorY + (ErrorH / 2);
      double YOf(double value) => zeroY - (Math.Clamp(value, -limit, limit) / limit * ErrorH / 2);
      plots.Add(new CardPlot(ReportItem.AnimationError, PlotX0, errorY, view.PlotX1, errorY + ErrorH, view.From, view.To, -limit, limit));

      parts.Add(new TextShape(20, errorY - 16, "ANIMATION ERROR PER FRAME", "label", "start"));
      parts.Add(
        new TextShape(view.PlotX1, errorY - 16, "+ shown too soon, − shown too late; the band is within the error threshold", "vsync-n", "end")
      );
      double threshold = section.Run.ErrorThresholdTicks / (double)TimeSpan.TicksPerMillisecond;
      parts.Add(new RectShape("band", N(PlotX0, 1), N(YOf(threshold), 1), N(view.PlotX1 - PlotX0, 1), N(YOf(-threshold) - YOf(threshold), 1)));
      foreach (double position in ChartScale.ErrorTicks(limit).Where(t => t != 0))
      {
        double y = YOf(position);
        parts.Add(GridLine(y, view.PlotX1));
        parts.Add(new TextShape(PlotX0 - 10, y + 4, $"{Ms(position, sign: true)} ms", "vsync-n", "end"));
      }

      var errors = data.Errors;
      var clipped = new List<(double X, double Value, bool Top)>();
      if (view.PerFrame)
      {
        double barW = Math.Max(1.0, ((view.PlotX1 - PlotX0) / Math.Max(1, section.FrameCount)) - 0.6);
        foreach (int i in errors.FramesIn(section.Start, section.End))
        {
          double value = TicksMs(data.Frames[i].AnimationErrorTicks!.Value);
          if (Math.Abs(value) < 1e-9)
            continue;
          double x = view.XOfFrame(i);
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
        foreach (var (column, frameStart, frameEnd) in view.Columns(section.Start, section.End))
        {
          var (start, end) = errors.Of(frameStart, frameEnd);
          int count = end - start;
          if (count == 0)
            continue;
          double min = TicksMs(errors.Values.KthSmallest(start, end, 0));
          double max = MaxMs(errors.Values, start, end);
          if (count >= MinFramesForTypical)
          {
            Bar(range, column, min, max);
            Bar(typical, column, errors.Values.PercentileMs(start, end, 0.05), errors.Values.PercentileMs(start, end, 0.95));
          }
          else
          {
            Bar(typical, column, min, max);
          }
          if (max > limit)
            clipped.Add((column + 0.5, max, true));
          if (min < -limit)
            clipped.Add((column + 0.5, min, false));
        }
        AddPath(parts, "bar-range", range);
        AddPath(parts, "bar", typical);
      }
      parts.Add(new LineShape("zero-line", N(PlotX0, 0), N(zeroY, 0), N(view.PlotX1, 0), N(zeroY, 0)));
      parts.Add(new TextShape(PlotX0 - 10, zeroY + 4, "0", "vsync-n", "end"));
      ClipMarks(parts, clipped, errorY, errorY + ErrorH, v => $"{Ms(v, sign: true)} ms", view.PlotX1);
      TimeTicks(parts, view.From, view.To, view.XOf, errorY + ErrorH);
    }

    private static void StepPanel(List<CardShape> parts, List<CardPlot> plots, PanelView view, double refreshMs, double stepY)
    {
      // Each frame's hold: from its first sighting to the next frame of its segment (in the section), at the next frame's display time step
      var section = view.Section;
      var data = view.Data;
      var holds = data.Holds;
      int holdEnd = Math.Max(section.Start, section.End - 1);
      var (scaleFrom, scaleTo) = view.WholeRunScales ? (0, data.Frames.Count) : (section.Start, holdEnd);
      var (scaleStart, scaleEnd) = holds.Of(scaleFrom, scaleTo);
      double top =
        scaleEnd > scaleStart
          ? ChartScale.StepTop(
            MaxMs(holds.Values, scaleStart, scaleEnd),
            holds.Values.PercentileMs(scaleStart, scaleEnd, ChartScale.BulkPercentile),
            refreshMs
          )
          : ChartScale.StepTop(Array.Empty<double>(), refreshMs);
      double YOf(double ms) => stepY + StepH - (Math.Min(ms, top) / top * StepH);
      double X1(int frame) => Math.Min(view.PlotX1, view.XOfFrame(frame + 1));
      plots.Add(new CardPlot(ReportItem.DisplayTimeStep, PlotX0, stepY, view.PlotX1, stepY + StepH, view.From, view.To, 0, top));

      parts.Add(new TextShape(20, stepY - 16, "DISPLAY TIME STEP: HOW LONG EACH FRAME STAYED ON SCREEN", "label", "start"));
      parts.Add(new TextShape(view.PlotX1, stepY - 16, "green as planned, red held too long (the next frame was late)", "vsync-n", "end"));
      foreach (double position in ChartScale.StepTicks(refreshMs, top).Where(t => t > 0))
      {
        double y = YOf(position);
        parts.Add(GridLine(y, view.PlotX1));
        parts.Add(new TextShape(PlotX0 - 10, y + 4, $"{Ms(Math.Round(position, 1))} ms", "vsync-n", "end"));
      }
      parts.Add(GridLine(YOf(0), view.PlotX1));
      parts.Add(new TextShape(PlotX0 - 10, YOf(0) + 4, "0", "vsync-n", "end"));

      var clipped = new List<(double X, double Value, bool Top)>();
      if (view.PerFrame)
      {
        var risers = new StringBuilder();
        var onTime = new StringBuilder();
        var late = new StringBuilder();
        (double X1, double Level)? previous = null;
        foreach (int i in holds.FramesIn(section.Start, holdEnd))
        {
          var next = data.Frames[i + 1];
          double level = TicksMs(next.DisplayDeltaTicks!.Value);
          double x0 = view.XOfFrame(i);
          double x1 = X1(i);
          double y = YOf(level);
          if (previous is { } before && Math.Abs(before.X1 - x0) < 1e-6 && Math.Abs(before.Level - level) > 1e-9)
            risers.Append($"M{Fixed(x0, 1)} {Fixed(YOf(before.Level), 1)}V{Fixed(y, 1)}");
          ((next.Flags & PresentedFrameFlags.Late) != 0 ? late : onTime).Append($"M{Fixed(x0, 1)} {Fixed(y, 1)}H{Fixed(x1, 1)}");
          if (level > top)
            clipped.Add(((x0 + x1) / 2, level, true));
          previous = (x1, level);
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
        var lateHolds = data.LateHolds;
        foreach (var (key, frameStart, frameEnd) in view.Columns(section.Start, holdEnd))
        {
          var (start, end) = holds.Of(frameStart, frameEnd);
          int count = end - start;
          if (count == 0)
            continue;
          // The holds end in frame order: the column's last hold ends last
          int last = frameEnd - 1;
          while (!holds.Frames[last])
            --last;
          double columnEnd = Math.Max(key + 1, X1(last));
          double lowest = TicksMs(holds.Values.KthSmallest(start, end, 0));
          double highest = MaxMs(holds.Values, start, end);
          bool anyLate = lateHolds.Frames.CountIn(frameStart, frameEnd) > 0;
          string box = $"M{Fixed(key, 0)} {Fixed(YOf(highest) - 1.25, 1)}H{Fixed(columnEnd, 1)}V{Fixed(YOf(lowest) + 1.25, 1)}H{Fixed(key, 0)}Z";
          if (highest > top)
            clipped.Add((key + 0.5, highest, true));
          if (count < MinFramesForTypical)
          {
            // Few holds: all of them solid
            (anyLate ? solidLate : solid).Append(box);
            continue;
          }
          (anyLate ? lateRange : onTimeRange).Append(box);
          // A hold that happened (the lower middle one), not an interpolation between two levels; red when a hold of that level was late
          long middle = holds.Values.KthSmallest(start, end, (count - 1) / 2);
          var (lateStart, lateEnd) = lateHolds.Of(frameStart, frameEnd);
          bool middleLate =
            lateEnd > lateStart
            && lateHolds.Values.CountBelow(lateStart, lateEnd, middle + 1) > lateHolds.Values.CountBelow(lateStart, lateEnd, middle);
          (middleLate ? medianLate : median).Append($"M{Fixed(key, 0)} {Fixed(YOf(TicksMs(middle)), 1)}H{Fixed(columnEnd, 1)}");
        }
        AddPath(parts, "held-range", onTimeRange);
        AddPath(parts, "held-range-late", lateRange);
        AddPath(parts, "held", median);
        AddPath(parts, "held-late", medianLate);
        AddPath(parts, "held-fill", solid);
        AddPath(parts, "held-fill-late", solidLate);
      }
      ClipMarks(parts, clipped, stepY, stepY + StepH, v => $"{Ms(v)} ms", view.PlotX1);
      TimeTicks(parts, view.From, view.To, view.XOf, stepY + StepH);
    }

    /// <summary>
    /// The application side, from the markers, on the display time step's whole-refresh grid: each frame's frametime (from its CPU start to the
    /// next frame's) as a step, and its CPU busy (from its CPU start until it was presented) as a faint bar, held from the frame's display time
    /// to the next frame's.
    /// </summary>
    private static void FrameTimePanel(List<CardShape> parts, List<CardPlot> plots, PanelView view, double refreshMs, double frameTimeY)
    {
      var section = view.Section;
      var data = view.Data;
      var frames = data.Frames;
      parts.Add(new TextShape(20, frameTimeY - 16, "FRAMETIME AND CPU BUSY: THE APPLICATION SIDE, FROM THE MARKERS", "label", "start"));
      if (data.Spans.CountIn(section.Start, section.End) == 0)
      {
        parts.Add(new TextShape(PlotX0, frameTimeY + (FrameTimeH / 2), "the markers carry no CPU start time or CPU busy", "vsync-n", "start"));
        return;
      }
      parts.Add(
        new TextShape(view.PlotX1, frameTimeY - 16, "blue: frametime (CPU start to the next); faint: CPU busy (until presented)", "vsync-n", "end")
      );
      var frameTimes = data.FrameTimes;
      var cpuBusy = data.CpuBusy;
      int Combined(int frame) => frameTimes.Frames.Rank(frame) + cpuBusy.Frames.Rank(frame);
      var (scaleFrom, scaleTo) = view.ScaleFrames;
      int scaleStart = Combined(scaleFrom);
      int scaleEnd = Combined(scaleTo);
      var both = data.FrameTimesAndCpuBusy;
      double top =
        scaleEnd > scaleStart
          ? ChartScale.StepTop(MaxMs(both, scaleStart, scaleEnd), both.PercentileMs(scaleStart, scaleEnd, ChartScale.BulkPercentile), refreshMs)
          : ChartScale.StepTop(Array.Empty<double>(), refreshMs);
      double YOf(double ms) => frameTimeY + FrameTimeH - (Math.Min(ms, top) / top * FrameTimeH);
      // A span ends at the next frame of its segment in the section, else after the frame's time on screen
      double X1(int i) =>
        Math.Min(
          view.PlotX1,
          i + 1 < section.End && frames[i + 1].Segment == frames[i].Segment
            ? view.XOfFrame(i + 1)
            : view.XOf(data.Seconds(i) + (frames[i].OnScreenTicks / (double)TimeSpan.TicksPerSecond))
        );
      plots.Add(new CardPlot(ReportItem.FrameTime, PlotX0, frameTimeY, view.PlotX1, frameTimeY + FrameTimeH, view.From, view.To, 0, top));
      foreach (double position in ChartScale.StepTicks(refreshMs, top).Where(t => t > 0))
      {
        double y = YOf(position);
        parts.Add(GridLine(y, view.PlotX1));
        parts.Add(new TextShape(PlotX0 - 10, y + 4, $"{Ms(Math.Round(position, 1))} ms", "vsync-n", "end"));
      }
      parts.Add(GridLine(YOf(0), view.PlotX1));
      parts.Add(new TextShape(PlotX0 - 10, YOf(0) + 4, "0", "vsync-n", "end"));

      var clipped = new List<(double X, double Value, bool Top)>();
      var busy = new StringBuilder();
      double bottom = YOf(0);
      if (view.PerFrame)
      {
        var steps = new StringBuilder();
        for (int i = section.Start; i < section.End; ++i)
        {
          if (!data.Spans[i])
            continue;
          double frameTime = frames[i].FrameTimeTicks is { } ticks ? TicksMs(ticks) : 0;
          double cpuBusyMs = TicksMs(frames[i].CpuBusyTicks);
          double x0 = view.XOfFrame(i);
          double x1 = X1(i);
          if (cpuBusyMs > 0)
            busy.Append($"M{Fixed(x0, 1)} {Fixed(YOf(cpuBusyMs), 1)}H{Fixed(x1, 1)}V{Fixed(bottom, 1)}H{Fixed(x0, 1)}Z");
          if (frameTime > 0)
            steps.Append($"M{Fixed(x0, 1)} {Fixed(YOf(frameTime), 1)}H{Fixed(x1, 1)}");
          double highest = Math.Max(frameTime, cpuBusyMs);
          if (highest > top)
            clipped.Add(((x0 + x1) / 2, highest, true));
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
        var segmentEnds = data.SegmentEnds;
        foreach (var (key, frameStart, frameEnd) in view.Columns(section.Start, section.End))
        {
          if (data.Spans.CountIn(frameStart, frameEnd) == 0)
            continue;
          // Spans end in frame order, except at the ends of segments and of the section: the column's last span, and any segment's end in it
          int last = frameEnd - 1;
          while (!data.Spans[last])
            --last;
          double end = Math.Max(key + 1, X1(last));
          int firstEnd = RunChartData.FirstWhere(0, segmentEnds.Count, k => segmentEnds[k] >= frameStart);
          for (int k = firstEnd; k < segmentEnds.Count && segmentEnds[k] < frameEnd; ++k)
          {
            if (data.Spans[segmentEnds[k]])
              end = Math.Max(end, X1(segmentEnds[k]));
          }
          var (busyStart, busyEnd) = cpuBusy.Of(frameStart, frameEnd);
          var (levelStart, levelEnd) = frameTimes.Of(frameStart, frameEnd);
          int busyCount = busyEnd - busyStart;
          int levelCount = levelEnd - levelStart;
          if (busyCount > 0)
          {
            double middleBusy = TicksMs(cpuBusy.Values.KthSmallest(busyStart, busyEnd, (busyCount - 1) / 2));
            busy.Append($"M{Fixed(key, 0)} {Fixed(YOf(middleBusy), 1)}H{Fixed(end, 1)}V{Fixed(bottom, 1)}H{Fixed(key, 0)}Z");
          }
          double highest = Math.Max(
            levelCount > 0 ? MaxMs(frameTimes.Values, levelStart, levelEnd) : 0,
            busyCount > 0 ? MaxMs(cpuBusy.Values, busyStart, busyEnd) : 0
          );
          if (highest > top)
            clipped.Add((key + 0.5, highest, true));
          if (levelCount == 0)
            continue;
          double lowest = TicksMs(frameTimes.Values.KthSmallest(levelStart, levelEnd, 0));
          double levelMax = MaxMs(frameTimes.Values, levelStart, levelEnd);
          string box = $"M{Fixed(key, 0)} {Fixed(YOf(levelMax) - 1.25, 1)}H{Fixed(end, 1)}V{Fixed(YOf(lowest) + 1.25, 1)}H{Fixed(key, 0)}Z";
          if (levelCount < MinFramesForTypical)
          {
            solid.Append(box);
            continue;
          }
          range.Append(box);
          double middle = TicksMs(frameTimes.Values.KthSmallest(levelStart, levelEnd, (levelCount - 1) / 2));
          median.Append($"M{Fixed(key, 0)} {Fixed(YOf(middle), 1)}H{Fixed(end, 1)}");
        }
        AddPath(parts, "cpu-busy", busy);
        AddPath(parts, "frametime-range", range);
        AddPath(parts, "frametime", median);
        AddPath(parts, "frametime-fill", solid);
      }
      ClipMarks(parts, clipped, frameTimeY, frameTimeY + FrameTimeH, v => $"{Ms(v)} ms", view.PlotX1);
      TimeTicks(parts, view.From, view.To, view.XOf, frameTimeY + FrameTimeH);
    }

    private static void LatePanel(List<CardShape> parts, List<CardPlot> plots, PanelView view, double lateY)
    {
      var section = view.Section;
      var data = view.Data;
      var pacing = section.Run.Run.Pacing;
      parts.Add(new TextShape(20, lateY - 16, $"SHARE OF LATE FRAMES IN THE LAST {LateShare.WindowSeconds:0} S", "label", "start"));
      if (pacing == null || data.Frames.Count == 0 || data.LateShare is not { } late)
      {
        parts.Add(new TextShape(PlotX0, lateY + (LateH / 2), "no pacing information", "vsync-n", "start"));
        return;
      }
      string note = $"whole run {Percent(pacing.LateShare)}";
      if (late.AnyHeldLonger)
        note =
          $"amber: on screen longer than a refresh ({Ms1(pacing.RefreshPeriodMs)} ms) as the pacer intended; red: longer than it intended; "
          + $"whole run {Percent(pacing.LateShare)}";
      parts.Add(new TextShape(view.PlotX1, lateY - 16, note, "vsync-n", "end"));
      var (scaleStart, scaleEnd) = view.ScaleFrames;
      double max = scaleEnd > scaleStart ? LateShareData.ShareOf(late.Shares.KthSmallest(scaleStart, scaleEnd, scaleEnd - scaleStart - 1)) : 0;
      double topShare = NiceCeiling(Math.Max(5, max * 1.25));
      double YOf(double share) => lateY + LateH - (share / topShare * LateH);
      plots.Add(new CardPlot(ReportItem.LateShare, PlotX0, lateY, view.PlotX1, lateY + LateH, view.From, view.To, 0, topShare));
      foreach (double tick in new[] { 0, topShare / 2, topShare })
      {
        parts.Add(GridLine(YOf(tick), view.PlotX1));
        parts.Add(
          new TextShape(PlotX0 - 10, YOf(tick) + 4, tick == 0 ? "0" : $"{tick.ToString("0.##", CultureInfo.InvariantCulture)} %", "vsync-n", "end")
        );
      }
      // Red where a frame in the window was late (later than its target: the pacer's intent in the markers, else one refresh), amber
      // where frames were only on screen longer than a refresh as the pacer intended, green where every frame took one refresh; each
      // stretch starts where the previous one ended, so the line is whole
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
      string Style(int start, int end) =>
        late.AnyLate.CountIn(start, end) > 0 ? "late-line"
        : late.AnyHeld.CountIn(start, end) > 0 ? "late-line-adapted"
        : "late-line-none";
      if (view.PerFrame)
      {
        for (int i = section.Start; i < section.End; ++i)
          Point(view.XOfFrame(i), YOf(late.Percent[i]), Style(i, i + 1));
      }
      else
      {
        // The column's worst: red if any of its frames' windows held a late frame, else amber if any held one on screen longer than a refresh
        foreach (var (key, start, end) in view.Columns(section.Start, section.End))
        {
          double low = LateShareData.ShareOf(late.Shares.KthSmallest(start, end, 0));
          double high = LateShareData.ShareOf(late.Shares.KthSmallest(start, end, end - start - 1));
          string style = Style(start, end);
          Point(key + 0.5, YOf(low), style);
          if (high > low)
            Point(key + 0.5, YOf(high), style);
        }
      }
      foreach (var (style, path) in paths)
        AddPath(parts, style, path);
      TimeTicks(parts, view.From, view.To, view.XOf, lateY + LateH);
    }

    /// <summary>
    /// One cell per refresh, from each frame's first capture: a new shade with every frame, late frames red. A capture card sees whole refreshes,
    /// so the refreshes between a frame's last capture and the next frame (captures that could not be decoded) are unknown cells; a camera sees
    /// each frame until the next one. Frames with skipped frame indices before them, or torn, get a mark above the strip.
    /// </summary>
    private static void StripPanel(List<CardShape> parts, List<CardPlot> plots, PanelView view, double refreshMs, ChartRun chart, double stripY)
    {
      var section = view.Section;
      var frames = view.Data.Frames;
      double from = view.From;
      double to = view.To;
      double cellW = (view.PlotX1 - PlotX0) * refreshMs / 1000 / (to - from);
      parts.Add(new TextShape(20, stripY - 16, "REFRESH STRIP", "label", "start"));
      if (cellW < MinCellPixels)
      {
        double longest = (view.PlotX1 - PlotX0) / MinCellPixels * refreshMs / 1000;
        parts.Add(new TextShape(view.PlotX1, stripY - 16, "one cell per refresh, a new shade with every new frame", "vsync-n", "end"));
        parts.Add(
          new TextShape(PlotX0, stripY + (StripH / 2) + 4, $"render a section of at most {Ms1(longest)} s to see the refreshes", "vsync-n", "start")
        );
        return;
      }
      plots.Add(new CardPlot(ReportItem.RefreshStrip, PlotX0, stripY, view.PlotX1, stripY + StripH, from, to, 0, 1));
      int legend = parts.Count;
      parts.Add(new TextShape(view.PlotX1, stripY - 16, string.Empty, "vsync-n", "end"));
      long refreshTicks = (long)Math.Round(refreshMs * TimeSpan.TicksPerMillisecond);
      int Cells(long ticks) => refreshTicks > 0 ? (int)Math.Max(0, (ticks + (refreshTicks / 2)) / refreshTicks) : 1;
      var marks = new StringBuilder();
      bool anyUnknown = false;
      for (int i = section.Start; i < section.End; ++i)
      {
        var frame = frames[i];
        long onScreen = frame.OnScreenTicks > 0 ? frame.OnScreenTicks : chart.CapturePeriodTicks;
        int cells = Math.Max(1, Cells(onScreen));
        int seen = cells;
        if (!chart.Camera && i + 1 < section.End && frames[i + 1].Segment == frame.Segment)
        {
          long shown = Math.Min(frame.LastSeenTicks + chart.CapturePeriodTicks, frames[i + 1].FirstSeenTicks) - frame.FirstSeenTicks;
          seen = Math.Clamp(Cells(shown), 1, cells);
        }
        string cls =
          (frame.Flags & PresentedFrameFlags.Late) != 0 ? "strip-late"
          : (i - section.Start) % 2 == 0 ? "strip-a"
          : "strip-b";
        double x0 = view.XOfFrame(i);
        for (int c = 0; c < cells; ++c)
        {
          double x = x0 + (c * cellW);
          if (x >= view.PlotX1)
            break;
          anyUnknown |= c >= seen;
          parts.Add(
            new RectShape(
              c < seen ? cls : "neutral",
              N(x + 0.5, 1),
              N(stripY, 0),
              N(Math.Max(0.5, Math.Min(cellW - 1, view.PlotX1 - x - 0.5)), 1),
              N(StripH, 0),
              "2"
            )
          );
        }
        if (frame.SkippedBefore > 0 || (frame.Flags & PresentedFrameFlags.Torn) != 0)
          marks.Append($"M{Fixed(x0 + 0.5, 1)} {Fixed(stripY - 2, 1)}L{Fixed(x0 - 3, 1)} {Fixed(stripY - 8, 1)}H{Fixed(x0 + 4, 1)}Z");
      }
      AddPath(parts, "strip-mark", marks);
      parts[legend] = new TextShape(
        view.PlotX1,
        stripY - 16,
        "one cell per refresh, a new shade with every new frame, late frames red"
          + (anyUnknown ? ", grey not decoded" : string.Empty)
          + (marks.Length > 0 ? "; ▾ skipped frame indices or a tear" : string.Empty),
        "vsync-n",
        "end"
      );
      TimeTicks(parts, from, to, view.XOf, stripY + StripH);
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Values beyond the panel's scale: a triangle at its edge, and the value beside it, largest first and where it does not overlap.</summary>
    private static void ClipMarks(
      List<CardShape> parts,
      List<(double X, double Value, bool Top)> clipped,
      double top,
      double bottom,
      Func<double, string> format,
      double plotX1
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
        if (right > plotX1)
          (left, right) = (x - 6 - (text.Length * 6.2), x - 6);
        if (placed.Any(p => p.Top == atTop && left < p.Right + 4 && right > p.Left - 4))
          continue;
        placed.Add((left, right, atTop));
        parts.Add(new TextShape(left, atTop ? edge + 17 : edge - 8, text, "clip-text", "start"));
      }
    }

    private static readonly double[] g_timeSteps = { 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600 };

    /// <summary>The time axis under a panel: a label every "nice" step, at most a dozen.</summary>
    internal static void TimeTicks(List<CardShape> parts, double from, double to, Func<double, double> xOf, double bottom)
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

    internal static LineShape GridLine(double y, double plotX1) => new LineShape("grid", N(PlotX0, 0), N(y, 1), N(plotX1, 0), N(y, 1));

    internal static void AddPath(List<CardShape> parts, string cls, StringBuilder d)
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

    internal static string Ms1(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Percent(double share) => share.ToString("P1", CultureInfo.InvariantCulture);

    private static string Hz(RunPacing? pacing) => pacing != null ? $"{pacing.RefreshHz.ToString("0.##", CultureInfo.InvariantCulture)} Hz" : "?? Hz";
  }
}
