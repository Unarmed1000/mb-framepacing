//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL: the frame stages charts and the trace file of a frame log, against what the script they are a port of gave for the same
//* logs (measure/test-data/frame-logs/expected: made with tools/frame_stages_chart.py before it was removed). The trace file byte for
//* byte; the charts shape for shape: every rectangle, line, text and outline with its class, its place to a tenth of a pixel, and its
//* tooltip, in the script's order.
//*
//* The logs are made up or simulated (the folder's README): no log of a capture session is stored.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using MB.FramePacing.Analysis.UnitTest;
using MB.FramePacing.Charts.Stages;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class FrameStagesTests
  {
    private static string Folder => Path.GetFullPath(Path.Combine(VideoClips.Directory(), "..", "frame-logs"));

    private static string Expected(string name) => File.ReadAllText(Path.Combine(Folder, "expected", name)).ReplaceLineEndings("\n");

    private static (string Name, IReadOnlyList<FrameLogFrame> Frames, IReadOnlyList<string> Notes) Log(string name) =>
      FrameLog.ReadSource(Path.Combine(Folder, name + ".csv"), string.Empty);

    [TestCase("made-up")]
    [TestCase("made-up-row-early")]
    [TestCase("sim-loop")]
    public void TheTraceFile_IsTheScripts_ByteForByte(string name)
    {
      var log = Log(name);

      Assert.That(FrameStagesTrace.Write(log.Name, log.Frames), Is.EqualTo(Expected(name + ".trace.json")));
    }

    [TestCase("made-up", 104, 12, "made-up-rows.svg")]
    [TestCase("made-up", 118, 22, "made-up-late-rows.svg")]
    [TestCase("sim-loop", 120, 12, "sim-loop-rows.svg")]
    public void ARowPerFrame_IsTheScriptsChart_ShapeForShape(string name, int first, int count, string expected)
    {
      var log = Log(name);

      string svg = FrameStagesCard.Render(log.Name, log.Frames, first, count, log.Notes);

      AssertSameChart(svg, Expected(expected));
    }

    [TestCase("made-up", 104, 12, "made-up-lanes.svg")]
    [TestCase("made-up", 118, 22, "made-up-late-lanes.svg")]
    [TestCase("made-up-row-early", 104, 12, "made-up-row-early-lanes.svg")]
    [TestCase("sim-loop", 120, 12, "sim-loop-lanes.svg")]
    public void ALanePerResource_IsTheScriptsChart_ShapeForShape(string name, int first, int count, string expected)
    {
      var log = Log(name);

      string svg = FrameLanesCard.Render(log.Name, log.Frames, first, count, log.Notes);

      AssertSameChart(svg, Expected(expected));
    }

    [Test]
    public void ALog_IsReadAsItsWholeNumbers_AMomentsZeroIsNotKnown()
    {
      var (frames, notes) = FrameLog.Read(
        "frameIndex,frameStartTicks,endFrameTicks,swapInterval,note,gpuWorkBeginTicks\n"
          + "7,1000,1500,2,text,0\r\n"
          + "8,2000,,0,,-5\n"
          + "9,0,2500,1,,3000\n"
          + "short,row\n"
          + ",4000,4500,1,,\n"
      );

      Assert.That(notes, Is.Empty);
      // The row without a frame start (a moment's zero) and the one without a frame index are no frames; nor is a row of another length
      Assert.That(frames.Select(frame => frame.Index), Is.EqualTo(new long[] { 7, 8 }));
      Assert.That(frames[0].Values.Keys, Is.EquivalentTo(new[] { "frameIndex", "frameStartTicks", "endFrameTicks", "swapInterval" }));
      Assert.That(frames[0].Span("frameStartTicks", "endFrameTicks"), Is.EqualTo((1000L, 1500L)));
      Assert.That(frames[0].SwapInterval, Is.EqualTo(2));
      Assert.That(frames[1].At("endFrameTicks"), Is.Null);
      Assert.That(frames[1].At("gpuWorkBeginTicks"), Is.EqualTo(-5));
      Assert.That(frames[1].Span("frameStartTicks", "endFrameTicks"), Is.Null);
      Assert.That(frames[1].Span("frameStartTicks", "gpuWorkBeginTicks"), Is.Null);
      // A swap interval the log does not say, or says as zero, is one
      Assert.That(frames[1].SwapInterval, Is.EqualTo(1));
      Assert.That(frames[0].Shown, Is.Null);
      Assert.That(FrameLog.Read(string.Empty).Frames, Is.Empty);
    }

    [Test]
    public void ALogWithAFramesStartOneRowEarly_IsReadFromTheRowBefore_AndSaysSo()
    {
      var plain = Log("made-up");
      var early = Log("made-up-row-early");

      Assert.That(plain.Notes, Is.Empty);
      Assert.That(early.Notes, Is.EqualTo(new[] { FrameLog.RowEarlyNote }));
      // The first row's own start was in the row before it, which the log does not have
      Assert.That(early.Frames.Select(frame => frame.Index), Is.EqualTo(plain.Frames.Skip(1).Select(frame => frame.Index)));
      Assert.That(early.Frames[0].Start, Is.EqualTo(plain.Frames[1].Start));
      Assert.That(early.Frames[5].At("pacerFrameId"), Is.EqualTo(plain.Frames[6].At("pacerFrameId")));
    }

    [Test]
    public void ARunOfASessionsZip_IsReadByItsName()
    {
      using var temp = new MB.FramePacing.Trace.UnitTest.TempFolder();
      string zip = temp.File("session.zip");
      using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
      {
        archive.CreateEntryFromFile(Path.Combine(Folder, "made-up.csv"), "session/vulkan/run-b.csv");
        archive.CreateEntryFromFile(Path.Combine(Folder, "sim-loop.csv"), "session/gles/run-a.csv");
        archive.CreateEntryFromFile(Path.Combine(Folder, "sim-loop.csv"), "session/gles/run-a.events.csv");
        archive.CreateEntryFromFile(Path.Combine(Folder, "sim-loop.csv"), "session/notes.csv");
      }

      Assert.That(FrameLog.RunsIn(zip), Is.EqualTo(new[] { "gles/run-a", "vulkan/run-b" }));
      var run = FrameLog.ReadSource(zip, "vulkan/run-b");
      Assert.That(run.Name, Is.EqualTo("vulkan/run-b"));
      Assert.That(run.Frames.Count, Is.EqualTo(Log("made-up").Frames.Count));
      Assert.That(
        Assert.Throws<ArgumentException>(() => FrameLog.ReadSource(zip, "vulkan/none"))!.Message,
        Does.Contain("0 runs named 'vulkan/none'")
      );
    }

    [Test]
    public void AStretchTheLogDoesNotHave_IsRefused()
    {
      var log = Log("made-up");

      var error = Assert.Throws<ArgumentException>(() => FrameStagesCard.Build(log.Name, log.Frames, 9000, 12, log.Notes));

      Assert.That(error!.Message, Does.Contain("no frame 9000 or later (its last is 143)"));
    }

    [Test]
    public void TheStagesOfAFrame_AreWhatItsRowSays()
    {
      var frames = Log("made-up").Frames;
      long period = FrameLogStages.RefreshPeriod(frames);
      Assert.That(period, Is.EqualTo(166_667));
      Assert.That(FrameLogStages.Median(new long[] { 5, 1, 3 }), Is.EqualTo(3));
      Assert.That(FrameLogStages.Median(new long[] { 4, 1, 3, 2 }), Is.EqualTo(2.5));
      Assert.That(FrameLogStages.FloorDivide(-1, 10), Is.EqualTo(-1));
      Assert.That(FrameLogStages.FloorDivide(-20, 10), Is.EqualTo(-2));
      Assert.That(FrameLogStages.FloorDivide(19, 10), Is.EqualTo(1));

      // Frame 109 took too long: it was shown a refresh late, so the frame before it stayed a refresh longer than asked
      int late = Enumerable.Range(0, frames.Count).First(i => frames[i].Index == 109);
      var held = FrameLogStages.OnScreen(frames, late - 1, period)!;
      Assert.That(FrameLogStages.HeldLonger(frames[late - 1], held.Shown, held.End, period), Is.True);
      Assert.That(FrameLogStages.AnimationError(frames, late), Is.EqualTo(-period));
      Assert.That(FrameLogStages.AnimationError(frames, 0), Is.Null);

      // Frames 112 and 113 were reported without a display time: who was on screen after 111 is not known
      int before = Enumerable.Range(0, frames.Count).First(i => frames[i].Index == 111);
      var screen = FrameLogStages.OnScreen(frames, before, period)!;
      Assert.That(screen.Unreported.Select(frame => frame.Index), Is.EqualTo(new long[] { 112, 113 }));
      Assert.That(screen.End, Is.EqualTo(screen.Shown + period));
      Assert.That(screen.UnknownEnd, Is.EqualTo(frames[before + 3].Shown));
      Assert.That(FrameLogStages.OnScreen(frames, before + 1, period), Is.Null);
      Assert.That(FrameLogStages.AnimationError(frames, before + 1), Is.Null);
      long lastReported = frames[^1].Index - FrameLog.SkipLast;
      Assert.That(FrameLogStages.NoDisplayTime(frames[before + 1], lastReported), Does.Contain("read at frame 115"));
      Assert.That(FrameLogStages.NoDisplayTime(frames[^1], lastReported), Is.EqualTo("Its result had not come back when the run ended."));
      Assert.That(FrameLogStages.NoDisplayTime(frames.First(f => f.Index == 126), lastReported), Does.StartWith("No display time was asked for"));
      Assert.That(
        FrameLogStages.NoDisplayTime(frames.First(f => f.Index == 128), lastReported),
        Is.EqualTo("The log has no result for this present.")
      );

      // A frame with a wait for a frame slot waits for its start up to that wait
      var slotted = frames.First(frame => frame.Index % 3 == 0);
      Assert.That(FrameLogStages.StartWait(slotted)!.Value.Last, Is.EqualTo(slotted.At("frameSlotWaitBeginTicks")));
      Assert.That(FrameLogStages.IsTimed(frames.First(frame => frame.Index % 4 == 0)), Is.True);
      Assert.That(FrameLogStages.IsTimed(frames.First(frame => frame.Index % 4 == 1)), Is.False);
      Assert.That(FrameLogStages.CpuSpan(slotted)!.Value.First, Is.EqualTo(slotted.At("hostCpuStartTicks")));
      Assert.That(FrameLogStages.AnimationTie(frames)!.Value.Frame.Index, Is.EqualTo(frames[0].Index));
    }

    [Test]
    public void AChartsShapes_CarryTheirTooltips_AndItsOwnPatterns()
    {
      var log = Log("made-up");

      string svg = FrameLanesCard.Render(log.Name, log.Frames, 104, 12, log.Notes);

      Assert.That(svg, Does.Contain("<title>Frame stages: made-up</title>"));
      Assert.That(svg, Does.Contain("<pattern id=\"hatch\"").And.Contain("<pattern id=\"cross5\""));
      Assert.That(svg, Does.Contain(".wait-slot { fill: url(#dots); }").And.Contain(".q5 { fill: url(#cross5); }"));
      Assert.That(svg, Does.Contain("<title>#104 update\n"));
      // A card without a style, a pattern or a tooltip of its own is written as it was
      var plain = new CardDrawing(
        "t",
        10,
        10,
        new CardShape[] { new RectShape("a", SvgMarkup.N(1, 1), SvgMarkup.N(1, 1), SvgMarkup.N(1, 1), SvgMarkup.N(1, 1)) },
        Array.Empty<CardPlot>()
      );
      string written = SvgCardWriter.Write(plain);
      Assert.That(written, Does.Not.Contain("<defs>").And.Not.Contain("<title>t</title>\n<style>\n  text { pointer-events"));
      Assert.That(written, Does.Contain("<rect class=\"a\" x=\"1.0\" y=\"1.0\" width=\"1.0\" height=\"1.0\"/>"));
    }

    /// <summary>The shapes of the two charts are the same, in the same order: each with its class, its numbers, its text and its tooltip.</summary>
    private static void AssertSameChart(string actual, string expected)
    {
      var ours = Shapes(actual);
      var theirs = Shapes(expected);
      for (int i = 0; i < Math.Min(ours.Count, theirs.Count); ++i)
        Assert.That(ours[i], Is.EqualTo(theirs[i]), $"shape {i}");
      Assert.That(ours.Count, Is.EqualTo(theirs.Count));
      // The same size, title and fill patterns
      var a = XDocument.Parse(actual).Root!;
      var b = XDocument.Parse(expected).Root!;
      Assert.That((string?)a.Attribute("width"), Is.EqualTo((string?)b.Attribute("width")));
      Assert.That((string?)a.Attribute("height"), Is.EqualTo((string?)b.Attribute("height")));
      Assert.That((string?)a.Attribute("aria-label"), Is.EqualTo((string?)b.Attribute("aria-label")));
      Assert.That(Patterns(a), Is.EqualTo(Patterns(b)));
      // Every rule of the script's own style is in ours, after the cards' style sheet
      string style = a.Elements().First(e => e.Name.LocalName == "style").Value;
      foreach (
        string rule in b.Elements().First(e => e.Name.LocalName == "style").Value.Split('\n').Where(line => line.Contains('{') && line.Contains('}'))
      )
      {
        if (rule.TrimStart().StartsWith('.') && !SvgMarkup.DiagramStyle.Contains(rule.Trim(), StringComparison.Ordinal) && !IsCardsRule(rule))
          Assert.That(style, Does.Contain(rule.Trim()), rule);
      }
    }

    /// <summary>A rule of the look the script copied from the cards' style sheet, which the cards' own sheet has in its place.</summary>
    private static bool IsCardsRule(string rule) =>
      new[]
      {
        ".card ",
        ".title ",
        ".sub ",
        ".label ",
        ".axis ",
        ".value ",
        ".grid ",
        ".zero-line ",
        ".band ",
        ".green",
        ".blue",
        ".red ",
        ".amber ",
        ".line",
        ".threshold",
        ".mark ",
      }.Any(name => rule.TrimStart().StartsWith(name, StringComparison.Ordinal));

    private static List<string> Patterns(XElement svg) =>
      svg.Descendants().Where(e => e.Name.LocalName == "pattern").Select(e => e.ToString(SaveOptions.DisableFormatting)).ToList();

    /// <summary>A chart's shapes as texts that are equal when the shapes are: the card's own frame and the fill patterns left out.</summary>
    private static List<string> Shapes(string svg)
    {
      var shapes = new List<string>();
      foreach (var element in XDocument.Parse(svg).Root!.Elements())
      {
        string kind = element.Name.LocalName;
        string cls = (string?)element.Attribute("class") ?? string.Empty;
        string tip = element.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? string.Empty;
        string Number(string name) =>
          double.Parse((string)element.Attribute(name)!, CultureInfo.InvariantCulture).ToString("0.0", CultureInfo.InvariantCulture);
        switch (kind)
        {
          case "rect" when cls == "card":
            break;
          case "rect":
            shapes.Add($"rect {cls} {Number("x")} {Number("y")} {Number("width")} {Number("height")} rx={(string?)element.Attribute("rx")} | {tip}");
            break;
          case "line":
            shapes.Add($"line {cls} {Number("x1")} {Number("y1")} {Number("x2")} {Number("y2")} | {tip}");
            break;
          case "text":
            shapes.Add($"text {cls} {Number("x")} {Number("y")} {(string?)element.Attribute("text-anchor") ?? "start"} | {element.Value}");
            break;
          case "polygon":
          case "polyline":
            shapes.Add($"outline {cls} {(kind == "polygon" ? "closed" : "open")} {Points((string)element.Attribute("points")!)} | {tip}");
            break;
          case "path":
          {
            string data = (string)element.Attribute("d")!;
            bool closed = data.EndsWith(" Z", StringComparison.Ordinal);
            string points = data.TrimStart('M')
              .Replace(" Z", string.Empty, StringComparison.Ordinal)
              .Replace(" L", ";", StringComparison.Ordinal)
              .Replace(' ', ',')
              .Replace(';', ' ');
            shapes.Add($"outline {cls} {(closed ? "closed" : "open")} {Points(points)} | {tip}");
            break;
          }
          default:
            break;
        }
      }
      return shapes;
    }

    private static string Points(string points) =>
      string.Join(
        ' ',
        points
          .Split(' ', StringSplitOptions.RemoveEmptyEntries)
          .Select(point =>
            string.Join(
              ',',
              point.Split(',').Select(n => double.Parse(n, CultureInfo.InvariantCulture).ToString("0.0", CultureInfo.InvariantCulture))
            )
          )
      );
  }
}
