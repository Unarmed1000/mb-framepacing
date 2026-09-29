//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The SVG report: its helpers give exactly what mb-framepacing-explained's Python gives, an hour of frames draws per pixel column and stays
//* small, a section of it draws every frame, and the PNG comes out of a headless browser at twice the size (skipped without one).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MB.FramePacing.Analysis;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class ReportSvgTests
  {
    private const long Refresh = TimeSpan.TicksPerSecond / 240;
    private const int HitchFrame = 500_000;

    /// <summary>Printed by generate_diagrams.py's ms() and text() and Python's number formatting (half to even, a signed zero).</summary>
    [TestCase(-2.0, true, "−2")]
    [TestCase(16.666, false, "16.7")]
    [TestCase(0.04, false, "0")]
    [TestCase(2.25, true, "+2.2")]
    [TestCase(0.25, false, "0.2")]
    [TestCase(0.15, false, "0.1")]
    [TestCase(-0.0, false, "0")]
    [TestCase(13.8888, false, "13.9")]
    [TestCase(1e-12, true, "0")]
    public void Ms_IsThePythonOriginal(double value, bool sign, string expected)
    {
      Assert.That(SvgMarkup.Ms(value, sign), Is.EqualTo(expected));
    }

    [TestCase(0.25, 1, "0.2")]
    [TestCase(0.35, 1, "0.3")]
    [TestCase(2.5, 0, "2")]
    [TestCase(3.5, 0, "4")]
    [TestCase(110.125, 2, "110.12")]
    [TestCase(-0.04, 1, "-0.0")]
    [TestCase(-0.04, 0, "-0")]
    [TestCase(1180.0, 1, "1180.0")]
    [TestCase(9.96, 1, "10.0")]
    public void Fixed_IsPythonsFormatting(double value, int decimals, string expected)
    {
      Assert.That(SvgMarkup.Fixed(value, decimals), Is.EqualTo(expected));
    }

    [Test]
    public void PixelColumns_GroupLikeGroupByAndOrderBy()
    {
      var random = new Random(7);
      foreach (bool ordered in new[] { true, false })
      {
        var xs = Enumerable.Range(0, 500).Select(i => ordered ? i * 0.37 : random.NextDouble() * 40).ToArray();
        var (order, columns) = PixelColumns.Of(xs);
        var expected = xs.Select((x, i) => (x, i)).GroupBy(p => (int)Math.Floor(p.x)).OrderBy(g => g.Key).ToList();
        Assert.That(columns.Select(c => (c.Column, c.Count)), Is.EqualTo(expected.Select(g => (g.Key, g.Count()))), $"ordered {ordered}");
        Assert.That(order, Is.EqualTo(expected.SelectMany(g => g.Select(p => p.i))), $"ordered {ordered}");
        if (ordered)
        {
          // Walking points in x order finds the same columns, galloping over each
          Assert.That(PixelColumns.Walk(0, xs.Length, i => xs[i]).Select(c => (c.Column, c.Start, c.End - c.Start)), Is.EqualTo(columns));
          Assert.That(PixelColumns.Walk(100, 300, i => xs[i]).Sum(c => c.End - c.Start), Is.EqualTo(200), "a part of them");
        }
      }
    }

    [Test]
    public void Build_WritesTheSameSvgAsRender()
    {
      var section = RunSection.Whole(Synthetic(2400));
      var drawing = ReportCard.Build(section);
      Assert.That(drawing.Shapes, Is.Not.Empty);
      Assert.That(SvgCardWriter.Write(drawing, "#fff"), Is.EqualTo(ReportCard.Render(section, background: "#fff")));
      Assert.That(drawing.FlatShapes.OfType<TextShape>().First().Content, Is.EqualTo(drawing.Title), "the title comes first");
    }

    [Test]
    public void Text_IsThePythonOriginal()
    {
      Assert.That(
        SvgMarkup.Text(110.25, 30.05, "A & <b> \"q\"", "title", "start"),
        Is.EqualTo("<text x=\"110.2\" y=\"30.1\" text-anchor=\"start\" class=\"title\">A &amp; &lt;b&gt; \"q\"</text>")
      );
      Assert.That(SvgMarkup.Text(0.35, -0.04, "x"), Is.EqualTo("<text x=\"0.3\" y=\"-0.0\" text-anchor=\"middle\">x</text>"));
    }

    /// <summary>An hour at 240 Hz: every pixel column draws the range of its frames, so the file stays small; the tiles are the whole run's.</summary>
    [Test]
    public void OneHour_DrawsPerPixelColumn_AndStaysSmall()
    {
      var run = OneHour();
      string svg = ReportCard.Render(RunSection.Whole(run));

      Assert.That(svg.Length, Is.LessThan(1_000_000), "an hour of frames in a small file");
      Assert.That(Count(svg, "<rect class=\"bar\""), Is.Zero, "no bar per frame");
      Assert.That(Count(svg, "<path class=\"bar-range\""), Is.EqualTo(1), "one path of per column ranges");
      Assert.That(Count(svg, "<path class=\"bar\""), Is.EqualTo(1), "one path of per column middle 90 %");
      Assert.That(Count(svg, "<path class=\"held-range-late\""), Is.EqualTo(1));
      Assert.That(Count(svg, "<path class=\"held\""), Is.EqualTo(1), "the median holds: on time");
      Assert.That(svg, Does.Contain("render a section of at most"), "no refresh strip for an hour");
      Assert.That(svg, Does.Contain("class=\"clip-mark\""), "the 700 ms hitch is beyond the scale, marked at the edge");
      Assert.That(svg, Does.Contain(">−700 ms<"), "... with its value");
      foreach (var tile in RunHeadline.Tiles(run))
        Assert.That(svg, Does.Contain(">" + SvgMarkup.Escape(tile.Value) + "<"), tile.Caption);
      Assert.DoesNotThrow(() => System.Xml.Linq.XDocument.Parse(svg), "well formed");
    }

    /// <summary>Two seconds of the hour (fewer frames than pixels, refreshes of 2 px): every frame drawn, the refresh strip too, and the tiles
    /// counted over the section only.</summary>
    [Test]
    public void OneHour_Section_DrawsEveryFrame()
    {
      var run = OneHour();
      double hitch = run.Run.Frames[HitchFrame].FirstSeenTicks / (double)TimeSpan.TicksPerSecond;
      var section = RunSection.Create(run, hitch - 1, hitch + 1);
      string svg = ReportCard.Render(section);

      var frames = section.Section.Run.Frames;
      Assert.That(frames, Has.Count.LessThan(1000));
      int withError = frames.Count(f => f.AnimationErrorTicks is { } e && e != 0);
      Assert.That(Count(svg, "<rect class=\"bar\""), Is.EqualTo(withError), "a bar per frame with an error");
      Assert.That(svg, Does.Not.Contain("render a section of at most"));
      Assert.That(Count(svg, "<rect class=\"strip-late\""), Is.GreaterThanOrEqualTo(frames.Count(f => (f.Flags & PresentedFrameFlags.Late) != 0)));
      Assert.That(section.Section.Run.Counts.PresentedFrames, Is.EqualTo(frames.Count));
      Assert.That(section.Section.Run.Pacing!.LateFrames, Is.EqualTo(frames.Count(f => (f.Flags & PresentedFrameFlags.Late) != 0)));
      Assert.That(
        svg,
        Does.Contain(" " + frames.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " frames<"),
        "the section's frames, under the average fps"
      );
      Assert.DoesNotThrow(() => System.Xml.Linq.XDocument.Parse(svg), "well formed");
    }

    /// <summary>The PNG is the SVG drawn by a headless Edge or Chrome at twice its size; skipped where there is none.</summary>
    [Test]
    public void Png_IsTwiceTheSize()
    {
      if (HeadlessBrowser.Find() is not { } browser)
      {
        Assert.Ignore("No Edge or Chrome on this machine");
        return;
      }
      var run = OneHour();
      string directory = Path.Combine(Path.GetTempPath(), "mb-framepacing-tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      try
      {
        var files = ReportFiles.Write(run, "run-1", directory, 100, 104, png: true);
        Assert.That(files.Select(Path.GetFileName), Is.EqualTo(new[] { "run-1-report-100s-104s.svg", "run-1-report-100s-104s.png" }));
        var header = new byte[24];
        using (var stream = File.OpenRead(files[1]))
          stream.ReadExactly(header);
        int BigEndian(int offset) => (header[offset] << 24) | (header[offset + 1] << 16) | (header[offset + 2] << 8) | header[offset + 3];
        int svgHeight = int.Parse(Regex.Match(File.ReadAllText(files[0]), "height=\"(\\d+)\"").Groups[1].Value);
        Assert.That((BigEndian(16), BigEndian(20)), Is.EqualTo((2 * ReportCard.Width, 2 * svgHeight)), browser);
      }
      finally
      {
        Directory.Delete(directory, recursive: true);
      }
    }

    /// <summary>
    /// An hour of a 240 Hz game: every 97th frame late (held two refreshes, off by one refresh), every 7th off by half a millisecond either
    /// way, and one 700 ms hitch.
    /// </summary>
    private static ChartRun OneHour() => Synthetic(240 * 3600);

    /// <summary>A run's name replaces its sequence id in the title, and the sequence id moves to the description.</summary>
    [Test]
    public void Title_ShowsTheNameAndKeepsTheSequenceId()
    {
      var run = Synthetic(240 * 2, lateEvery: 0);
      Assert.That(RunHeadline.Title(run.Run), Is.EqualTo("Run 1  'one hour'"));
      Assert.That(RunHeadline.SequenceLine(run.Run), Is.Null);
      var named = run with { Run = run.Run with { Name = "menu scroll" } };
      Assert.That(RunHeadline.Title(named.Run), Is.EqualTo("Run 1  'menu scroll'"));
      string svg = ReportCard.Render(RunSection.Whole(named));
      Assert.That(svg, Does.Contain(">Run 1  'menu scroll'<"));
      Assert.That(svg, Does.Contain(">Sequence id one hour.<"));
    }

    /// <summary>The late share is green while no frame in the window was late, red where one was.</summary>
    [Test]
    public void LateShare_IsGreenWhileNoFrameIsLate()
    {
      string none = ReportCard.Render(RunSection.Whole(Synthetic(240 * 10, lateEvery: 0)));
      Assert.That(none, Does.Contain("class=\"late-line-none\""));
      Assert.That(none, Does.Not.Contain("class=\"late-line\""));

      // One late frame 5 s in: green before it, red for the 2 s window after it, green again
      string one = ReportCard.Render(RunSection.Whole(Synthetic(240 * 10, lateEvery: 1200)));
      Assert.That(Count(one, "class=\"late-line-none\""), Is.EqualTo(1));
      Assert.That(Count(one, "class=\"late-line\""), Is.EqualTo(1));
      var green = Regex.Match(one, "class=\"late-line-none\" d=\"([^\"]*)\"").Groups[1].Value;
      Assert.That(Regex.Matches(green, "M").Count, Is.EqualTo(2), "two green stretches, before and after the late window");
    }

    /// <summary>
    /// Frames on screen longer than one refresh as the pacer intended count in the late share and make it amber; a frame later than its
    /// target makes it red for the window, then amber again; where every frame takes one refresh it is green.
    /// </summary>
    [Test]
    public void LateShare_IsAmberWhenHeldLongerAsIntended_AndRedWhenLate()
    {
      // The application prefers one refresh per frame; from frame 1400 its pacer runs at two
      var (drawing, frames) = LateShareCard(i => Refresh);
      var plot = drawing.Plots.Single();
      double Seconds(int i) => (frames[i].FirstSeenTicks - frames[0].FirstSeenTicks) / (double)TimeSpan.TicksPerSecond;
      (double X, double Y)[] Points(string cls) =>
        drawing
          .FlatShapes.OfType<PathShape>()
          .Where(p => p.Class == cls)
          .SelectMany(p => Regex.Matches(p.Data, "[ML](-?[0-9.]+) (-?[0-9.]+)"))
          .Select(m =>
            (
              plot.ValueX(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)),
              plot.ValueY(double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))
            )
          )
          .ToArray();
      // More frames than pixels: the line is drawn per pixel column, its points at the columns' centres
      double tolerance = 1.5 * (plot.XTo - plot.XFrom) / (plot.Right - plot.Left);
      var green = Points("late-line-none");
      var red = Points("late-line");
      var amber = Points("late-line-adapted");
      Assert.That(green.Max(p => p.X), Is.EqualTo(Seconds(1400)).Within(tolerance), "green while every frame takes one refresh");
      Assert.That(green.Max(p => p.Y), Is.Zero);
      Assert.That(red.Min(p => p.X), Is.EqualTo(Seconds(1800)).Within(tolerance), "red from the frame that missed the raised target");
      Assert.That(red.Max(p => p.X), Is.EqualTo(Seconds(1800) + 2).Within(tolerance + 0.01), "red for the 2 s window");
      Assert.That(
        amber.Where(p => p.X < Seconds(1800)).Max(p => p.Y),
        Is.GreaterThan(99),
        "frames held longer as intended count: the line rises in amber"
      );
      Assert.That(amber.Max(p => p.X), Is.EqualTo(Seconds(2399)).Within(tolerance), "amber again after the late frame's window");
      Assert.That(KeyWords(drawing).Contains("longer than the application prefers, as the pacer intended"), "the key says what amber means");
    }

    /// <summary>
    /// The same frames, but from frame 1400 the application prefers two refreshes per frame (a 30 fps lock): it runs as it wants, so the line
    /// stays green until the late frame, red for its window, then green again.
    /// </summary>
    [Test]
    public void LateShare_IsGreenWhenTheApplicationPrefersTheLowerRate()
    {
      var (drawing, _) = LateShareCard(i => i < 1400 ? Refresh : 2 * Refresh);
      var amber = drawing.FlatShapes.OfType<PathShape>().Where(p => p.Class == "late-line-adapted");
      Assert.That(amber, Is.Empty, "no frame stayed longer than the application prefers without being late");
      Assert.That(drawing.FlatShapes.OfType<PathShape>().Any(p => p.Class == "late-line"), "the late frame is still red");
      Assert.That(drawing.FlatShapes.OfType<PathShape>().Any(p => p.Class == "late-line-none"));
    }

    /// <summary>
    /// The late share card of 2400 frames: one refresh each until frame 1400, then two (the markers target two), frame 1800 three and late.
    /// <paramref name="preferred"/> gives each frame's preferred frame time.
    /// </summary>
    private static (CardDrawing Drawing, List<PresentedFrame> Frames) LateShareCard(Func<int, long> preferred)
    {
      var run = Synthetic(2400, lateEvery: 0);
      var frames = new List<PresentedFrame>();
      long time = 0;
      foreach (var (f, i) in run.Run.Frames.Select((f, i) => (f, i)))
      {
        long display =
          i < 1400 ? Refresh
          : i == 1800 ? 3 * Refresh
          : 2 * Refresh;
        if (i > 0)
          time += display;
        frames.Add(
          f with
          {
            FirstSeenTicks = time,
            LastSeenTicks = time,
            DisplayDeltaTicks = i > 0 ? display : null,
            MarkerTargetFrameTicks = (uint)(i < 1400 ? Refresh : 2 * Refresh),
            PreferredTicks = preferred(i),
            Flags = i == 1800 ? PresentedFrameFlags.Late : PresentedFrameFlags.None,
          }
        );
      }
      var drawing = ReportCard.Build(
        RunSection.Whole(run with { Run = run.Run with { Frames = frames } }),
        ReportOptions.ShowOnly(new[] { ReportItem.LateShare })
      );
      return (drawing, frames);
    }

    internal static ChartRun Synthetic(int Count, int lateEvery = 97)
    {
      var frames = new List<PresentedFrame>(Count);
      long time = 0;
      for (int i = 0; i < Count; ++i)
      {
        bool hitch = i == HitchFrame;
        bool late = hitch || (lateEvery > 0 && i > 0 && i % lateEvery == 0);
        long display =
          hitch ? 168 * Refresh
          : late ? 2 * Refresh
          : Refresh;
        long error =
          hitch ? -700 * TimeSpan.TicksPerMillisecond
          : late ? -Refresh
          : i % 7 == 0 ? (i % 14 == 0 ? 5000 : -5000)
          : 0;
        if (i > 0)
          time += display;
        bool first = i == 0;
        frames.Add(
          new PresentedFrame(
            0,
            (ulong)i,
            time,
            i,
            time,
            time,
            1,
            Refresh,
            0,
            first ? null : display,
            first ? null : display + error,
            first ? null : error,
            0,
            late ? PresentedFrameFlags.Late : PresentedFrameFlags.None,
            TargetTicks: first ? null : Refresh
          )
        );
      }
      int lateCount = frames.Count(f => (f.Flags & PresentedFrameFlags.Late) != 0);
      var pacing = new RunPacing(
        Refresh / (double)TimeSpan.TicksPerMillisecond,
        false,
        Refresh / (double)TimeSpan.TicksPerMillisecond,
        PacingSource.NativeRefresh,
        lateCount,
        lateCount / (double)(Count - 1),
        LateShare.Worst(frames, LateShare.WindowTicks),
        lateCount,
        0,
        PacingVerdict.BadPacing
      );
      var analysis = new RunAnalysis(
        1,
        "one hour",
        null,
        true,
        true,
        new RunCounts(Count, Count, 0, 0, 0, 0, Count, 0, 0, 1),
        RunStatistics.From(frames, TimeSpan.TicksPerMillisecond, Refresh),
        frames,
        Array.Empty<string>(),
        Pacing: pacing
      );
      return new ChartRun(analysis, Refresh, TimeSpan.TicksPerMillisecond, false);
    }

    private static int Count(string text, string part) => Regex.Matches(text, Regex.Escape(part)).Count;

    private static double HeightOf(string svg) => double.Parse(Regex.Match(svg, "height=\"(\\d+)\"").Groups[1].Value);

    /// <summary>What each item draws, to find it in the SVG.</summary>
    private static readonly (string Id, string Mark)[] g_marks =
    {
      (ReportItem.Title, "class=\"title\""),
      (ReportItem.Description, "class=\"sub\""),
      (ReportItem.Display, ">DISPLAY<"),
      (ReportItem.AverageFps, ">AVERAGE FPS<"),
      (ReportItem.OnePercentLow, ">1 % LOW<"),
      (ReportItem.PointOnePercentLow, ">0.1 % LOW<"),
      (ReportItem.FramesOff, ">FRAMES VISIBLY OFF<"),
      (ReportItem.ErrorP99, ">ERROR P99<"),
      (ReportItem.ErrorP999, ">ERROR P99.9<"),
      (ReportItem.WorstError, ">WORST ERROR<"),
      (ReportItem.LateFrames, ">LATE FRAMES<"),
      (ReportItem.AnimationError, ">ANIMATION ERROR PER FRAME: + SHOWN TOO SOON, − SHOWN TOO LATE<"),
      (ReportItem.DisplayTimeStep, ">DISPLAY TIME STEP: HOW LONG EACH FRAME STAYED ON SCREEN<"),
      (ReportItem.FrameTime, ">FRAMETIME AND CPU BUSY: THE APPLICATION SIDE, FROM THE MARKERS<"),
      (ReportItem.LateShare, ">SHARE OF LATE FRAMES IN THE LAST 2 S (WHOLE RUN "),
      (ReportItem.RefreshStrip, ">REFRESH STRIP: ONE CELL PER REFRESH, A NEW SHADE PER FRAME<"),
    };

    /// <summary>Hiding an item removes exactly what it draws; the title, description, the tiles and every panel also make the card shorter.</summary>
    [Test]
    public void Options_HidingAnItem_RemovesExactlyIt()
    {
      var section = RunSection.Whole(Synthetic(2400));
      string all = ReportCard.Render(section);
      Assert.That(ReportCard.Render(section, ReportOptions.Default), Is.EqualTo(all), "the default options draw the standard card");
      Assert.That(g_marks.All(m => all.Contains(m.Mark, StringComparison.Ordinal)), "every item is in the full card");
      foreach (var (id, mark) in g_marks.Append((ReportItem.Tiles, ">PRESENTED FRAMES<")))
      {
        string svg = ReportCard.Render(section, ReportOptions.Default.Hide(new[] { id }));
        Assert.DoesNotThrow(() => System.Xml.Linq.XDocument.Parse(svg), id);
        Assert.That(svg, Does.Not.Contain(mark), id + ": gone");
        foreach (var (other, otherMark) in g_marks.Where(m => m.Id != id && !(id == ReportItem.Tiles && ReportItem.TileIds.Contains(m.Id))))
          Assert.That(svg, Does.Contain(otherMark), $"{id}: {other} stays");
        if (!ReportItem.TileIds.Contains(id) && id != ReportItem.Display)
          Assert.That(HeightOf(svg), Is.LessThan(HeightOf(all)), id + ": the card is shorter");
      }
    }

    /// <summary>ShowOnly keeps just the items named: one panel is a card of that panel; one tile is a tiles row of it.</summary>
    [Test]
    public void Options_ShowOnly_KeepsJustThoseItems()
    {
      var section = RunSection.Whole(Synthetic(2400));
      string panel = ReportCard.Render(section, ReportOptions.ShowOnly(new[] { ReportItem.AnimationError }));
      Assert.That(g_marks.Where(m => m.Id != ReportItem.AnimationError).Any(m => panel.Contains(m.Mark, StringComparison.Ordinal)), Is.False);
      Assert.That(panel, Does.Contain(">ANIMATION ERROR PER FRAME: + SHOWN TOO SOON, − SHOWN TOO LATE<"));
      Assert.That(HeightOf(panel), Is.EqualTo(20 + 36 + 150 + 64), "margin and label, the panel, its time axis");

      string tile = ReportCard.Render(section, ReportOptions.ShowOnly(new[] { ReportItem.FramesOff }));
      Assert.That(Count(tile, "class=\"tile\""), Is.EqualTo(1), "one tile");
      Assert.That(tile, Does.Contain(">FRAMES VISIBLY OFF<"));

      string tiles = ReportCard.Render(section, ReportOptions.ShowOnly(new[] { ReportItem.Tiles }));
      Assert.That(Count(tiles, "class=\"tile\""), Is.EqualTo(ReportItem.TileIds.Count), "every tile");
    }

    [Test]
    public void Options_UnknownItem_NamesTheKnownOnes()
    {
      var error = Assert.Throws<ArgumentException>(() => ReportOptions.ParseIds("late-share,frametimes"));
      Assert.That(error!.Message, Does.Contain("frametimes").And.Contain("Known:").And.Contain(ReportItem.RefreshStrip));
    }

    /// <summary>
    /// The animation time step is opt-in: not in the standard card; shown, a blue line on the display time step's holds (one hold per frame
    /// until the next, joined), taking no room, on a scale that covers it; naming it alone keeps its panel; without the panel it is not drawn.
    /// </summary>
    [Test]
    public void Options_AnimationTimeStep_IsAnOptInOverlay()
    {
      // Fewer frames than pixels: every frame drawn
      var run = Synthetic(480);
      var section = RunSection.Whole(run);
      string plain = ReportCard.Render(section);
      Assert.That(plain, Does.Not.Contain("class=\"step-line\""), "not in the standard card");

      var shown = ReportOptions.Default.Show(new[] { ReportItem.AnimationTimeStep });
      var drawing = ReportCard.Build(section, shown);
      string svg = SvgCardWriter.Write(drawing, null);
      Assert.That(svg, Does.Contain(">DISPLAY TIME STEP AND ANIMATION TIME STEP<"));
      Assert.That(HeightOf(svg), Is.EqualTo(HeightOf(plain)), "an overlay takes no room");
      var line = drawing.FlatShapes.OfType<PathShape>().Single(p => p.Class == "step-line");
      Assert.That(line.Data.Count(c => c == 'M'), Is.EqualTo(1), "one line: every hold follows the one before");
      Assert.That(line.Data.Count(c => c == 'H'), Is.EqualTo(run.Run.Frames.Count - 1), "a hold per frame until the next");

      var only = ReportCard.Build(section, ReportOptions.ShowOnly(new[] { ReportItem.AnimationTimeStep }));
      Assert.That(only.Plots.Select(p => p.Id), Is.EqualTo(new[] { ReportItem.DisplayTimeStep }), "naming the overlay keeps its panel");
      Assert.That(only.FlatShapes.OfType<PathShape>().Any(p => p.Class == "step-line"));
      string noPanel = ReportCard.Render(section, shown.Hide(new[] { ReportItem.DisplayTimeStep }));
      Assert.That(noPanel, Does.Not.Contain("class=\"step-line\""), "no panel, no overlay");
      Assert.That(ReportCard.Render(section, shown.Hide(new[] { ReportItem.AnimationTimeStep })), Is.EqualTo(plain), "hidden again");

      // Every 50th frame's animation time step four refreshes long: the scale covers it only with the overlay
      var longSteps = run with
      {
        Run = run.Run with { Frames = run.Run.Frames.Select((f, i) => i % 50 == 25 ? f with { AnimationDeltaTicks = 4 * Refresh } : f).ToList() },
      };
      double Top(ReportOptions options) =>
        ReportCard.Build(RunSection.Whole(longSteps), options).Plots.Single(p => p.Id == ReportItem.DisplayTimeStep).YTo;
      double refreshMs = Refresh / (double)TimeSpan.TicksPerMillisecond;
      Assert.That(Top(ReportOptions.Default), Is.EqualTo(2.5 * refreshMs).Within(1e-9), "two refreshes and a half: the late frames' holds");
      Assert.That(Top(shown), Is.EqualTo(4.5 * refreshMs).Within(1e-9), "four refreshes and a half: the animation time steps too");

      var error = Assert.Throws<ArgumentException>(() => ReportOptions.Default.Show(new[] { ReportItem.FrameTime }));
      Assert.That(error!.Message, Does.Contain(ReportItem.FrameTime).And.Contain(ReportItem.AnimationTimeStep));
    }

    /// <summary>An hour draws the animation time step per pixel column too: its range faint and its middle value as the line.</summary>
    [Test]
    public void Options_AnimationTimeStep_OneHour_DrawsPerPixelColumn()
    {
      string svg = ReportCard.Render(RunSection.Whole(OneHour()), ReportOptions.Default.Show(new[] { ReportItem.AnimationTimeStep }));
      Assert.That(Count(svg, "<path class=\"step-range\""), Is.EqualTo(1));
      Assert.That(Count(svg, "<path class=\"step-line\""), Is.EqualTo(1));
      Assert.That(svg.Length, Is.LessThan(1_000_000), "still a small file");
    }

    /// <summary>
    /// The refresh strip over the first second: a cell per refresh of that second across the plot, its own time axis from 0 to 1 s, and a
    /// label that says so; longer than the section, the standard strip.
    /// </summary>
    [Test]
    public void Options_StripSeconds_DrawsTheFirstSeconds()
    {
      var run = Synthetic(240 * 10, lateEvery: 0);
      var section = RunSection.Whole(run);
      var only = ReportOptions.ShowOnly(new[] { ReportItem.RefreshStrip });
      Assert.That(ReportCard.Render(section, only), Does.Contain("render a section of at most"), "ten seconds at 240 Hz are too many cells");

      var drawing = ReportCard.Build(section, only with { StripSeconds = 1 });
      // A refresh is a whole number of ticks, a little under 1/240 s: the frame that appears just before 1 s starts its cell at the edge
      int shown = run.Run.Frames.Count(f => f.FirstSeenTicks < TimeSpan.TicksPerSecond);
      Assert.That(shown, Is.EqualTo(241));
      Assert.That(drawing.FlatShapes.OfType<RectShape>().Count(), Is.EqualTo(shown), "a cell per refresh of the first second");
      var plot = drawing.Plots.Single();
      Assert.That((plot.XFrom, plot.XTo, plot.Left, plot.Right), Is.EqualTo((0.0, 1.0, ReportCard.PlotX0, ReportCard.PlotX1)));
      var texts = drawing.FlatShapes.OfType<TextShape>().Select(t => t.Content).ToList();
      Assert.That(
        texts,
        Does.Contain("REFRESH STRIP, FIRST 1 S: ONE CELL PER REFRESH, A NEW SHADE PER FRAME").And.Contain("1 s").And.Not.Contain("10 s")
      );

      Assert.That(ReportCard.Render(section, only with { StripSeconds = 20 }), Is.EqualTo(ReportCard.Render(section, only)), "the whole section");
      Assert.Throws<ArgumentOutOfRangeException>(() => _ = ReportOptions.Default with { StripSeconds = 0 });
    }

    /// <summary>A title of the card's own replaces the run's; a section still adds its time range.</summary>
    [Test]
    public void Options_Title_ReplacesTheRunsTitle()
    {
      var run = Synthetic(240 * 10, lateEvery: 0);
      var titled = ReportOptions.Default with { Title = "Naive timer, heavy load" };
      string whole = ReportCard.Render(RunSection.Whole(run), titled);
      Assert.That(whole, Does.Contain(">Naive timer, heavy load<").And.Not.Contain(">Run 1"));
      Assert.That(ReportCard.Render(RunSection.Create(run, 2, 4), titled), Does.Contain(">Naive timer, heavy load, 2.0–4.0 s<"));
    }

    /// <summary>
    /// Hide empty leaves out the tiles without a value (0.1 % low and error p99.9 below 1,000 frames) and the late share of a run without a
    /// late frame; a tile of 0 late frames has a value and stays; a run with late frames keeps the late share.
    /// </summary>
    [Test]
    public void Options_HideEmpty_LeavesOutWhatHasNothingToShow()
    {
      var quiet = RunSection.Whole(Synthetic(480, lateEvery: 0));
      var hideEmpty = ReportOptions.Default with { HideEmpty = true };
      string all = ReportCard.Render(quiet);
      Assert.That(all, Does.Contain(">0.1 % LOW<").And.Contain(">ERROR P99.9<").And.Contain(">SHARE OF LATE FRAMES IN THE LAST 2 S (WHOLE RUN "));
      string svg = ReportCard.Render(quiet, hideEmpty);
      Assert.That(
        svg,
        Does.Not.Contain(">0.1 % LOW<").And.Not.Contain(">ERROR P99.9<").And.Not.Contain(">SHARE OF LATE FRAMES IN THE LAST 2 S (WHOLE RUN ")
      );
      Assert.That(svg, Does.Contain(">1 % LOW<").And.Contain(">ERROR P99<").And.Contain(">LATE FRAMES<"));
      Assert.That(Count(svg, "class=\"tile\""), Is.EqualTo(Count(all, "class=\"tile\"") - 2), "two tiles fewer (the display box stays)");
      Assert.That(HeightOf(svg), Is.LessThan(HeightOf(all)));

      string late = ReportCard.Render(RunSection.Whole(Synthetic(480)), hideEmpty);
      Assert.That(late, Does.Contain(">SHARE OF LATE FRAMES IN THE LAST 2 S (WHOLE RUN "), "late frames: the late share stays");
    }

    /// <summary>Five tiles in a row of five are one row: a tile and a gap lower than in rows of four.</summary>
    [Test]
    public void Options_TilesPerRow_LaysOutTheTiles()
    {
      var section = RunSection.Whole(Synthetic(2400));
      var five = ReportOptions.ShowOnly(
        new[] { ReportItem.AverageFps, ReportItem.OnePercentLow, ReportItem.FramesOff, ReportItem.ErrorP99, ReportItem.WorstError }
      );
      string fourPerRow = ReportCard.Render(section, five);
      string fivePerRow = ReportCard.Render(section, five with { TilesPerRow = 5 });
      Assert.That(Count(fivePerRow, "class=\"tile\""), Is.EqualTo(5));
      Assert.That(HeightOf(fourPerRow) - HeightOf(fivePerRow), Is.EqualTo(64 + 12), "one row less: a tile and the gap");
      Assert.Throws<ArgumentOutOfRangeException>(() => _ = ReportOptions.Default with { TilesPerRow = 0 });
      Assert.Throws<ArgumentOutOfRangeException>(() => _ = ReportOptions.Default with { TilesPerRow = ReportOptions.MaxTilesPerRow + 1 });
    }

    /// <summary>The GUI's style comes from the SVG's sheet: text starts from the 'text' rule, and a later rule of the sheet wins, as in CSS.</summary>
    [Test]
    public void CardStyle_ResolvesTheSheetLikeCss()
    {
      var text = CardStyle.Resolve(string.Empty, text: true);
      Assert.That((text.Fill, text.FontSize, text.TabularNumbers), Is.EqualTo(("#e6edf3", 13.0, true)));
      var warning = CardStyle.Resolve("tile-value warn", text: true);
      Assert.That((warning.Fill, warning.FontSize, warning.FontWeight), Is.EqualTo(("#d29922", 20.0, 600)), ".warn comes later in the sheet");
      var label = CardStyle.Resolve("label", text: true);
      Assert.That((label.FontSize, label.LetterSpacingEm, label.Fill), Is.EqualTo((11.0, 0.08, "#8b949e")));
      var vsync = CardStyle.Resolve("vsync", text: false);
      Assert.That(vsync.StrokeDashArray, Is.EqualTo(new[] { 3.0, 4.0 }));
      Assert.That((vsync.Stroke, vsync.StrokeOpacity, vsync.Fill), Is.EqualTo(("#ffffff", 0.34, (string?)null)));
      var curve = CardStyle.Resolve("curve", text: false);
      Assert.That((curve.Fill, curve.StrokeWidth, curve.RoundJoins), Is.EqualTo(("none", 2.0, true)));
      Assert.That(CardStyle.Resolve("card", text: false).FillOpacity, Is.EqualTo(0.94));
    }

    /// <summary>Every class a card draws with is in the style sheet, so the GUI draws every shape the way the SVG shows it.</summary>
    [Test]
    public void CardStyle_HasEveryClassTheCardsUse()
    {
      var run = Synthetic(240 * 10);
      var section = RunSection.Create(run, 4, 5);
      var overlay = ReportOptions.Default.Show(new[] { ReportItem.AnimationTimeStep });
      var drawings = new List<CardDrawing>
      {
        ReportCard.Build(section),
        ReportCard.Build(section, overlay),
        ReportCard.Build(RunSection.Whole(run), overlay),
        FrameTimelineCard.Build(RunSection.Create(run, 4, 4.1)),
      };
      drawings.AddRange(DistributionCard.All.Select(card => DistributionCard.Build(card.Id, section)));
      var known = CardStyle.Rules.Select(rule => rule.Selector).ToHashSet();
      IEnumerable<string> Classes(IEnumerable<CardShape> shapes) =>
        shapes.SelectMany(shape =>
          shape switch
          {
            RectShape r => new[] { r.Class },
            LineShape l => new[] { l.Class },
            PathShape p => new[] { p.Class },
            TextShape t => new[] { t.Class },
            GroupShape g => Classes(g.Children),
            ScrollShape l => Classes(l.Children),
            _ => Array.Empty<string>(),
          }
        );
      var used = drawings.SelectMany(d => Classes(d.Shapes)).SelectMany(c => c.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToHashSet();
      Assert.That(used, Is.Not.Empty);
      Assert.That(used.Where(c => !known.Contains(c)), Is.Empty, "classes without a rule");
    }

    /// <summary>The hover text: the frame shown at a time (the last one first seen by then), a histogram's bin, a percentile.</summary>
    [Test]
    public void CardHover_DescribesThePointUnderThePointer()
    {
      var run = Synthetic(240 * 10);
      var section = RunSection.Whole(run);
      var hover = new CardHover(section);
      var frames = run.Run.Frames;
      double Seconds(int i) => (frames[i].FirstSeenTicks - frames[0].FirstSeenTicks) / (double)TimeSpan.TicksPerSecond;
      Assert.That(hover.FrameAt(Seconds(100)), Is.SameAs(frames[100]));
      Assert.That(hover.FrameAt((Seconds(100) + Seconds(101)) / 2), Is.SameAs(frames[100]), "until the next frame appears");
      Assert.That(hover.FrameAt(-1), Is.SameAs(frames[0]));

      var drawing = ReportCard.Build(section);
      var error = drawing.Plots.Single(p => p.Id == ReportItem.AnimationError);
      // Frame 97 is late (every 97th): held two refreshes, one refresh off
      string text = hover.Describe(error, Seconds(97), 0)!;
      Assert.That(text, Does.StartWith($"Frame 97 at {Seconds(97).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)} s (late)"));
      Assert.That(text, Does.Contain("display time step 8.33 ms").And.Contain("animation error -4.17 ms"));

      var histogram = DistributionCard.Build(DistributionCard.DisplayTimeStepHistogram, section).Plots.Single();
      var steps = RunHistograms.Create(run.Run).DisplayDeltaMs;
      var tallest = steps.Bins.MaxBy(b => b.Count)!;
      Assert.That(
        hover.Describe(histogram, tallest.CenterMs, 0),
        Is.EqualTo(
          $"display time step {tallest.CenterMs.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)} ms (bin of 0.1 ms): {tallest.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} frames"
        )
      );
      var percentiles = DistributionCard.Build(DistributionCard.ErrorPercentiles, section).Plots.Single();
      Assert.That(hover.Describe(percentiles, 150, 0), Does.StartWith("p100.0: |animation error|"), "clamped to 100");
    }

    /// <summary>
    /// The refresh strip: a capture card's refreshes between a frame's last capture and the next frame (not decoded) are unknown cells, a
    /// camera's frame lasts until the next one; frames with skipped frame indices before them, or torn, get a mark above the strip.
    /// </summary>
    [Test]
    public void Strip_ShowsUnknownRefreshesAndMarksSkippedOrTornFrames()
    {
      var run = Synthetic(40, lateEvery: 0);
      var frames = run
        .Run.Frames.Select(
          (f, i) =>
            i switch
            {
              5 => f with { OnScreenTicks = 3 * Refresh },
              10 => f with { FirstSeenTicks = f.FirstSeenTicks + (2 * Refresh), LastSeenTicks = f.LastSeenTicks + (2 * Refresh), SkippedBefore = 2 },
              12 => f with
              {
                FirstSeenTicks = f.FirstSeenTicks + (2 * Refresh),
                LastSeenTicks = f.LastSeenTicks + (2 * Refresh),
                Flags = PresentedFrameFlags.Torn,
              },
              > 5 => f with { FirstSeenTicks = f.FirstSeenTicks + (2 * Refresh), LastSeenTicks = f.LastSeenTicks + (2 * Refresh) },
              _ => f,
            }
        )
        .ToList();
      var card = run with { Run = run.Run with { Frames = frames } };
      var only = ReportOptions.ShowOnly(new[] { ReportItem.RefreshStrip });

      var drawing = ReportCard.Build(RunSection.Whole(card), only);
      var cells = drawing.FlatShapes.OfType<RectShape>().ToList();
      Assert.That(cells, Has.Count.EqualTo(42), "a cell per refresh: 39 frames of one, one of three");
      Assert.That(cells.Count(c => c.Class == "neutral"), Is.EqualTo(2), "the two refreshes after frame 5's last capture are unknown");
      var marks = drawing.FlatShapes.OfType<PathShape>().Single(p => p.Class == "strip-mark");
      Assert.That(marks.Data.Count(c => c == 'M'), Is.EqualTo(2), "frame 10 (skipped indices before it) and frame 12 (torn)");
      Assert.That(KeyWords(drawing), Does.Contain("not decoded"), "the key names the unknown refreshes");
      Assert.That(
        drawing.FlatShapes.OfType<TextRunsShape>().SelectMany(t => t.Runs).Count(r => r.Class == "key-grey"),
        Is.EqualTo(1),
        "with their swatch in their colour"
      );

      var camera = ReportCard.Build(RunSection.Whole(card with { Camera = true }), only);
      Assert.That(camera.FlatShapes.OfType<RectShape>().Count(c => c.Class == "neutral"), Is.Zero, "a camera sees frame 5 until frame 6");
      Assert.That(camera.FlatShapes.OfType<RectShape>().Count(), Is.EqualTo(42));

      var clean = ReportCard.Build(RunSection.Whole(run), only);
      Assert.That(clean.FlatShapes.OfType<RectShape>().Count(c => c.Class == "neutral"), Is.Zero);
      Assert.That(clean.FlatShapes.OfType<PathShape>().Any(p => p.Class == "strip-mark"), Is.False);
      // A clean strip has nothing to explain: no key
      Assert.That(KeyWords(clean), Is.Empty);
    }

    /// <summary>The words of a card's keys (text runs without a class of their own; swatches have their key colour).</summary>
    private static List<string> KeyWords(CardDrawing drawing) =>
      drawing
        .FlatShapes.OfType<TextRunsShape>()
        .SelectMany(t => t.Runs)
        .Where(r => r.Class.Length == 0)
        .Select(r => r.Text.Trim())
        .Where(w => w.Length > 0)
        .ToList();

    /// <summary>Every panel's plot area maps its time range onto the plot's width: the section's start at the left edge, its end at the right.</summary>
    [Test]
    public void Plots_MapTheSectionOntoThePanels()
    {
      var run = OneHour();
      var section = RunSection.Create(run, 100, 102);
      var drawing = ReportCard.Build(section);
      Assert.That(
        drawing.Plots.Select(p => p.Id),
        Is.EqualTo(new[] { ReportItem.AnimationError, ReportItem.DisplayTimeStep, ReportItem.LateShare, ReportItem.RefreshStrip }),
        "no frametime panel plot: the synthetic markers carry no CPU times"
      );
      foreach (var plot in drawing.Plots)
      {
        Assert.That((plot.Left, plot.Right, plot.XFrom, plot.XTo), Is.EqualTo((ReportCard.PlotX0, ReportCard.PlotX1, 100.0, 102.0)), plot.Id);
        Assert.That(plot.ValueX(plot.PixelX(101.25)), Is.EqualTo(101.25).Within(1e-9), plot.Id);
        Assert.That(plot.ValueY(plot.PixelY(0.5)), Is.EqualTo(0.5).Within(1e-9), plot.Id);
      }
      var error = drawing.Plots[0];
      Assert.That(error.YFrom, Is.EqualTo(-error.YTo), "the error scale is symmetric");
    }

    /// <summary>
    /// The distribution cards of the hour: each writes what its shapes hold, has one plot, and stays small (the drift draws per pixel column);
    /// the histograms have a bar per occupied bin.
    /// </summary>
    [Test]
    public void DistributionCards_OneHour()
    {
      var run = OneHour();
      var section = RunSection.Whole(run);
      var histograms = RunHistograms.Create(run.Run);
      foreach (var (id, _) in DistributionCard.All)
      {
        var drawing = DistributionCard.Build(id, section);
        string svg = DistributionCard.Render(id, section);
        Assert.That(SvgCardWriter.Write(drawing), Is.EqualTo(svg), id);
        Assert.That(drawing.Plots.Single().Id, Is.EqualTo(id));
        Assert.That(svg.Length, Is.LessThan(200_000), id + ": small");
        Assert.That(drawing.Title, Does.StartWith("Run 1  'one hour': "), id);
        Assert.DoesNotThrow(() => System.Xml.Linq.XDocument.Parse(svg), id);
      }
      int Bars(string id) => DistributionCard.Build(id, section).FlatShapes.OfType<RectShape>().Count(r => r.Class == "hist-bar");
      Assert.That(Bars(DistributionCard.ErrorHistogram), Is.EqualTo(histograms.AnimationErrorMs.Bins.Count(b => b.Count > 0)));
      Assert.That(Bars(DistributionCard.DisplayTimeStepHistogram), Is.EqualTo(histograms.DisplayDeltaMs.Bins.Count(b => b.Count > 0)));
      var drift = DistributionCard.Build(DistributionCard.Drift, section).FlatShapes.OfType<PathShape>().Single(p => p.Class == "curve");
      Assert.That(drift.Data.Count(c => c is 'M' or 'L'), Is.LessThanOrEqualTo(2 * (int)(ReportCard.PlotX1 - ReportCard.PlotX0 + 1)), "per column");

      var error = Assert.Throws<ArgumentException>(() => DistributionCard.Build("histogram", section));
      Assert.That(error!.Message, Does.Contain("Known:").And.Contain(DistributionCard.Drift));
    }
  }
}
