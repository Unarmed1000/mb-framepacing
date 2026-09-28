//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The SVG report: its helpers give exactly what mb-framepacing-explained's Python gives, an hour of frames draws per pixel column and stays
//* small, a section of it draws every frame, and the PNG comes out of a headless browser at twice the size (skipped without one).
//*
//* (c) 2026 Mana Battery
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
      }
    }

    [Test]
    public void Build_WritesTheSameSvgAsRender()
    {
      var section = RunSection.Whole(Synthetic(2400));
      var drawing = ReportCard.Build(section);
      Assert.That(drawing.Shapes, Is.Not.Empty);
      Assert.That(SvgCardWriter.Write(drawing, "#fff"), Is.EqualTo(ReportCard.Render(section, background: "#fff")));
      Assert.That(drawing.Shapes.OfType<TextShape>().First().Content, Is.EqualTo(drawing.Title), "the title comes first");
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

    private static ChartRun Synthetic(int Count, int lateEvery = 97)
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
      (ReportItem.AnimationError, ">ANIMATION ERROR PER FRAME<"),
      (ReportItem.DisplayTimeStep, ">DISPLAY TIME STEP: HOW LONG EACH FRAME STAYED ON SCREEN<"),
      (ReportItem.FrameTime, ">FRAMETIME AND CPU BUSY: THE APPLICATION SIDE, FROM THE MARKERS<"),
      (ReportItem.LateShare, ">SHARE OF LATE FRAMES IN THE LAST 2 S<"),
      (ReportItem.RefreshStrip, ">REFRESH STRIP<"),
    };

    /// <summary>Hiding an item removes exactly what it draws; the title, description, the tiles and every panel also make the card shorter.</summary>
    [Test]
    public void Options_HidingAnItem_RemovesExactlyIt()
    {
      var section = RunSection.Whole(Synthetic(2400));
      string all = ReportCard.Render(section);
      Assert.That(ReportCard.Render(section, ReportOptions.Default), Is.EqualTo(all), "the default options draw everything");
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
      Assert.That(panel, Does.Contain(">ANIMATION ERROR PER FRAME<"));
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
  }
}
