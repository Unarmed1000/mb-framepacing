//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A run, or a section of one, as one card (shapes: CardDrawing, written as SVG by SvgCardWriter, drawn by the GUI) in the style of mb-framepacing-explained's example charts (generate_charts.py): the headline
//* tiles, the animation error per frame as signed bars, the display time step as held steps (green as planned, red held too long), the share
//* of late frames in the last 2 s, and the refresh strip; opt-in, the animation time step over the display time step (as generate_charts.py
//* draws them), and for a card in a document its own title, a strip of the first seconds and a tiles row of its own width (ReportOptions).
//* The x axis is seconds since the run's first frame, as on the
//* Timeline. A section short enough to give every frame a pixel draws every frame; longer ones draw each pixel column's range (as the GUI's
//* plottables do), so an hour stays a small file. The error and display time step scales follow the GUI charts, including their marks for
//* the few values far beyond the rest.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
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

    /// <summary>The plots' width on a card <paramref name="width"/> units wide.</summary>
    public static double PlotWidth(double width) => width - (Width - PlotX1) - PlotX0;

    private const double TileH = 64;
    private const double TileGap = 12;
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
    /// otherwise each scales to the section. With <paramref name="visible"/> (seconds, inside the section) the plots show that range and the
    /// rest of the section lies beyond their edges, in the panels' scrolling layers (ScrollShape): the GUI's scrolling window, which it moves
    /// without building again.
    /// </summary>
    public static CardDrawing Build(
      RunSection section,
      ReportOptions? options = null,
      bool wholeRunScales = false,
      double width = Width,
      (double From, double To)? visible = null
    )
    {
      double plotX1 = width - (Width - PlotX1);
      options ??= ReportOptions.Default;
      var chart = section.Run;
      var run = chart.Run;
      var pacing = run.Pacing;
      double refreshMs = pacing?.RefreshPeriodMs ?? (chart.CapturePeriodTicks / (double)TimeSpan.TicksPerMillisecond);
      double from = section.FromSeconds;
      double to = section.ToSeconds;
      var (viewFrom, viewTo) = visible ?? (from, to);
      var data = section.Data;
      double XOf(double seconds) => PlotX0 + ((plotX1 - PlotX0) * (seconds - viewFrom) / (viewTo - viewFrom));
      double XOfFrame(int index) => XOf(data.Seconds(index));
      int frameCount = section.FrameCount;
      // The section's frames over its width at the visible range's scale
      double pixelsPerFrame = frameCount > 0 ? (plotX1 - PlotX0) * ((to - from) / (viewTo - viewFrom)) / frameCount : double.MaxValue;
      bool perFrame = pixelsPerFrame >= 1;

      string title = (options.Title ?? RunHeadline.Title(run)) + (section.IsWholeRun ? string.Empty : $", {Ms1(from)}–{Ms1(to)} s");
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
      // The frame rates describe the frames that animate: counted from the prepared data, without the section's own numbers
      var (steps0, steps1) = section.Data.DisplaySteps.Of(section.Start, section.End);
      var (counted0, counted1) = section.Data.FrameRateSteps.Of(section.Start, section.End);
      int excluded = (steps1 - steps0) - (counted1 - counted0);
      if (RunHeadline.ExcludedStatic(excluded) is { } staticFrames)
        description.Add($"Frame rates and display time steps {staticFrames}: nothing animates in them.");
      if (RunHeadline.SequenceLine(run) is { } sequence)
        description.Add(sequence);
      // The section's own numbers only when a tile shows them
      var tiles = ReportItem.TileIds.Any(options.IsShown)
        ? RunHeadline.Tiles(section.Section).Where(t => options.IsShown(t.Id) && (t.HasValue || !options.HideEmpty)).ToList()
        : new List<HeadlineTile>();
      if (options.HideEmpty && options.IsShown(ReportItem.LateShare) && (section.Section.Run.Pacing?.LateFrames ?? 0) == 0)
        options = options.Hide(new[] { ReportItem.LateShare });
      var layout = Layout.For(options, description.Count, tiles.Count);
      var parts = Header(title, options.IsShown(ReportItem.Description) ? description : Array.Empty<string>(), options.IsShown(ReportItem.Title));
      if (options.IsShown(ReportItem.Display))
        DisplayBox(parts, chart, section, refreshMs, width);

      Tiles(parts, tiles, layout.TilesY, width, options.TilesPerRow);
      // The panels only read the section: each draws into shapes of its own, on the thread pool, joined in the card's order
      var panels = new List<Action<List<CardShape>, List<CardPlot>>>();
      var view = new PanelView(section, XOf, XOfFrame, perFrame, wholeRunScales, plotX1, viewFrom, viewTo, pixelsPerFrame);
      if (layout.ErrorY is { } errorY)
        panels.Add((shapes, plots) => ErrorPanel(shapes, plots, view, refreshMs, errorY));
      if (layout.StepY is { } stepY)
      {
        bool animation = options.IsShown(ReportItem.AnimationTimeStep);
        panels.Add((shapes, plots) => StepPanel(shapes, plots, view, refreshMs, stepY, animation));
      }
      if (layout.FrameTimeY is { } frameTimeY)
        panels.Add((shapes, plots) => FrameTimePanel(shapes, plots, view, refreshMs, frameTimeY));
      if (layout.LateY is { } lateY)
        panels.Add((shapes, plots) => LatePanel(shapes, plots, view, lateY));
      if (layout.StripY is { } stripY)
      {
        var (stripView, stripLabel) = StripView(view, options.StripSeconds);
        panels.Add((shapes, plots) => StripPanel(shapes, plots, stripView, refreshMs, chart, stripY, stripLabel));
      }
      var drawn = panels.Select(_ => (Shapes: new List<CardShape>(), Plots: new List<CardPlot>())).ToArray();
      Parallel.For(0, panels.Count, i => panels[i](drawn[i].Shapes, drawn[i].Plots));
      foreach (var (shapes, _) in drawn)
        parts.AddRange(shapes);

      return new CardDrawing(title, width, layout.Height, parts, drawn.SelectMany(d => d.Plots).ToList());
    }

    /// <summary>
    /// The refresh strip's view and label: the panels' view, or with <paramref name="seconds"/> shorter than the section, only the section's
    /// first seconds across the whole plot width, on a time axis of its own (fixed, not scrolling with the visible range).
    /// </summary>
    private static (PanelView View, string Label) StripView(PanelView view, double? seconds)
    {
      if (seconds is not { } length || view.To - view.From <= length)
        return (view, "REFRESH STRIP");
      var strip = RunSection.Create(view.Section.Run, view.From, view.From + length);
      double from = strip.FromSeconds;
      double to = strip.ToSeconds;
      double plotX1 = view.PlotX1;
      var data = view.Data;
      double XOf(double s) => PlotX0 + ((plotX1 - PlotX0) * (s - from) / (to - from));
      int frames = Math.Max(1, strip.FrameCount);
      var stripView = view with
      {
        Section = strip,
        XOf = XOf,
        XOfFrame = i => XOf(data.Seconds(i)),
        ViewFrom = from,
        ViewTo = to,
        PerFrame = (plotX1 - PlotX0) / frames >= 1,
        PixelsPerFrame = (plotX1 - PlotX0) / frames,
      };
      return (stripView, $"REFRESH STRIP, FIRST {length.ToString("0.##", CultureInfo.InvariantCulture)} S");
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
          int rows = (tiles + options.TilesPerRow - 1) / options.TilesPerRow;
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
      static bool Known(uint ticks) => ticks > 0 && ticks != MB.FramePacing.Marker.MarkerPayload.OnDemandFrameTicks;
      var refreshes = frames
        .Where(f => f.DisplayDeltaTicks.HasValue && (Known(f.MarkerTargetFrameTicks) || f.TargetTicks.HasValue))
        .Select(f =>
          (int)
            Math.Round(
              (Known(f.MarkerTargetFrameTicks) ? f.MarkerTargetFrameTicks : f.TargetTicks!.Value) / (double)TimeSpan.TicksPerMillisecond / refreshMs
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
      // What the application wants, when its markers say so
      var preferredFps = frames
        .Where(f => Known(f.MarkerPreferredFrameTicks))
        .Select(f => Math.Round(TimeSpan.TicksPerSecond / (double)f.MarkerPreferredFrameTicks, 1))
        .Distinct()
        .Order()
        .ToArray();
      bool onDemand = frames.Any(f => f.MarkerPreferredFrameTicks == MB.FramePacing.Marker.MarkerPayload.OnDemandFrameTicks);
      var wants = new List<string>();
      if (preferredFps.Length > 0)
      {
        string Fps(double fps) => fps.ToString("0.#", CultureInfo.InvariantCulture);
        wants.Add(
          preferredFps.Length == 1 ? $"preferred {Fps(preferredFps[0])} fps" : $"preferred {Fps(preferredFps[0])}\u2013{Fps(preferredFps[^1])} fps"
        );
      }
      if (onDemand)
        wants.Add("on demand");
      string preferred = wants.Count > 0 ? ", " + string.Join(", ", wants) : string.Empty;
      parts.Add(new TextShape(X + 14, Y + 60, $"{Ms1(refreshMs)} ms per refresh{target}{preferred}", "vsync-n", "start"));
    }

    private static string Refreshes(int count) => count == 1 ? "1 refresh" : $"{count} refreshes";

    private static void Tiles(List<CardShape> parts, IReadOnlyList<HeadlineTile> tiles, double tilesY, double width, int perRow)
    {
      double tileW = (width - 40 - ((perRow - 1) * TileGap)) / perRow;
      for (int i = 0; i < tiles.Count; ++i)
      {
        var tile = tiles[i];
        double x = 20 + ((i % perRow) * (tileW + TileGap));
        double y = tilesY + ((i / perRow) * (TileH + TileGap));
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
      double PlotX1,
      double ViewFrom,
      double ViewTo,
      double PixelsPerFrame
    )
    {
      /// <summary>Where the section ends on the card: beyond the plot's right edge when the visible range is shorter.</summary>
      public double EndX => XOf(Section.ToSeconds);

      /// <summary>A shape that moves with the time axis: into the scrolling layer of the band <paramref name="top"/> to <paramref name="bottom"/>.</summary>
      public void Move(List<CardShape> parts, CardShape shape, double top, double bottom) => AddScrolling(parts, shape, PlotX0, top, PlotX1, bottom);

      public void MovePath(List<CardShape> parts, string cls, StringBuilder d, double top, double bottom)
      {
        if (d.Length > 0)
          Move(parts, new PathShape(cls, d.ToString()), top, bottom);
      }

      /// <summary>The time axis under a panel, for the section at the visible range's step: labels scroll, a little beyond the plot's edges.</summary>
      public void Ticks(List<CardShape> parts, double bottom) =>
        TimeTicks(
          shape => AddScrolling(parts, shape, PlotX0 - TickOverhang, bottom, PlotX1 + TickOverhang, bottom + 26),
          Section.FromSeconds,
          Section.ToSeconds,
          ViewTo - ViewFrom,
          XOf,
          bottom
        );

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

    private static void ErrorPanel(List<CardShape> parts, List<CardPlot> plots, PanelView view, double refreshMs, double errorY)
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
      plots.Add(new CardPlot(ReportItem.AnimationError, PlotX0, errorY, view.PlotX1, errorY + ErrorH, view.ViewFrom, view.ViewTo, -limit, limit));

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

      // A line at every whole number of refreshes an error reaches, over the frames the scale covers: an error of a refresh or more is a
      // frame shown a whole refresh early or late
      var errors = data.Errors;
      var (rangeStart, rangeEnd) = errors.Of(view.ScaleFrames.Start, view.ScaleFrames.End);
      if (rangeEnd > rangeStart)
      {
        double lowest = TicksMs(errors.Values.KthSmallest(rangeStart, rangeEnd, 0));
        double highest = MaxMs(errors.Values, rangeStart, rangeEnd);
        foreach (double position in ChartScale.ErrorRefreshTicks(refreshMs, limit, lowest, highest))
        {
          double y = YOf(position);
          int refreshes = (int)Math.Round(Math.Abs(position) / refreshMs);
          string text = $"{(position > 0 ? "+" : "−")}{refreshes} refresh{(refreshes == 1 ? string.Empty : "es")} ({Ms(Math.Abs(position))} ms)";
          parts.Add(new LineShape("error-refresh", N(PlotX0, 0), N(y, 1), N(view.PlotX1, 0), N(y, 1)));
          parts.Add(new TextShape(view.PlotX1 - 4, position > 0 ? y - 4 : y + 12, text, "error-refresh-text", "end"));
        }
      }
      var clipped = new List<(double X, double Value, bool Top)>();
      if (view.PerFrame)
      {
        double barW = Math.Max(1.0, (section.FrameCount > 0 ? view.PixelsPerFrame : view.PlotX1 - PlotX0) - 0.6);
        foreach (int i in errors.FramesIn(section.Start, section.End))
        {
          double value = TicksMs(data.Frames[i].AnimationErrorTicks!.Value);
          if (Math.Abs(value) < 1e-9)
            continue;
          double x = view.XOfFrame(i);
          var (y0, y1) = (Math.Min(zeroY, YOf(value)), Math.Max(zeroY, YOf(value)));
          view.Move(parts, new RectShape("bar", N(x, 2), N(y0, 1), N(barW, 2), N(Math.Max(MinBarHeight, y1 - y0), 1)), errorY, errorY + ErrorH);
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
        view.MovePath(parts, "bar-range", range, errorY, errorY + ErrorH);
        view.MovePath(parts, "bar", typical, errorY, errorY + ErrorH);
      }
      parts.Add(new LineShape("zero-line", N(PlotX0, 0), N(zeroY, 0), N(view.PlotX1, 0), N(zeroY, 0)));
      parts.Add(new TextShape(PlotX0 - 10, zeroY + 4, "0", "vsync-n", "end"));
      ClipMarks(
        shape => view.Move(parts, shape, errorY, errorY + ErrorH),
        clipped,
        errorY,
        errorY + ErrorH,
        v => $"{Ms(v, sign: true)} ms",
        view.PlotX1
      );
      view.Ticks(parts, errorY + ErrorH);
    }

    /// <summary>
    /// The display time step as held steps (green as planned, red held too long). With <paramref name="animation"/>, the animation time step
    /// under it as a blue line on the same holds and scale: an even display with an uneven animation is delta time jitter.
    /// </summary>
    private static void StepPanel(List<CardShape> parts, List<CardPlot> plots, PanelView view, double refreshMs, double stepY, bool animation)
    {
      // Each frame's hold: from its first sighting to the next frame of its segment (in the section), at the next frame's display time step
      var section = view.Section;
      var data = view.Data;
      var holds = data.Holds;
      int holdEnd = Math.Max(section.Start, section.End - 1);
      var (scaleFrom, scaleTo) = view.WholeRunScales ? (0, data.Frames.Count) : (section.Start, holdEnd);
      // The scale covers the display time steps, and the animation time steps when they are drawn too
      var scales = new List<(double Longest, double Bulk)>();
      foreach (var sequence in animation ? new[] { holds, data.AnimationHolds } : new[] { holds })
      {
        var (scaleStart, scaleEnd) = sequence.Of(scaleFrom, scaleTo);
        if (scaleEnd > scaleStart)
          scales.Add((MaxMs(sequence.Values, scaleStart, scaleEnd), sequence.Values.PercentileMs(scaleStart, scaleEnd, ChartScale.BulkPercentile)));
      }
      double top =
        scales.Count > 0
          ? ChartScale.StepTop(scales.Max(s => s.Longest), scales.Max(s => s.Bulk), refreshMs)
          : ChartScale.StepTop(Array.Empty<double>(), refreshMs);
      // An animation time step can be 0 or less (the animation clock stood still or went back): drawn at 0
      double YOf(double ms) => stepY + StepH - (Math.Clamp(ms, 0, top) / top * StepH);
      double X1(int frame) => Math.Min(view.EndX, view.XOfFrame(frame + 1));
      plots.Add(new CardPlot(ReportItem.DisplayTimeStep, PlotX0, stepY, view.PlotX1, stepY + StepH, view.ViewFrom, view.ViewTo, 0, top));

      if (animation)
      {
        parts.Add(new TextShape(20, stepY - 16, "DISPLAY TIME STEP AND ANIMATION TIME STEP", "label", "start"));
        parts.Add(new TextShape(view.PlotX1, stepY - 16, "green display time step (red held too long), blue animation time step", "vsync-n", "end"));
      }
      else
      {
        parts.Add(new TextShape(20, stepY - 16, "DISPLAY TIME STEP: HOW LONG EACH FRAME STAYED ON SCREEN", "label", "start"));
        parts.Add(new TextShape(view.PlotX1, stepY - 16, "green as planned, red held too long (the next frame was late)", "vsync-n", "end"));
      }
      foreach (double position in ChartScale.StepTicks(refreshMs, top).Where(t => t > 0))
      {
        double y = YOf(position);
        parts.Add(GridLine(y, view.PlotX1));
        parts.Add(new TextShape(PlotX0 - 10, y + 4, $"{Ms(Math.Round(position, 1))} ms", "vsync-n", "end"));
      }
      parts.Add(GridLine(YOf(0), view.PlotX1));
      parts.Add(new TextShape(PlotX0 - 10, YOf(0) + 4, "0", "vsync-n", "end"));
      if (animation)
        AnimationSteps(parts, view, holdEnd, YOf, X1, stepY);

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
        view.MovePath(parts, "riser", risers, stepY, stepY + StepH);
        view.MovePath(parts, "held", onTime, stepY, stepY + StepH);
        view.MovePath(parts, "held-late", late, stepY, stepY + StepH);
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
        view.MovePath(parts, "held-range", onTimeRange, stepY, stepY + StepH);
        view.MovePath(parts, "held-range-late", lateRange, stepY, stepY + StepH);
        view.MovePath(parts, "held", median, stepY, stepY + StepH);
        view.MovePath(parts, "held-late", medianLate, stepY, stepY + StepH);
        view.MovePath(parts, "held-fill", solid, stepY, stepY + StepH);
        view.MovePath(parts, "held-fill-late", solidLate, stepY, stepY + StepH);
      }
      ClipMarks(shape => view.Move(parts, shape, stepY, stepY + StepH), clipped, stepY, stepY + StepH, v => $"{Ms(v)} ms", view.PlotX1);
      view.Ticks(parts, stepY + StepH);
    }

    /// <summary>
    /// The animation time step on the display time step's holds (under them): per frame one blue line, stepping at each hold's start; per pixel
    /// column the range faint and the lower middle value as the line. Values beyond the scale stay at its edge (the display's marks tell).
    /// </summary>
    private static void AnimationSteps(
      List<CardShape> parts,
      PanelView view,
      int holdEnd,
      Func<double, double> yOf,
      Func<int, double> x1Of,
      double stepY
    )
    {
      var section = view.Section;
      var steps = view.Data.AnimationHolds;
      var line = new StringBuilder();
      if (view.PerFrame)
      {
        double? previousX1 = null;
        foreach (int i in steps.FramesIn(section.Start, holdEnd))
        {
          double x0 = view.XOfFrame(i);
          double x1 = x1Of(i);
          double y = yOf(TicksMs(view.Data.Frames[i + 1].AnimationDeltaTicks!.Value));
          // Joined to the hold before when it ends where this one starts (a gap in the segment starts a new line)
          line.Append(
            previousX1 is { } before && Math.Abs(before - x0) < 1e-6
              ? $"V{Fixed(y, 1)}H{Fixed(x1, 1)}"
              : $"M{Fixed(x0, 1)} {Fixed(y, 1)}H{Fixed(x1, 1)}"
          );
          previousX1 = x1;
        }
        view.MovePath(parts, "step-line", line, stepY, stepY + StepH);
        return;
      }
      var range = new StringBuilder();
      foreach (var (key, frameStart, frameEnd) in view.Columns(section.Start, holdEnd))
      {
        var (start, end) = steps.Of(frameStart, frameEnd);
        int count = end - start;
        if (count == 0)
          continue;
        int last = frameEnd - 1;
        while (!steps.Frames[last])
          --last;
        double columnEnd = Math.Max(key + 1, x1Of(last));
        double lowest = TicksMs(steps.Values.KthSmallest(start, end, 0));
        double highest = MaxMs(steps.Values, start, end);
        range.Append($"M{Fixed(key, 0)} {Fixed(yOf(highest) - 0.6, 1)}H{Fixed(columnEnd, 1)}V{Fixed(yOf(lowest) + 0.6, 1)}H{Fixed(key, 0)}Z");
        double middle = TicksMs(steps.Values.KthSmallest(start, end, (count - 1) / 2));
        line.Append($"M{Fixed(key, 0)} {Fixed(yOf(middle), 1)}H{Fixed(columnEnd, 1)}");
      }
      view.MovePath(parts, "step-range", range, stepY, stepY + StepH);
      view.MovePath(parts, "step-line", line, stepY, stepY + StepH);
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
          view.EndX,
          i + 1 < section.End && frames[i + 1].Segment == frames[i].Segment
            ? view.XOfFrame(i + 1)
            : view.XOf(data.Seconds(i) + (frames[i].OnScreenTicks / (double)TimeSpan.TicksPerSecond))
        );
      plots.Add(new CardPlot(ReportItem.FrameTime, PlotX0, frameTimeY, view.PlotX1, frameTimeY + FrameTimeH, view.ViewFrom, view.ViewTo, 0, top));
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
        view.MovePath(parts, "cpu-busy", busy, frameTimeY, frameTimeY + FrameTimeH);
        view.MovePath(parts, "frametime", steps, frameTimeY, frameTimeY + FrameTimeH);
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
        view.MovePath(parts, "cpu-busy", busy, frameTimeY, frameTimeY + FrameTimeH);
        view.MovePath(parts, "frametime-range", range, frameTimeY, frameTimeY + FrameTimeH);
        view.MovePath(parts, "frametime", median, frameTimeY, frameTimeY + FrameTimeH);
        view.MovePath(parts, "frametime-fill", solid, frameTimeY, frameTimeY + FrameTimeH);
      }
      ClipMarks(
        shape => view.Move(parts, shape, frameTimeY, frameTimeY + FrameTimeH),
        clipped,
        frameTimeY,
        frameTimeY + FrameTimeH,
        v => $"{Ms(v)} ms",
        view.PlotX1
      );
      view.Ticks(parts, frameTimeY + FrameTimeH);
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
          $"amber: on screen longer than the application prefers, as the pacer intended; red: longer than it intended; "
          + $"whole run {Percent(pacing.LateShare)}";
      parts.Add(new TextShape(view.PlotX1, lateY - 16, note, "vsync-n", "end"));
      var (scaleStart, scaleEnd) = view.ScaleFrames;
      double max = scaleEnd > scaleStart ? LateShareData.ShareOf(late.Shares.KthSmallest(scaleStart, scaleEnd, scaleEnd - scaleStart - 1)) : 0;
      double topShare = NiceCeiling(Math.Max(5, max * 1.25));
      double YOf(double share) => lateY + LateH - (share / topShare * LateH);
      plots.Add(new CardPlot(ReportItem.LateShare, PlotX0, lateY, view.PlotX1, lateY + LateH, view.ViewFrom, view.ViewTo, 0, topShare));
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
        view.MovePath(parts, style, path, lateY, lateY + LateH);
      view.Ticks(parts, lateY + LateH);
    }

    /// <summary>
    /// One cell per refresh, from each frame's first capture: a new shade with every frame, late frames red. A capture card sees whole refreshes,
    /// so the refreshes between a frame's last capture and the next frame (captures that could not be decoded) are unknown cells; a camera sees
    /// each frame until the next one. Frames with skipped frame indices before them, or torn, get a mark above the strip.
    /// </summary>
    private static void StripPanel(
      List<CardShape> parts,
      List<CardPlot> plots,
      PanelView view,
      double refreshMs,
      ChartRun chart,
      double stripY,
      string label
    )
    {
      var section = view.Section;
      var frames = view.Data.Frames;
      double from = view.ViewFrom;
      double to = view.ViewTo;
      double cellW = (view.PlotX1 - PlotX0) * refreshMs / 1000 / (to - from);
      parts.Add(new TextShape(20, stripY - 16, label, "label", "start"));
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
          : i % 2 == 0 ? "strip-a"
          : "strip-b";
        double x0 = view.XOfFrame(i);
        for (int c = 0; c < cells; ++c)
        {
          double x = x0 + (c * cellW);
          if (x >= view.EndX)
            break;
          anyUnknown |= c >= seen;
          view.Move(
            parts,
            new RectShape(
              c < seen ? cls : "neutral",
              N(x + 0.5, 1),
              N(stripY, 0),
              N(Math.Max(0.5, Math.Min(cellW - 1, view.EndX - x - 0.5)), 1),
              N(StripH, 0),
              "2"
            ),
            stripY - 10,
            stripY + StripH
          );
        }
        if (frame.SkippedBefore > 0 || (frame.Flags & PresentedFrameFlags.Torn) != 0)
          marks.Append($"M{Fixed(x0 + 0.5, 1)} {Fixed(stripY - 2, 1)}L{Fixed(x0 - 3, 1)} {Fixed(stripY - 8, 1)}H{Fixed(x0 + 4, 1)}Z");
      }
      view.MovePath(parts, "strip-mark", marks, stripY - 10, stripY + StripH);
      parts[legend] = new TextShape(
        view.PlotX1,
        stripY - 16,
        "one cell per refresh, a new shade with every new frame, late frames red"
          + (anyUnknown ? ", grey not decoded" : string.Empty)
          + (marks.Length > 0 ? "; ▾ skipped frame indices or a tear" : string.Empty),
        "vsync-n",
        "end"
      );
      view.Ticks(parts, stripY + StripH);
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------

    // Time labels are centred on their time: those at the plot's edges reach this far beyond it
    private const double TickOverhang = 30;

    /// <summary>
    /// Add <paramref name="shape"/> to the scrolling layer (ScrollShape) of the clip area given, the last part when it is that layer, else a new
    /// one, so the shapes keep their order.
    /// </summary>
    private static void AddScrolling(List<CardShape> parts, CardShape shape, double left, double top, double right, double bottom)
    {
      if (
        parts.Count > 0
        && parts[^1] is ScrollShape last
        && last.Left == left
        && last.Top == top
        && last.Right == right
        && last.Bottom == bottom
        && last.Children is List<CardShape> children
      )
      {
        children.Add(shape);
        return;
      }
      parts.Add(new ScrollShape(left, top, right, bottom, new List<CardShape> { shape }));
    }

    /// <summary>Values beyond the panel's scale: a triangle at its edge, and the value beside it, largest first and where it does not overlap.</summary>
    private static void ClipMarks(
      Action<CardShape> add,
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
        add(new PathShape("clip-mark", $"M{Fixed(x, 1)} {Fixed(edge, 1)}L{Fixed(x - 4, 1)} {Fixed(inside, 1)}H{Fixed(x + 4, 1)}Z"));
        string text = format(value);
        double left = x + 6;
        double right = left + (text.Length * 6.2);
        if (right > plotX1)
          (left, right) = (x - 6 - (text.Length * 6.2), x - 6);
        if (placed.Any(p => p.Top == atTop && left < p.Right + 4 && right > p.Left - 4))
          continue;
        placed.Add((left, right, atTop));
        add(new TextShape(left, atTop ? edge + 17 : edge - 8, text, "clip-text", "start"));
      }
    }

    private static readonly double[] g_timeSteps = { 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600 };

    /// <summary>The time axis under a panel: a label every "nice" step, at most a dozen.</summary>
    internal static void TimeTicks(List<CardShape> parts, double from, double to, Func<double, double> xOf, double bottom) =>
      TimeTicks(parts.Add, from, to, to - from, xOf, bottom);

    /// <summary>The labels from <paramref name="from"/> to <paramref name="to"/> at the step for <paramref name="length"/> seconds, on whole steps.</summary>
    private static void TimeTicks(Action<CardShape> add, double from, double to, double length, Func<double, double> xOf, double bottom)
    {
      double step = g_timeSteps.FirstOrDefault(s => length / s <= 12, 3600);
      bool minutes = step >= 60;
      for (double t = Math.Ceiling(from / step) * step; t <= to + 1e-9; t += step)
      {
        string label = minutes
          ? $"{(t / 60).ToString("0.##", CultureInfo.InvariantCulture)} min"
          : $"{t.ToString("0.##", CultureInfo.InvariantCulture)} s";
        add(new TextShape(xOf(t), bottom + 18, label, "vsync-n"));
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
