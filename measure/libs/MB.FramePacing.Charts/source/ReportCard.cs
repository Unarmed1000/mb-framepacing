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

    // The events panel: the frames lane over the capture lane
    private const double LaneH = 10;
    private const double LaneGap = 6;
    private const double EventsH = (2 * LaneH) + LaneGap;

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
      double refreshMs = (pacing?.RefreshPeriod ?? chart.CapturePeriod).TotalMilliseconds;
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
          + (pacing != null ? $", measured against a {Ms1(pacing.TargetFrameTime.TotalMilliseconds)} ms target." : "."),
        $"Resolution {Ms1(chart.CapturePeriod.TotalMilliseconds)} ms (one capture period); error threshold "
          + $"{Ms(chart.ErrorThreshold.TotalMilliseconds)} ms.",
        perFrame ? "Every frame is drawn." : "Each pixel column shows its frames' range; with 20 or more, the middle 90 % solid and the rest faint.",
      };
      // The frame rates describe the frames that animate: counted from the prepared data, without the section's own numbers
      int excluded = section.Data.StaticSteps.CountIn(section.Start, section.End);
      if (RunHeadline.ExcludedStatic(excluded) is { } staticFrames)
        description.Add($"Frame rates and display time steps {staticFrames}: nothing animates while they are on screen.");
      // Static by assumption, not by a marker: said apart, so a stall that looks like a rest is not hidden
      int assumed = section.Data.AssumedStatic.CountIn(section.Start, section.End);
      if (assumed > 0)
        description.Add(
          $"{assumed.ToString("N0", CultureInfo.InvariantCulture)} static frame{(assumed == 1 ? " is" : "s are")} assumed: "
            + $"{(assumed == 1 ? "its" : "their")} flag was lost with a dropped frame."
        );
      // Capture gaps, by kind when the capture rows are known: the steps they made uncertain are not judged
      int uncertain = section.Data.UncertainSteps.CountIn(section.Start, section.End);
      string gaps = CaptureGaps(section);
      if (uncertain > 0 || gaps.Length > 0)
        description.Add(
          (gaps.Length > 0 ? $"Capture: {gaps}" : "Capture gaps (not decoded, not recorded, dropped by the source or missed)")
            + (
              uncertain > 0
                ? $"; {uncertain.ToString("N0", CultureInfo.InvariantCulture)} display time step{(uncertain == 1 ? string.Empty : "s")} across them not judged."
                : "."
            )
        );
      if (RunHeadline.SequenceLine(run) is { } sequence)
        description.Add(sequence);
      // The section's own numbers only when a tile shows them
      var tiles = ReportItem.TileIds.Any(options.IsShown) ? RunHeadline.Shown(section.Section, options).ToList() : new List<HeadlineTile>();
      if (options.HideEmpty && options.IsShown(ReportItem.LateShare) && (section.Section.Run.Pacing?.LateFrames ?? 0) == 0)
        options = options.Hide(new[] { ReportItem.LateShare });
      // The display box (RunHeadline, as the GUI's Display card); what the application wants gets a line of its own
      var display = options.IsShown(ReportItem.Display) ? RunHeadline.Display(section) : null;
      double displayH = display is { Wants.Length: > 0 } ? DisplayBoxH + DisplayLineH : DisplayBoxH;
      var layout = Layout.For(options, description.Count, tiles.Count, displayH);
      var parts = Header(title, options.IsShown(ReportItem.Description) ? description : Array.Empty<string>(), options.IsShown(ReportItem.Title));
      if (display != null)
        DisplayBox(parts, display, width, displayH);

      Tiles(parts, tiles, layout.TilesY, width, options.TilesPerRowFor(tiles.Count));
      // The panels only read the section: each draws into shapes of its own, on the thread pool, joined in the card's order
      var panels = new List<Action<List<CardShape>, List<CardPlot>>>();
      var view = new PanelView(section, XOf, XOfFrame, perFrame, wholeRunScales, options.ClampStatic, plotX1, viewFrom, viewTo, pixelsPerFrame);
      if (layout.ErrorY is { } errorY)
        panels.Add((shapes, plots) => ErrorPanel(shapes, plots, view, refreshMs, errorY));
      if (layout.StepY is { } stepY)
      {
        bool animation = options.IsShown(ReportItem.AnimationTimeStep);
        bool lines = options.IsShown(ReportItem.FrameTimeLines);
        panels.Add((shapes, plots) => StepPanel(shapes, plots, view, refreshMs, stepY, animation, lines));
      }
      if (layout.FrameTimeY is { } frameTimeY)
        panels.Add((shapes, plots) => FrameTimePanel(shapes, plots, view, refreshMs, frameTimeY, options.IsShown(ReportItem.FrameTimeLines)));
      if (layout.LateY is { } lateY)
        panels.Add((shapes, plots) => LatePanel(shapes, plots, view, lateY));
      if (layout.StripY is { } stripY)
      {
        var (stripView, stripLabel) = StripView(view, options.StripSeconds);
        panels.Add((shapes, plots) => StripPanel(shapes, plots, stripView, refreshMs, chart, stripY, stripLabel));
      }
      if (layout.EventsY is { } eventsY)
        panels.Add((shapes, plots) => EventsPanel(shapes, plots, view, chart, eventsY));
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
    private sealed record Layout(
      double TilesY,
      double? ErrorY,
      double? StepY,
      double? FrameTimeY,
      double? LateY,
      double? StripY,
      double? EventsY,
      double Height
    )
    {
      public static Layout For(ReportOptions options, int descriptionLines, int tiles, double displayH = DisplayBoxH)
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
          cursor = Math.Max(text > 0 ? text + 31 : 0, display ? 14 + displayH + 20 : 0);
          previous = "header";
        }
        double tilesY = cursor;
        if (tiles > 0)
        {
          int perRow = options.TilesPerRowFor(tiles);
          int rows = (tiles + perRow - 1) / perRow;
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
        var eventsY = Panel(ReportItem.Events, EventsH);
        double height = previous == "panel" ? cursor + BottomAfterPanel : cursor + BottomMargin;
        return new Layout(tilesY, errorY, stepY, frameTimeY, lateY, stripY, eventsY, height);
      }
    }

    /// <summary>
    /// The display in the top right corner (<see cref="RunHeadline.Display"/>): its refresh rate, whether it is the fixed refresh a capture card
    /// captures at (vsync) or calculated from a camera's frames, the time per refresh, what the frames targeted in whole refreshes, and what
    /// the application wants.
    /// </summary>
    private static void DisplayBox(List<CardShape> parts, DisplaySummary display, double width, double boxH)
    {
      const double BoxW = 300;
      double X = width - 20 - BoxW;
      const double Y = 14;
      parts.Add(new RectShape("tile", N(X, 1), N(Y, 0), N(BoxW, 0), N(boxH, 0), "10"));
      parts.Add(new TextShape(X + 14, Y + 19, "DISPLAY", "label", "start"));
      parts.Add(
        new TextRunsShape(
          X + 14,
          Y + 42,
          new[] { new TextRun(display.Rate, display.Mismatch ? "tile-value warn" : "tile-value"), new TextRun("  " + display.Kind, "vsync-n") }
        )
      );
      parts.Add(new TextShape(X + 14, Y + 60, display.Refresh, "vsync-n", "start"));
      if (display.Wants.Length > 0)
        parts.Add(new TextShape(X + 14, Y + 60 + DisplayLineH, display.Wants, "vsync-n", "start"));
    }

    // The display box's height, and one more line for what the application wants
    private const double DisplayBoxH = 68;
    private const double DisplayLineH = 16;

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
        // The value and its detail as one line: whatever draws it puts the detail right after the value
        var runs = new List<TextRun> { new TextRun(tile.Value, tile.Warning ? "tile-value warn" : "tile-value") };
        if (tile.Detail.Length > 0)
          runs.Add(new TextRun("  " + tile.Detail, "vsync-n"));
        parts.Add(new TextRunsShape(x + 14, y + 44, runs));
      }
    }

    /// <summary>What every panel reads: the section, its frames' x, whether frames are drawn one by one, and which scales to use.</summary>
    private sealed record PanelView(
      RunSection Section,
      Func<double, double> XOf,
      Func<int, double> XOfFrame,
      bool PerFrame,
      bool WholeRunScales,
      bool ClampStatic,
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

    /// <summary>
    /// A panel's key, right aligned on the baseline <paramref name="y"/>: per item a swatch and its word, as one line of text runs, so whatever
    /// draws it lays the pieces out (no width is guessed). A swatch is a coloured character in the colour of the data it stands for (the
    /// <c>key-</c> classes): ■ for a fill, ━ for a line (<c>Line</c>), ┅ for the refresh line, ▾ for the strip's mark, and ▐▌ in two colours
    /// for colours that alternate per frame (two classes).
    /// </summary>
    private static List<CardShape> Key(double right, double y, IReadOnlyList<(string[] Classes, string Text, bool Line)> items)
    {
      var runs = new List<TextRun>();
      foreach (var (classes, text, line) in items)
      {
        if (runs.Count > 0)
          runs.Add(new TextRun("  "));
        if (classes.Length == 0)
        {
          runs.Add(new TextRun(text));
          continue;
        }
        if (classes.Length == 2)
        {
          runs.Add(new TextRun("▐", g_keyColours[classes[0]]));
          runs.Add(new TextRun("▌", g_keyColours[classes[1]]));
        }
        else
        {
          string glyph =
            classes[0] is "error-refresh" or "ref-target" or "ref-preferred" ? "┅"
            : line ? "━"
            : "■";
          runs.Add(new TextRun(glyph, g_keyColours[classes[0]]));
        }
        runs.Add(new TextRun(" " + text));
      }
      return runs.Count > 0 ? new List<CardShape> { new TextRunsShape(right, y, runs, "vsync-n", "end") } : new List<CardShape>();
    }

    /// <summary>The kinds that colour a column of holds, the first one the column has: a late hold outweighs the others.</summary>
    private static readonly HoldKind[] g_holdPriority = { HoldKind.Late, HoldKind.OlderFrameBack, HoldKind.FramesDropped, HoldKind.Unknown };

    /// <summary>The key entries of the hold kinds besides "as planned", in the key's order.</summary>
    private static readonly (HoldKind Kind, string Text)[] g_holdKeys =
    {
      (HoldKind.Late, "held too long: the next frame was late"),
      (HoldKind.OlderFrameBack, "held: an older frame came back"),
      (HoldKind.FramesDropped, "held: frames never shown before the next"),
      (HoldKind.Unknown, "not known: a capture gap"),
    };

    /// <summary>A hold's class: <paramref name="prefix"/> (held, held-range, held-fill) and its kind's suffix.</summary>
    private static string HoldClass(string prefix, HoldKind kind) =>
      kind switch
      {
        HoldKind.Late => prefix + "-late",
        HoldKind.OlderFrameBack => prefix + "-older",
        HoldKind.FramesDropped => prefix + "-dropped",
        HoldKind.Unknown => prefix + "-unknown",
        _ => prefix,
      };

    /// <summary>The key colour of each kind of data a panel draws: its fill or stroke as a text fill.</summary>
    private static readonly Dictionary<string, string> g_keyColours = new Dictionary<string, string>
    {
      ["band"] = "key-faint",
      ["error-refresh"] = "key-amber",
      ["static-band"] = "key-violet-a",
      ["held"] = "key-green",
      ["held-late"] = "key-red",
      ["held-older"] = "key-pink",
      ["held-dropped"] = "key-orange",
      ["held-unknown"] = "key-unknown",
      ["strip-older"] = "key-pink",
      ["strip-dropped"] = "key-orange",
      ["step-line"] = "key-blue",
      ["frametime"] = "key-blue",
      ["cpu-busy"] = "key-blue-faint",
      ["late-line-none"] = "key-green",
      ["late-line-adapted"] = "key-amber",
      ["late-line"] = "key-red",
      ["strip-late"] = "key-red",
      ["strip-static-a"] = "key-violet-a",
      ["strip-static-b"] = "key-violet-b",
      ["neutral"] = "key-grey",
      ["ref-target"] = "key-light",
      ["ref-preferred"] = "key-amber",
      ["event-dropped"] = "key-orange",
      ["event-older"] = "key-pink",
      ["event-torn"] = "key-cyan",
      ["event-gap"] = "key-unknown",
      ["event-undecoded"] = "key-undecoded",
    };

    /// <summary>The key item of the static bands: what a band behind a panel's data means.</summary>
    private static readonly (string[] Classes, string Text, bool Line) g_staticKey = (new[] { "static-band" }, "static: nothing animates", false);

    /// <summary>
    /// About how wide a small (11 px, vsync-n: 0.04 em letter spacing) text is, in card units: per character by its kind, measured on rendered
    /// cards.
    /// </summary>
    private static double SmallTextWidth(string text)
    {
      double width = 0;
      foreach (char c in text)
      {
        width +=
          "il.,:;'|!".Contains(c) ? 2.6
          : "tfjr ".Contains(c) ? 3.4
          : c is 'm' or 'w' ? 8.7
          : c is 'M' or 'W' ? 9.5
          : char.IsUpper(c) ? 6.9
          : char.IsDigit(c) ? 6.0
          : char.IsLower(c) ? 5.7
          : 5.0;
        width += 0.44;
      }
      return width;
    }

    private const string StaticLabel = "static: nothing animates";

    // A band with a frame the analysis assumed static (a dropped frame took its flag): said apart from what the markers stated
    private const string AssumedStaticLabel = "static (assumed)";

    /// <summary>
    /// A violet band behind every stretch of static frames (nothing animates) from <paramref name="top"/> to <paramref name="bottom"/>: from the
    /// first static frame's display time to the next frame's. Bands closer than a pixel merge; with <paramref name="label"/>, a band wide enough
    /// says what it is: static, or assumed static when one of its frames is (PresentedFrameFlags.StaticAssumed).
    /// </summary>
    private static bool StaticBands(List<CardShape> parts, PanelView view, double top, double bottom, bool label)
    {
      var data = view.Data;
      var stretches = data.StaticStretches;
      if (stretches.Count == 0)
        return false;
      var section = view.Section;
      // The first stretch that ends after the section's first frame
      int lo = 0;
      int hi = stretches.Count;
      while (lo < hi)
      {
        int mid = (lo + hi) / 2;
        if (stretches[mid].End <= section.Start)
          lo = mid + 1;
        else
          hi = mid;
      }
      var bands = new List<(double X0, double X1, bool Assumed)>();
      for (int k = lo; k < stretches.Count && stretches[k].Start < section.End; ++k)
      {
        var (start, end) = stretches[k];
        var last = data.Frames[end - 1];
        double x0 = Math.Max(PlotX0 - 1, view.XOfFrame(Math.Max(start, section.Start)));
        double x1 = Math.Min(view.EndX, view.XOf(data.Seconds(end - 1) + last.OnScreen.TotalSeconds));
        if (x1 <= x0)
          continue;
        bool assumed = data.AssumedStatic.CountIn(start, end) > 0;
        if (bands.Count > 0 && x0 - bands[^1].X1 < 1)
          bands[^1] = (bands[^1].X0, Math.Max(bands[^1].X1, x1), bands[^1].Assumed || assumed);
        else
          bands.Add((x0, x1, assumed));
      }
      foreach (var (x0, x1, assumed) in bands)
      {
        view.Move(parts, new RectShape("static-band", N(x0, 1), N(top, 1), N(Math.Max(1, x1 - x0), 1), N(bottom - top, 1)), top, bottom);
        string text = assumed ? AssumedStaticLabel : StaticLabel;
        if (label && x1 - x0 >= SmallTextWidth(text) + 12)
          view.Move(parts, new TextShape(x0 + 6, top + 13, text, "static-text", "start"), top, bottom);
      }
      return bands.Count > 0;
    }

    private static double NanosecondsMs(long nanoseconds) => nanoseconds / (double)NanosecondTimeSpan.NanosecondsPerMillisecond;

    private static double MaxMs(WaveletMatrix values, int start, int end) => NanosecondsMs(values.KthSmallest(start, end, end - start - 1));

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

      parts.Add(new TextShape(20, errorY - 16, "ANIMATION ERROR PER FRAME: + SHOWN TOO SOON, − SHOWN TOO LATE", "label", "start"));
      var panelKey = new List<(string[] Classes, string Text, bool Line)> { (new[] { "band" }, "within the error threshold", false) };
      double threshold = section.Run.ErrorThreshold.TotalMilliseconds;
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
        double lowest = NanosecondsMs(errors.Values.KthSmallest(rangeStart, rangeEnd, 0));
        double highest = MaxMs(errors.Values, rangeStart, rangeEnd);
        foreach (double position in ChartScale.ErrorRefreshTicks(refreshMs, limit, lowest, highest))
        {
          double y = YOf(position);
          int refreshes = (int)Math.Round(Math.Abs(position) / refreshMs);
          string text = $"{(position > 0 ? "+" : "−")}{refreshes} refresh{(refreshes == 1 ? string.Empty : "es")} ({Ms(Math.Abs(position))} ms)";
          parts.Add(new LineShape("error-refresh", N(PlotX0, 0), N(y, 1), N(view.PlotX1, 0), N(y, 1)));
          parts.Add(new TextShape(view.PlotX1 - 4, position > 0 ? y - 4 : y + 12, text, "error-refresh-text", "end"));
          if (panelKey.Count == 1)
            panelKey.Add((new[] { "error-refresh" }, "a whole refresh early or late", true));
        }
      }
      if (StaticBands(parts, view, errorY, errorY + ErrorH, label: true))
        panelKey.Add(g_staticKey);
      parts.AddRange(Key(view.PlotX1, errorY - 16, panelKey));
      var clipped = new List<(double X, double Value, bool Top)>();
      if (view.PerFrame)
      {
        double barW = Math.Max(1.0, (section.FrameCount > 0 ? view.PixelsPerFrame : view.PlotX1 - PlotX0) - 0.6);
        foreach (int i in errors.FramesIn(section.Start, section.End))
        {
          double value = data.Frames[i].AnimationError!.Value.TotalMilliseconds;
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
          path.Append($"M{N(column, 0)} {N(top, 1)}h1V{N(bottom, 1)}h-1Z");
        }
        foreach (var (column, frameStart, frameEnd) in view.Columns(section.Start, section.End))
        {
          var (start, end) = errors.Of(frameStart, frameEnd);
          int count = end - start;
          if (count == 0)
            continue;
          double min = NanosecondsMs(errors.Values.KthSmallest(start, end, 0));
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
    private static void StepPanel(
      List<CardShape> parts,
      List<CardPlot> plots,
      PanelView view,
      double refreshMs,
      double stepY,
      bool animation,
      bool frameTimeLines
    )
    {
      // Each frame's hold: from its first sighting to the next frame of its segment (in the section), at the next frame's display time step
      var section = view.Section;
      var data = view.Data;
      var holds = data.Holds;
      int holdEnd = Math.Max(section.Start, section.End - 1);
      var (scaleFrom, scaleTo) = view.WholeRunScales ? (0, data.Frames.Count) : (section.Start, holdEnd);
      // The scale covers the display time steps of the frames that animate (an idle screen's hold would squash them: it gets a mark at the
      // edge with its value), and the animation time steps when they are drawn too; every hold's when static values count (ClampStatic off)
      var holdScale = view.ClampStatic ? data.AnimatingHolds : data.Holds;
      var animationScale = view.ClampStatic ? data.AnimatingAnimationHolds : data.AnimationHolds;
      var scales = new List<(double Longest, double Bulk)>();
      foreach (var sequence in animation ? new[] { holdScale, animationScale } : new[] { holdScale })
      {
        var (scaleStart, scaleEnd) = sequence.Of(scaleFrom, scaleTo);
        if (scaleEnd > scaleStart)
          scales.Add((MaxMs(sequence.Values, scaleStart, scaleEnd), sequence.Values.PercentileMs(scaleStart, scaleEnd, ChartScale.BulkPercentile)));
      }
      // And the reference lines of the holds that animate
      double referenceLongest = 0;
      if (frameTimeLines)
      {
        var references = view.ClampStatic ? data.AnimatingStepReferences : data.AllStepReferences;
        var (referenceStart, referenceEnd) = references.Of(scaleFrom, scaleTo);
        if (referenceEnd > referenceStart)
          referenceLongest = MaxMs(references.Values, referenceStart, referenceEnd);
      }
      double top =
        scales.Count > 0
          ? ChartScale.StepTop(Math.Max(scales.Max(s => s.Longest), referenceLongest), scales.Max(s => s.Bulk), refreshMs)
          : ChartScale.StepTop(referenceLongest > 0 ? new[] { referenceLongest } : Array.Empty<double>(), refreshMs);
      // An animation time step can be 0 or less (the animation clock stood still or went back): drawn at 0
      double YOf(double ms) => stepY + StepH - (Math.Clamp(ms, 0, top) / top * StepH);
      double X1(int frame) => Math.Min(view.EndX, view.XOfFrame(frame + 1));
      plots.Add(new CardPlot(ReportItem.DisplayTimeStep, PlotX0, stepY, view.PlotX1, stepY + StepH, view.ViewFrom, view.ViewTo, 0, top));

      parts.Add(
        new TextShape(
          20,
          stepY - 16,
          animation ? "DISPLAY TIME STEP AND ANIMATION TIME STEP" : "DISPLAY TIME STEP: HOW LONG EACH FRAME STAYED ON SCREEN",
          "label",
          "start"
        )
      );
      // The key: as planned, and every other kind of hold the section has; the animation time step when drawn
      var panelKey = new List<(string[] Classes, string Text, bool Line)> { (new[] { "held" }, "as planned", true) };
      foreach (var (kind, text) in g_holdKeys)
      {
        var (kindStart, kindEnd) = data.HoldsOf(kind).Of(section.Start, holdEnd);
        if (kindEnd > kindStart)
          panelKey.Add((new[] { HoldClass("held", kind) }, text, true));
      }
      if (animation)
        panelKey.Add((new[] { "step-line" }, "animation time step", true));
      foreach (double position in ChartScale.StepTicks(refreshMs, top).Where(t => t > 0))
      {
        double y = YOf(position);
        parts.Add(GridLine(y, view.PlotX1));
        parts.Add(new TextShape(PlotX0 - 10, y + 4, $"{Ms(Math.Round(position, 1))} ms", "vsync-n", "end"));
      }
      parts.Add(GridLine(YOf(0), view.PlotX1));
      parts.Add(new TextShape(PlotX0 - 10, YOf(0) + 4, "0", "vsync-n", "end"));
      if (StaticBands(parts, view, stepY, stepY + StepH, label: false))
        panelKey.Add(g_staticKey);
      // The reference lines under the holds; the key names them when they are drawn
      var clipped = new List<(double X, double Value, bool Top)>();
      var lines = new List<CardShape>();
      if (frameTimeLines)
        ReferenceKey(panelKey, ReferenceLines(lines, view, data.StepReferences, holdEnd, YOf, top, stepY, stepY + StepH, clipped));
      parts.AddRange(Key(view.PlotX1, stepY - 16, panelKey));
      parts.AddRange(lines);
      if (animation)
        AnimationSteps(parts, view, holdEnd, YOf, X1, stepY);

      if (view.PerFrame)
      {
        var risers = new StringBuilder();
        var byKind = Enum.GetValues<HoldKind>().ToDictionary(kind => kind, _ => new StringBuilder());
        (double X1, double Level)? previous = null;
        foreach (int i in holds.FramesIn(section.Start, holdEnd))
        {
          var next = data.Frames[i + 1];
          double level = next.DisplayDelta!.Value.TotalMilliseconds;
          double x0 = view.XOfFrame(i);
          double x1 = X1(i);
          double y = YOf(level);
          if (previous is { } before && Math.Abs(before.X1 - x0) < 1e-6 && Math.Abs(before.Level - level) > 1e-9)
            risers.Append($"M{N(x0, 1)} {N(YOf(before.Level), 1)}V{N(y, 1)}");
          byKind[data.HoldKinds[i]].Append($"M{N(x0, 1)} {N(y, 1)}H{N(x1, 1)}");
          if (level > top)
            clipped.Add(((x0 + x1) / 2, level, true));
          previous = (x1, level);
        }
        view.MovePath(parts, "riser", risers, stepY, stepY + StepH);
        foreach (var kind in Enum.GetValues<HoldKind>())
          view.MovePath(parts, HoldClass("held", kind), byKind[kind], stepY, stepY + StepH);
      }
      else
      {
        // Per pixel column: the range of its holds faint, in the colour of the first kind it holds (red if any was held too long), and its
        // median hold as the solid line in that hold's kind
        var ranges = Enum.GetValues<HoldKind>().ToDictionary(kind => kind, _ => new StringBuilder());
        var medians = Enum.GetValues<HoldKind>().ToDictionary(kind => kind, _ => new StringBuilder());
        var solids = Enum.GetValues<HoldKind>().ToDictionary(kind => kind, _ => new StringBuilder());
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
          double lowest = NanosecondsMs(holds.Values.KthSmallest(start, end, 0));
          double highest = MaxMs(holds.Values, start, end);
          var boxKind = g_holdPriority.FirstOrDefault(kind => data.HoldsOf(kind).Frames.CountIn(frameStart, frameEnd) > 0, HoldKind.AsPlanned);
          string box = $"M{N(key, 0)} {N(YOf(highest) - 1.25, 1)}H{N(columnEnd, 1)}V{N(YOf(lowest) + 1.25, 1)}H{N(key, 0)}Z";
          if (highest > top)
            clipped.Add((key + 0.5, highest, true));
          if (count < MinFramesForTypical)
          {
            // Few holds: all of them solid
            solids[boxKind].Append(box);
            continue;
          }
          ranges[boxKind].Append(box);
          // A hold that happened (the lower middle one), not an interpolation between two levels, in the kind of a hold of that level
          long middle = holds.Values.KthSmallest(start, end, (count - 1) / 2);
          var middleKind = g_holdPriority.FirstOrDefault(
            kind =>
            {
              var sequence = data.HoldsOf(kind);
              var (kindStart, kindEnd) = sequence.Of(frameStart, frameEnd);
              return kindEnd > kindStart
                && sequence.Values.CountBelow(kindStart, kindEnd, middle + 1) > sequence.Values.CountBelow(kindStart, kindEnd, middle);
            },
            HoldKind.AsPlanned
          );
          medians[middleKind].Append($"M{N(key, 0)} {N(YOf(NanosecondsMs(middle)), 1)}H{N(columnEnd, 1)}");
        }
        foreach (var kind in Enum.GetValues<HoldKind>())
        {
          view.MovePath(parts, HoldClass("held-range", kind), ranges[kind], stepY, stepY + StepH);
          view.MovePath(parts, HoldClass("held", kind), medians[kind], stepY, stepY + StepH);
          view.MovePath(parts, HoldClass("held-fill", kind), solids[kind], stepY, stepY + StepH);
        }
      }
      ClipMarks(shape => view.Move(parts, shape, stepY, stepY + StepH), clipped, stepY, stepY + StepH, v => $"{Ms(v)} ms", view.PlotX1);
      view.Ticks(parts, stepY + StepH);
    }

    /// <summary>
    /// The target and preferred frame time as dashed stepped lines over the holds <paramref name="stretches"/> cover in the section (up to
    /// <paramref name="holdEnd"/>): the target where there is one (not on demand), the preferred frame time only where it differs from the
    /// target (the pacer runs slower than the application wants). A line above the scale stays at its edge with a mark. Returns which lines
    /// were drawn, for the key.
    /// </summary>
    private static (bool Target, bool Preferred) ReferenceLines(
      List<CardShape> parts,
      PanelView view,
      IReadOnlyList<ReferenceStretch> stretches,
      int holdEnd,
      Func<double, double> yOf,
      double top,
      double panelTop,
      double panelBottom,
      List<(double X, double Value, bool Top)> clipped
    )
    {
      var section = view.Section;
      var target = new StringBuilder();
      var preferred = new StringBuilder();
      double? targetEnd = null;
      double? preferredEnd = null;
      void Add(StringBuilder path, ref double? end, double x0, double x1, NanosecondTimeSpan frameTime)
      {
        double ms = frameTime.TotalMilliseconds;
        double y = yOf(ms);
        // Joined to the stretch before when it ends where this one starts
        path.Append(end is { } before && Math.Abs(before - x0) < 1e-6 ? $"V{N(y, 1)}H{N(x1, 1)}" : $"M{N(x0, 1)} {N(y, 1)}H{N(x1, 1)}");
        end = x1;
        if (ms > top)
          clipped.Add(((x0 + x1) / 2, ms, true));
      }
      int first = RunChartData.FirstWhere(0, stretches.Count, k => stretches[k].End > section.Start);
      for (int k = first; k < stretches.Count && stretches[k].Start < holdEnd; ++k)
      {
        var stretch = stretches[k];
        double x0 = view.XOfFrame(Math.Max(stretch.Start, section.Start));
        double x1 = Math.Min(view.EndX, view.XOfFrame(Math.Min(stretch.End, holdEnd)));
        if (x1 <= x0)
          continue;
        if (stretch.TargetFrameTime is { } targetTime)
          Add(target, ref targetEnd, x0, x1, targetTime);
        else
          targetEnd = null;
        if (stretch.PreferredFrameTime is { } preferredTime && preferredTime != stretch.TargetFrameTime)
          Add(preferred, ref preferredEnd, x0, x1, preferredTime);
        else
          preferredEnd = null;
      }
      view.MovePath(parts, "ref-target", target, panelTop, panelBottom);
      view.MovePath(parts, "ref-preferred", preferred, panelTop, panelBottom);
      return (target.Length > 0, preferred.Length > 0);
    }

    /// <summary>The key entries of the reference lines drawn.</summary>
    private static void ReferenceKey(List<(string[] Classes, string Text, bool Line)> panelKey, (bool Target, bool Preferred) drawn)
    {
      if (drawn.Target)
        panelKey.Add((new[] { "ref-target" }, "target", true));
      if (drawn.Preferred)
        panelKey.Add((new[] { "ref-preferred" }, "preferred", true));
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
          double y = yOf(view.Data.Frames[i + 1].AnimationDelta!.Value.TotalMilliseconds);
          // Joined to the hold before when it ends where this one starts (a gap in the segment starts a new line)
          line.Append(previousX1 is { } before && Math.Abs(before - x0) < 1e-6 ? $"V{N(y, 1)}H{N(x1, 1)}" : $"M{N(x0, 1)} {N(y, 1)}H{N(x1, 1)}");
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
        double lowest = NanosecondsMs(steps.Values.KthSmallest(start, end, 0));
        double highest = MaxMs(steps.Values, start, end);
        range.Append($"M{N(key, 0)} {N(yOf(highest) - 0.6, 1)}H{N(columnEnd, 1)}V{N(yOf(lowest) + 0.6, 1)}H{N(key, 0)}Z");
        double middle = NanosecondsMs(steps.Values.KthSmallest(start, end, (count - 1) / 2));
        line.Append($"M{N(key, 0)} {N(yOf(middle), 1)}H{N(columnEnd, 1)}");
      }
      view.MovePath(parts, "step-range", range, stepY, stepY + StepH);
      view.MovePath(parts, "step-line", line, stepY, stepY + StepH);
    }

    /// <summary>
    /// The application side, from the markers, on the display time step's whole-refresh grid: each frame's frametime (from its CPU start to the
    /// next frame's) as a step, and its CPU busy (from its CPU start until it was presented) as a faint bar, held from the frame's display time
    /// to the next frame's.
    /// </summary>
    private static void FrameTimePanel(
      List<CardShape> parts,
      List<CardPlot> plots,
      PanelView view,
      double refreshMs,
      double frameTimeY,
      bool frameTimeLines
    )
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
      var panelKey = new List<(string[] Classes, string Text, bool Line)>
      {
        (new[] { "frametime" }, "frametime: CPU start to the next", true),
        (new[] { "cpu-busy" }, "CPU busy: until presented", false),
      };
      var frameTimes = data.FrameTimes;
      var cpuBusy = data.CpuBusy;
      // The scale leaves a static frame's frametime and CPU busy (an idle wait) out: they get a mark at the edge with their value. Unless
      // static values count
      var frameTimeScale = view.ClampStatic ? data.AnimatingFrameTimes : data.FrameTimes;
      var cpuBusyScale = view.ClampStatic ? data.AnimatingCpuBusy : cpuBusy;
      int Combined(int frame) => frameTimeScale.Frames.Rank(frame) + cpuBusyScale.Frames.Rank(frame);
      var (scaleFrom, scaleTo) = view.ScaleFrames;
      int scaleStart = Combined(scaleFrom);
      int scaleEnd = Combined(scaleTo);
      var both = view.ClampStatic ? data.FrameTimesAndCpuBusy : data.AllFrameTimesAndCpuBusy;
      // And the reference lines of the frames that animate
      double referenceLongest = 0;
      if (frameTimeLines)
      {
        var references = view.ClampStatic ? data.AnimatingFrameTimeReferences : data.AllFrameTimeReferences;
        var (referenceStart, referenceEnd) = references.Of(scaleFrom, scaleTo);
        if (referenceEnd > referenceStart)
          referenceLongest = MaxMs(references.Values, referenceStart, referenceEnd);
      }
      double top =
        scaleEnd > scaleStart
          ? ChartScale.StepTop(
            Math.Max(MaxMs(both, scaleStart, scaleEnd), referenceLongest),
            both.PercentileMs(scaleStart, scaleEnd, ChartScale.BulkPercentile),
            refreshMs
          )
          : ChartScale.StepTop(referenceLongest > 0 ? new[] { referenceLongest } : Array.Empty<double>(), refreshMs);
      double YOf(double ms) => frameTimeY + FrameTimeH - (Math.Min(ms, top) / top * FrameTimeH);
      // A span ends at the next frame of its segment in the section, else after the frame's time on screen
      double X1(int i) =>
        Math.Min(
          view.EndX,
          i + 1 < section.End && frames[i + 1].Segment == frames[i].Segment
            ? view.XOfFrame(i + 1)
            : view.XOf(data.Seconds(i) + frames[i].OnScreen.TotalSeconds)
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
      if (StaticBands(parts, view, frameTimeY, frameTimeY + FrameTimeH, label: false))
        panelKey.Add(g_staticKey);
      var clipped = new List<(double X, double Value, bool Top)>();
      var lines = new List<CardShape>();
      if (frameTimeLines)
      {
        int spanEnd = Math.Max(section.Start, section.End - 1);
        ReferenceKey(
          panelKey,
          ReferenceLines(lines, view, data.FrameTimeReferences, spanEnd, YOf, top, frameTimeY, frameTimeY + FrameTimeH, clipped)
        );
      }
      parts.AddRange(Key(view.PlotX1, frameTimeY - 16, panelKey));
      parts.AddRange(lines);

      var busy = new StringBuilder();
      double bottom = YOf(0);
      if (view.PerFrame)
      {
        var steps = new StringBuilder();
        for (int i = section.Start; i < section.End; ++i)
        {
          if (!data.Spans[i])
            continue;
          double frameTime = frames[i].FrameTime?.TotalMilliseconds ?? 0;
          double cpuBusyMs = frames[i].CpuBusy.Value.TotalMilliseconds;
          double x0 = view.XOfFrame(i);
          double x1 = X1(i);
          if (cpuBusyMs > 0)
            busy.Append($"M{N(x0, 1)} {N(YOf(cpuBusyMs), 1)}H{N(x1, 1)}V{N(bottom, 1)}H{N(x0, 1)}Z");
          if (frameTime > 0)
            steps.Append($"M{N(x0, 1)} {N(YOf(frameTime), 1)}H{N(x1, 1)}");
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
            double middleBusy = NanosecondsMs(cpuBusy.Values.KthSmallest(busyStart, busyEnd, (busyCount - 1) / 2));
            busy.Append($"M{N(key, 0)} {N(YOf(middleBusy), 1)}H{N(end, 1)}V{N(bottom, 1)}H{N(key, 0)}Z");
          }
          double highest = Math.Max(
            levelCount > 0 ? MaxMs(frameTimes.Values, levelStart, levelEnd) : 0,
            busyCount > 0 ? MaxMs(cpuBusy.Values, busyStart, busyEnd) : 0
          );
          if (highest > top)
            clipped.Add((key + 0.5, highest, true));
          if (levelCount == 0)
            continue;
          double lowest = NanosecondsMs(frameTimes.Values.KthSmallest(levelStart, levelEnd, 0));
          double levelMax = MaxMs(frameTimes.Values, levelStart, levelEnd);
          string box = $"M{N(key, 0)} {N(YOf(levelMax) - 1.25, 1)}H{N(end, 1)}V{N(YOf(lowest) + 1.25, 1)}H{N(key, 0)}Z";
          if (levelCount < MinFramesForTypical)
          {
            solid.Append(box);
            continue;
          }
          range.Append(box);
          double middle = NanosecondsMs(frameTimes.Values.KthSmallest(levelStart, levelEnd, (levelCount - 1) / 2));
          median.Append($"M{N(key, 0)} {N(YOf(middle), 1)}H{N(end, 1)}");
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
      string title = $"SHARE OF LATE FRAMES IN THE LAST {LateShare.WindowSeconds:0} S";
      if (pacing == null || data.Frames.Count == 0 || data.LateShare is not { } late)
      {
        parts.Add(new TextShape(20, lateY - 16, title, "label", "start"));
        parts.Add(new TextShape(PlotX0, lateY + (LateH / 2), "no pacing information", "vsync-n", "start"));
        return;
      }
      parts.Add(new TextShape(20, lateY - 16, $"{title} (WHOLE RUN {Percent(pacing.LateShare)})", "label", "start"));
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
      bool staticLate = StaticBands(parts, view, lateY, lateY + LateH, label: false);
      // Red where a frame in the window was late (later than its target: the pacer's intent in the markers, else one refresh), amber
      // where frames were only on screen longer than the application prefers, as the pacer intended, green otherwise; each stretch starts
      // where the previous one ended, so the line is whole
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
          path.Append(last is { } previous ? $"M{N(previous.X, 1)} {N(previous.Y, 1)}L" : "M");
          lastStyle = style;
        }
        else
        {
          path.Append('L');
        }
        path.Append($"{N(x, 1)} {N(y, 1)}");
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
      // The key: the colours the line takes in the section
      var panelKey = new List<(string[] Classes, string Text, bool Line)>();
      foreach (
        var (style, text) in new[]
        {
          ("late-line-none", "none late"),
          ("late-line-adapted", "longer than the application prefers, as the pacer intended"),
          ("late-line", "late: longer than the pacer intended"),
        }
      )
      {
        if (paths[style].Length > 0)
          panelKey.Add((new[] { style }, text, true));
      }
      if (staticLate)
        panelKey.Add(g_staticKey);
      parts.AddRange(Key(view.PlotX1, lateY - 16, panelKey));
      view.Ticks(parts, lateY + LateH);
    }

    /// <summary>
    /// One cell per refresh, from each frame's first capture: a new shade with every frame, late frames red. A capture card sees whole refreshes,
    /// so the refreshes between a frame's last capture and the next frame (captures that could not be decoded) are unknown cells; a camera sees
    /// each frame until the next one. What the frames and the capture missed is in the events panel below.
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
      parts.Add(new TextShape(20, stripY - 16, label + ": ONE CELL PER REFRESH, A NEW SHADE PER FRAME", "label", "start"));
      if (cellW < MinCellPixels)
      {
        double longest = (view.PlotX1 - PlotX0) / MinCellPixels * refreshMs / 1000;
        parts.Add(
          new TextShape(PlotX0, stripY + (StripH / 2) + 4, $"render a section of at most {Ms1(longest)} s to see the refreshes", "vsync-n", "start")
        );
        return;
      }
      plots.Add(new CardPlot(ReportItem.RefreshStrip, PlotX0, stripY, view.PlotX1, stripY + StripH, from, to, 0, 1));
      int legend = parts.Count;
      parts.Add(new TextShape(view.PlotX1, stripY - 16, string.Empty, "vsync-n", "end"));
      // The refresh's milliseconds rounded back to its nanoseconds
      var refresh = new NanosecondTimeSpan((long)Math.Round(refreshMs * NanosecondTimeSpan.NanosecondsPerMillisecond));
      int Cells(NanosecondTimeSpan span) =>
        refresh > NanosecondTimeSpan.Zero ? (int)Math.Max(0, (span.Nanoseconds + (refresh.Nanoseconds / 2)) / refresh.Nanoseconds) : 1;
      bool anyUnknown = false;
      bool anyStatic = false;
      bool anyLate = false;
      bool anyOlder = false;
      bool anyDropped = false;
      for (int i = section.Start; i < section.End; ++i)
      {
        var frame = frames[i];
        var onScreen = frame.OnScreen > NanosecondTimeSpan.Zero ? frame.OnScreen : chart.CapturePeriod;
        int cells = Math.Max(1, Cells(onScreen));
        int seen = cells;
        if (!chart.Camera && i + 1 < section.End && frames[i + 1].Segment == frame.Segment)
        {
          var lastShown = frame.LastSeenTime + chart.CapturePeriod;
          var nextSeen = frames[i + 1].FirstSeenTime;
          var shown = (lastShown < nextSeen ? lastShown : nextSeen) - frame.FirstSeenTime;
          seen = Math.Clamp(Cells(shown), 1, cells);
        }
        bool isStatic = (frame.Flags & PresentedFrameFlags.StaticAfter) != 0;
        bool isLate = (frame.Flags & PresentedFrameFlags.Late) != 0;
        anyStatic |= isStatic && !isLate;
        anyLate |= isLate;
        string cls =
          (frame.Flags & PresentedFrameFlags.Late) != 0 ? "strip-late"
          : isStatic ? (i % 2 == 0 ? "strip-static-a" : "strip-static-b")
          : i % 2 == 0 ? "strip-a"
          : "strip-b";
        double x0 = view.XOfFrame(i);
        // Frames the target dropped before the next one: the refreshes at the end of this frame's hold where they were due repeat this
        // frame; each dropped frame was due one target frame time (in whole refreshes) after the one before it
        int repeats = 0;
        if (i + 1 < frames.Count && frames[i + 1].Segment == frame.Segment && view.Data.DroppedBeforeFrame[i + 1] is > 0 and var dropped)
        {
          var target = frames[i + 1].TargetFrameTime is { } t && t > NanosecondTimeSpan.Zero ? t : refresh;
          repeats = (int)Math.Min(seen - 1, dropped * Math.Max(1, Cells(target)));
        }
        for (int c = 0; c < cells; ++c)
        {
          double x = x0 + (c * cellW);
          if (x >= view.EndX)
            break;
          // A refresh that showed an older frame out of order (also between two sightings of this frame); after the frame's last
          // sighting, else one the capture did not tell; the refreshes where dropped frames were due repeat this frame
          var at = frame.FirstSeenTime + new NanosecondTimeSpan(c * refresh.Nanoseconds);
          bool older = frame.OlderFrames is { } shown && shown.Any(o => (o.CaptureTime - at).Duration().Nanoseconds * 2 < refresh.Nanoseconds);
          string cell =
            older ? "strip-older"
            : c >= seen ? "neutral"
            : c >= seen - repeats ? "strip-dropped"
            : cls;
          anyDropped |= cell == "strip-dropped";
          anyOlder |= older;
          anyUnknown |= cell == "neutral";
          view.Move(
            parts,
            new RectShape(cell, N(x + 0.5, 1), N(stripY, 0), N(Math.Max(0.5, Math.Min(cellW - 1, view.EndX - x - 0.5)), 1), N(StripH, 0), "2"),
            stripY - 10,
            stripY + StripH
          );
        }
      }
      // The key: the exceptions the section shows
      var panelKey = new List<(string[] Classes, string Text, bool Line)>();
      if (anyLate)
        panelKey.Add((new[] { "strip-late" }, "late", false));
      if (anyStatic)
        panelKey.Add((new[] { "strip-static-a", "strip-static-b" }, "static", false));
      if (anyDropped)
        panelKey.Add((new[] { "strip-dropped" }, "a dropped frame was due (repeat)", false));
      if (anyOlder)
        panelKey.Add((new[] { "strip-older" }, "an older frame (out of order)", false));
      if (anyUnknown)
        panelKey.Add((new[] { "neutral" }, "not decoded or not recorded", false));
      parts.RemoveAt(legend);
      parts.InsertRange(legend, Key(view.PlotX1, stripY - 16, panelKey));
      view.Ticks(parts, stripY + StripH);
    }

    /// <summary>Each event kind's class (its colour in the lanes and the key) and its word in the key.</summary>
    private static readonly Dictionary<RunEventKind, (string Class, string Text)> g_eventKinds = new Dictionary<RunEventKind, (string, string)>
    {
      [RunEventKind.FramesDropped] = ("event-dropped", "dropped"),
      [RunEventKind.OutOfOrder] = ("event-older", "out of order"),
      [RunEventKind.Torn] = ("event-torn", "torn"),
      [RunEventKind.NotRecorded] = ("event-gap", "not recorded"),
      [RunEventKind.SourceDropped] = ("event-gap", "dropped by the source"),
      [RunEventKind.Missed] = ("event-gap", "missed"),
      [RunEventKind.NotDecoded] = ("event-undecoded", "not decoded"),
    };

    /// <summary>The section's time on the capture's clock, for its events: from its start to its end, and the last frame's refresh.</summary>
    private static (NanosecondTickCount From, NanosecondTickCount To) EventTimes(RunSection section) =>
      (section.Data.TimeAt(section.FromSeconds), section.Data.TimeAt(section.ToSeconds) + section.Data.Run.CapturePeriod);

    /// <summary>What the capture missed in the section, by kind ("2 not decoded, 1 missed"); empty when nothing, or the rows are not known.</summary>
    private static string CaptureGaps(RunSection section)
    {
      var events = section.Data.Events;
      var (from, to) = EventTimes(section);
      return string.Join(
        ", ",
        RunEvents
          .CaptureKinds.Select(kind => (Kind: kind, Count: events.Count(kind, from, to)))
          .Where(k => k.Count > 0)
          .Select(k => $"{k.Count.ToString("N0", CultureInfo.InvariantCulture)} {g_eventKinds[k.Kind].Text}")
      );
    }

    /// <summary>
    /// Two lanes of events at every zoom: the frames (dropped by the target, out of order, torn) over the capture (not recorded, dropped by the
    /// source, missed, not decoded). One mark per pixel column and lane, of the kind that outweighs the others there; an event covers its
    /// refresh. The key counts each kind in the section.
    /// </summary>
    private static void EventsPanel(List<CardShape> parts, List<CardPlot> plots, PanelView view, ChartRun chart, double eventsY)
    {
      var events = view.Data.Events;
      var data = view.Data;
      // The period is at least one nanosecond, the smallest step of time
      var period = new NanosecondTimeSpan(Math.Max(1, chart.CapturePeriod.Nanoseconds));
      var smallestStep = new NanosecondTimeSpan(1);
      var (from, to) = EventTimes(view.Section);
      parts.Add(new TextShape(20, eventsY - 16, "EVENTS: WHAT THE FRAMES DID, WHAT THE CAPTURE MISSED", "label", "start"));
      plots.Add(new CardPlot(ReportItem.Events, PlotX0, eventsY, view.PlotX1, eventsY + EventsH, view.ViewFrom, view.ViewTo, 0, 1));

      // The time axis is linear: a pixel column's time on the capture's clock
      double x0 = view.XOf(0);
      double pixelsPerSecond = view.XOf(1) - x0;
      NanosecondTickCount TimeAt(double x) => data.TimeAt((x - x0) / pixelsPerSecond);
      int firstColumn = (int)Math.Floor(Math.Max(view.XOf(view.From), 0));
      int lastColumn = (int)Math.Ceiling(view.EndX);

      var key = new List<(string[] Classes, string Text, bool Line)>();
      foreach (var (lane, kinds, name) in new[] { (0, RunEvents.FrameKinds, "frames"), (1, RunEvents.CaptureKinds, "capture") })
      {
        double y = eventsY + (lane * (LaneH + LaneGap));
        parts.Add(new TextShape(PlotX0 - 8, y + LaneH - 1, name, "vsync-n", "end"));
        parts.Add(new RectShape("event-track", N(PlotX0, 1), N(y, 0), N(view.PlotX1 - PlotX0, 1), N(LaneH, 0), "2"));

        // Runs of columns of one kind are one mark, at least two pixels wide
        string? runClass = null;
        int runStart = 0;
        void EndRun(int end)
        {
          if (runClass == null)
            return;
          double x = Math.Min(runStart, end - 2);
          view.Move(parts, new RectShape(runClass, N(x, 1), N(y, 0), N(Math.Max(2, end - x), 1), N(LaneH, 0), "1"), y, y + LaneH);
          runClass = null;
        }
        for (int column = firstColumn; column < lastColumn; ++column)
        {
          // An event covers its refresh: it shows in every column that refresh reaches
          var columnStart = TimeAt(column) - period + smallestStep;
          var columnEnd = TimeAt(column + 1);
          var start = columnStart > from ? columnStart : from;
          var end = columnEnd < to ? columnEnd : to;
          string? cls = null;
          // Indexed: an enumerator of the list would be a new object per column
          for (int k = 0; k < kinds.Count; ++k)
          {
            if (start < end && events.Any(kinds[k], start, end))
            {
              cls = g_eventKinds[kinds[k]].Class;
              break;
            }
          }
          if (cls != runClass)
          {
            EndRun(column);
            if (cls != null)
            {
              runClass = cls;
              runStart = column;
            }
          }
        }
        EndRun(lastColumn);

        key.Add((Array.Empty<string>(), name + ":", false));
        var counted = kinds.Select(kind => (Kind: kind, Count: events.Count(kind, from, to))).Where(k => k.Count > 0).ToList();
        foreach (var (kind, count) in counted)
          key.Add((new[] { g_eventKinds[kind].Class }, $"{count.ToString("N0", CultureInfo.InvariantCulture)} {g_eventKinds[kind].Text}", false));
        if (counted.Count == 0)
          key.Add(
            (
              Array.Empty<string>(),
              lane == 0 ? "none"
              : events.CapturesKnown ? "nothing missed"
              : "not known",
              false
            )
          );
      }
      parts.AddRange(Key(view.PlotX1, eventsY - 16, key));
      view.Ticks(parts, eventsY + EventsH);
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
        add(new PathShape("clip-mark", $"M{N(x, 1)} {N(edge, 1)}L{N(x - 4, 1)} {N(inside, 1)}H{N(x + 4, 1)}Z"));
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

    private static string Hz(RunPacing? pacing) => RunHeadline.Hz(pacing);
  }
}
