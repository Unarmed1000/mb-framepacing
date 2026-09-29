//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The report cards of the test clips in test-data/videos (the GUI draws them, the command line and the GUI write them as SVG): every
//* series each card draws is read back through its shapes and plot areas and compared with the values the clip's manifest gives, point by
//* point: exactly where the shape holds the number, within the SVG's rounding where a path holds it. The x axis is seconds since the run's
//* first frame and the values are milliseconds, both converted from whole ticks the same way. Skipped when ffmpeg is not installed.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MB.FramePacing.Analysis;
using MB.FramePacing.Analysis.UnitTest;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  [Category("ffmpeg")]
  public class ChartVideoClipTests
  {
    private string m_directory = string.Empty;
    private string m_ffmpeg = string.Empty;

    [SetUp]
    public void SetUp()
    {
      m_ffmpeg = VideoClips.FindFfmpegOrIgnore();
      m_directory = Path.Combine(Path.GetTempPath(), "mb-framepacing-tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
      try
      {
        if (Directory.Exists(m_directory))
          Directory.Delete(m_directory, true);
      }
      catch (IOException) { }
    }

    /// <summary>
    /// The Timeline card: the animation error bar of every frame with an error, at its display time and exactly as high (bars too small to
    /// see keep a minimum height, in the right direction), on the symmetric scale with the error threshold's band; every frame's hold on
    /// the display time step panel (until the next frame, at its display time step, held too long when the next frame is late); and the
    /// 2 s late share at every frame. The display time step and frametime scales leave static frames out (their holds and frametimes are
    /// idle waits, drawn at the top edge), and a violet band behind every stretch of static frames, with the key saying so, on every panel.
    /// </summary>
    [TestCaseSource(typeof(VideoClips), nameof(VideoClips.Names))]
    public void TimelineCard_MatchesTheManifest(string clip)
    {
      var (manifest, _, chart) = Analyze(clip);
      var drawing = ReportCard.Build(
        RunSection.Whole(chart),
        ReportOptions.ShowOnly(new[] { ReportItem.AnimationError, ReportItem.DisplayTimeStep, ReportItem.FrameTime, ReportItem.LateShare })
      );
      var measured = Enumerable.Range(1, manifest.FrameCount - 1).ToArray(); // frame 0 follows the previous loop, not in the capture
      // The frames with an animation error: a step from or to a static frame is not judged
      var judged = measured.Where(manifest.IsJudged).ToArray();
      double Seconds(int i) => (manifest.ShownTicks(i) - manifest.ShownTicks(0)) / (double)TimeSpan.TicksPerSecond;
      double Error(int i) => Ms(manifest.AnimationErrorTicks(i)!.Value);

      var error = drawing.Plots.Single(p => p.Id == ReportItem.AnimationError);
      double largest = judged.Max(i => Math.Abs(Error(i)));
      Assert.That((error.YFrom, error.YTo), Is.EqualTo((-Math.Max(2, largest * 1.15), Math.Max(2, largest * 1.15))), $"{clip}: symmetric scale");
      // A refresh line at every whole refresh an error reaches (within a tenth of one), inside the scale
      double refreshMs = chart.Run.Pacing?.RefreshPeriodMs ?? (chart.CapturePeriodTicks / (double)TimeSpan.TicksPerMillisecond);
      var expectedLines = new List<double>();
      foreach (int sign in new[] { 1, -1 })
      {
        double reach = judged.Select(i => sign * Error(i)).Max();
        for (int k = 1; k * refreshMs < error.YTo && k * refreshMs <= reach + (0.1 * refreshMs); ++k)
          expectedLines.Add(sign * k * refreshMs);
      }
      var lines = drawing.FlatShapes.OfType<LineShape>().Where(l => l.Class == "error-refresh").Select(l => error.ValueY(l.Y1.Value)).ToArray();
      if (expectedLines.Count <= 8)
        Assert.That(lines, Is.EqualTo(expectedLines).Within(0.05 * (error.YTo - error.YFrom) / (error.Bottom - error.Top)), $"{clip}: refresh lines");
      var band = drawing.FlatShapes.OfType<RectShape>().Single(r => r.Class == "band");
      Assert.That(error.ValueY(band.Y.Value), Is.EqualTo(1.0).Within(1e-9), $"{clip}: 1 ms threshold band");
      var withError = judged.Where(i => manifest.AnimationErrorTicks(i) != 0).ToArray();
      var bars = drawing.FlatShapes.OfType<RectShape>().Where(r => r.Class == "bar").ToArray();
      Assert.That(bars.Select(b => error.ValueX(b.X.Value)), Is.EqualTo(withError.Select(Seconds)).Within(1e-9), $"{clip}: a bar per error");
      double zeroY = error.PixelY(0);
      for (int k = 0; k < bars.Length; ++k)
      {
        double expected = Error(withError[k]);
        bool up = bars[k].Y.Value < zeroY - 1e-9;
        Assert.That(up, Is.EqualTo(expected > 0), $"{clip}: frame {withError[k]} shown too soon or too late");
        if (Math.Abs(error.PixelY(expected) - zeroY) >= 1)
        {
          double value = error.ValueY(up ? bars[k].Y.Value : bars[k].Y.Value + bars[k].Height.Value);
          Assert.That(value, Is.EqualTo(expected).Within(1e-9), $"{clip}: frame {withError[k]}'s animation error");
        }
      }

      // Frame i is held from frame i - 1's display time until its own, at its display time step, too long when it is late
      var step = drawing.Plots.Single(p => p.Id == ReportItem.DisplayTimeStep);
      double pixelMs = (step.YTo - step.YFrom) / (step.Bottom - step.Top);
      double pixelSeconds = (step.XTo - step.XFrom) / (step.Right - step.Left);
      // The scale covers the display time steps of the frames that animate: a static frame's hold (the next frame's step) is left out
      var animating = measured.Where(manifest.CountsTowardFrameRate).Select(i => Ms(manifest.DisplayStepTicks(i))).ToArray();
      Assert.That(step.YTo, Is.EqualTo(ChartScale.StepTop(animating, refreshMs)).Within(1e-9), $"{clip}: the step scale leaves static holds out");
      var holds = Segments(drawing, "held").Select(h => (h, Late: false)).Concat(Segments(drawing, "held-late").Select(h => (h, Late: true)));
      var drawn = holds.OrderBy(h => h.h.X0).ToArray();
      Assert.That(drawn, Has.Length.EqualTo(measured.Length), $"{clip}: a hold per frame until the next");
      for (int k = 0; k < measured.Length; ++k)
      {
        int i = measured[k];
        var (hold, late) = drawn[k];
        Assert.That(step.ValueX(hold.X0), Is.EqualTo(Seconds(i - 1)).Within(0.051 * pixelSeconds), $"{clip}: hold {i} starts");
        Assert.That(step.ValueX(hold.X1), Is.EqualTo(Seconds(i)).Within(0.051 * pixelSeconds), $"{clip}: hold {i} ends at the next frame");
        Assert.That(
          step.ValueY(hold.Y),
          Is.EqualTo(Math.Min(Ms(manifest.DisplayStepTicks(i)), step.YTo)).Within(0.051 * pixelMs),
          $"{clip}: display time step {i} (at the top edge beyond the scale)"
        );
        Assert.That(late, Is.EqualTo(manifest.IsLate(i)), $"{clip}: hold {i} too long");
      }

      // Every frame's point, once: a stretch in a new colour starts at the point the previous one ended on
      var lateShare = drawing.Plots.Single(p => p.Id == ReportItem.LateShare);
      var points = new[] { "late-line-none", "late-line", "late-line-adapted" }
        .SelectMany(cls => PathPoints(drawing, cls))
        .Distinct()
        .OrderBy(point => point.X)
        .ToArray();
      var shares = ExpectedLateShare(manifest);
      Assert.That(points, Has.Length.EqualTo(manifest.FrameCount), $"{clip}: a late share point per frame");
      double shareTolerance = 0.051 * (lateShare.YTo - lateShare.YFrom) / (lateShare.Bottom - lateShare.Top);
      Assert.That(points.Select(point => lateShare.ValueY(point.Y)), Is.EqualTo(shares).Within(shareTolerance), $"{clip}: late share (%)");

      // The frametime scale: every frametime of a frame that animates (to the next frame index) and every CPU busy; a static frame's
      // frametime is an idle wait
      var frameTime = drawing.Plots.Single(p => p.Id == ReportItem.FrameTime);
      var frameTimeValues = Enumerable
        .Range(0, manifest.FrameCount - 1)
        .Where(i =>
          !manifest.IsStatic(i)
          && manifest.FrameIndex(i + 1) == manifest.FrameIndex(i) + 1
          && manifest.CpuStartTicks(i) != 0
          && manifest.CpuStartTicks(i + 1) != 0
        )
        .Select(i => Ms(manifest.CpuStartTicks(i + 1) - manifest.CpuStartTicks(i)))
        .Concat(Enumerable.Range(0, manifest.FrameCount).Where(i => manifest.CpuBusyTicks(i) != 0).Select(i => Ms(manifest.CpuBusyTicks(i))))
        .ToArray();
      Assert.That(
        frameTime.YTo,
        Is.EqualTo(ChartScale.StepTop(frameTimeValues, refreshMs)).Within(1e-9),
        $"{clip}: the frametime scale leaves static frametimes out"
      );

      // A violet band behind every stretch of static frames: from the first one's display time to the next frame's (bands closer than a
      // pixel merge), on each of the four panels, and the key says what it is
      double bandPixel = (error.XTo - error.XFrom) / (error.Right - error.Left);
      var stretches = new List<(double From, double To)>();
      for (int i = 0; i < manifest.FrameCount; ++i)
      {
        if (!manifest.IsStatic(i))
          continue;
        int end = i + 1;
        while (end < manifest.FrameCount && manifest.IsStatic(end))
          ++end;
        double to = end < manifest.FrameCount ? Seconds(end) : Seconds(end - 1) + (manifest.RefreshesOnScreen(end - 1) * refreshMs / 1000);
        if (stretches.Count > 0 && Seconds(i) - stretches[^1].To < bandPixel)
          stretches[^1] = (stretches[^1].From, to);
        else
          stretches.Add((Seconds(i), to));
        i = end - 1;
      }
      var bands = drawing
        .FlatShapes.OfType<RectShape>()
        .Where(r => r.Class == "static-band" && Math.Abs(r.Y.Value - error.Top) < 0.1)
        .OrderBy(r => r.X.Value)
        .ToArray();
      Assert.That(bands, Has.Length.EqualTo(stretches.Count), $"{clip}: a band per static stretch");
      for (int k = 0; k < bands.Length; ++k)
      {
        Assert.That(error.ValueX(bands[k].X.Value), Is.EqualTo(stretches[k].From).Within(0.06 * bandPixel), $"{clip}: static stretch {k} starts");
        Assert.That(
          error.ValueX(bands[k].X.Value + bands[k].Width.Value),
          Is.EqualTo(Math.Min(stretches[k].To, error.XTo)).Within(0.06 * bandPixel),
          $"{clip}: static stretch {k} ends"
        );
      }
      Assert.That(
        drawing.FlatShapes.OfType<RectShape>().Count(r => r.Class == "static-band"),
        Is.EqualTo(4 * stretches.Count),
        $"{clip}: on every panel"
      );
      Assert.That(
        drawing.FlatShapes.OfType<TextShape>().Count(t => t.Content == "static: nothing animates" && t.Class == "vsync-n"),
        Is.EqualTo(stretches.Count > 0 ? 4 : 0),
        $"{clip}: the key of every panel names the static stretches"
      );
    }

    /// <summary>
    /// The animation time step over the display time step (opt-in): every frame's animation time step on its hold, from the previous frame's
    /// display time until its own, on the panel's scale (which covers the animation time steps too), as one joined line.
    /// </summary>
    [TestCaseSource(typeof(VideoClips), nameof(VideoClips.Names))]
    public void AnimationTimeStep_MatchesTheManifest(string clip)
    {
      var (manifest, _, chart) = Analyze(clip);
      var drawing = ReportCard.Build(RunSection.Whole(chart), ReportOptions.ShowOnly(new[] { ReportItem.AnimationTimeStep }));
      var measured = Enumerable.Range(1, manifest.FrameCount - 1).ToArray(); // frame 0 follows the previous loop, not in the capture
      double Seconds(int i) => (manifest.ShownTicks(i) - manifest.ShownTicks(0)) / (double)TimeSpan.TicksPerSecond;

      var step = drawing.Plots.Single(p => p.Id == ReportItem.DisplayTimeStep);
      double pixelMs = (step.YTo - step.YFrom) / (step.Bottom - step.Top);
      double pixelSeconds = (step.XTo - step.XFrom) / (step.Right - step.Left);
      var drawn = SteppedLine(drawing, "step-line").ToArray();
      Assert.That(drawn, Has.Length.EqualTo(measured.Length), $"{clip}: an animation time step per frame until the next");
      for (int k = 0; k < measured.Length; ++k)
      {
        int i = measured[k];
        var hold = drawn[k];
        double expected = Math.Clamp(Ms(manifest.AnimationStepTicks(i)), 0, step.YTo);
        Assert.That(step.ValueX(hold.X0), Is.EqualTo(Seconds(i - 1)).Within(0.051 * pixelSeconds), $"{clip}: step {i} starts");
        Assert.That(step.ValueX(hold.X1), Is.EqualTo(Seconds(i)).Within(0.051 * pixelSeconds), $"{clip}: step {i} ends at the next frame");
        Assert.That(step.ValueY(hold.Y), Is.EqualTo(expected).Within(0.051 * pixelMs), $"{clip}: animation time step {i}");
      }
    }

    /// <summary>
    /// The distribution cards, read back through their shapes and plot areas: a histogram bar per occupied bin at its centre, log10 of its
    /// count high (shape numbers are exact), the threshold and median lines; the percentile curve at every 0.1 percentile and the drift of
    /// every frame (path points, written with one decimal: within 0.05 px).
    /// </summary>
    [TestCaseSource(typeof(VideoClips), nameof(VideoClips.Names))]
    public void DistributionCards_MatchTheManifest(string clip)
    {
      var (manifest, report, _) = Analyze(clip);
      var section = RunSection.Whole(AnalysisOutput.Read(report.CaptureDirectory).Single().Chart);
      // The frames with an animation error (a step from or to a static frame is not judged); the display time step histogram counts the
      // steps toward the frame rate (all but a static frame's time on screen)
      var measured = Enumerable.Range(1, manifest.FrameCount - 1).Where(manifest.IsJudged).ToArray();
      var counted = Enumerable.Range(1, manifest.FrameCount - 1).Where(manifest.CountsTowardFrameRate).ToArray();

      var errors = DistributionCard.Build(DistributionCard.ErrorHistogram, section);
      AssertCardBars(errors, measured.Select(i => manifest.AnimationErrorTicks(i)!.Value), clip + ": animation error histogram");
      var errorPlot = errors.Plots.Single();
      Assert.That(
        errors.FlatShapes.OfType<LineShape>().Where(l => l.Class == "average-line").Select(l => errorPlot.ValueX(l.X1.Value)),
        Is.EqualTo(new[] { -1.0, 1.0 }).Within(1e-9),
        $"{clip}: ±1 ms threshold"
      );

      var display = DistributionCard.Build(DistributionCard.DisplayTimeStepHistogram, section);
      AssertCardBars(display, counted.Select(manifest.DisplayStepTicks), clip + ": display time step histogram");
      double median = Analysis.Statistics.FromTicks(counted.Select(manifest.DisplayStepTicks)).P50;
      var medianLine = display.FlatShapes.OfType<LineShape>().Single(l => l.Class == "average-line");
      Assert.That(display.Plots.Single().ValueX(medianLine.X1.Value), Is.EqualTo(median).Within(1e-9), $"{clip}: median display time step");

      var percentiles = DistributionCard.Build(DistributionCard.ErrorPercentiles, section);
      var sorted = measured.Select(i => Ms(Math.Abs(manifest.AnimationErrorTicks(i)!.Value))).Order().ToArray();
      AssertCurve(
        percentiles,
        DistributionCard.CurvePercentiles.Select(p => (p, Analysis.Statistics.Percentile(sorted, p / 100))),
        clip + ": |animation error| by percentile"
      );

      var drift = DistributionCard.Build(DistributionCard.Drift, section);
      var all = Enumerable.Range(0, manifest.FrameCount).ToArray();
      AssertCurve(
        drift,
        all.Select(i => ((manifest.ShownTicks(i) - manifest.ShownTicks(0)) / (double)TimeSpan.TicksPerSecond, Ms(manifest.DriftTicks(i)))),
        clip + ": drift"
      );
    }

    /// <summary>The report files: every chart of the run as an SVG card next to the other reports.</summary>
    [Test]
    public void ChartFiles_WriteEveryChart()
    {
      var (_, report, _) = Analyze("60-busy-swappy");
      var files = ChartFiles.Write(report);
      Assert.That(
        files.Select(Path.GetFileName),
        Is.EqualTo(
          new[]
          {
            "run-1-report.svg",
            "run-1-error-histogram.svg",
            "run-1-display-time-step-histogram.svg",
            "run-1-error-percentiles.svg",
            "run-1-drift.svg",
          }
        )
      );
      foreach (var file in files)
      {
        Assert.That(Path.GetDirectoryName(file), Is.EqualTo(report.OutputDirectory), file);
        Assert.DoesNotThrow(() => System.Xml.Linq.XDocument.Load(file), file);
      }
    }

    /// <summary>
    /// The analysis output reads back exactly: every presented frame to the tick, the pacing, statistics and counts, the capture period and the
    /// error threshold.
    /// </summary>
    [TestCaseSource(typeof(VideoClips), nameof(VideoClips.Names))]
    public void AnalysisOutput_ReadsBackWhatTheAnalysisWrote(string clip)
    {
      var (_, report, chart) = Analyze(clip);
      var read = AnalysisOutput.Read(report.CaptureDirectory).Single();
      Assert.That(read.FilePrefix, Is.EqualTo("run-1"));
      var back = read.Chart;
      Assert.That(
        (back.CapturePeriodTicks, back.ErrorThresholdTicks, back.Camera),
        Is.EqualTo((chart.CapturePeriodTicks, chart.ErrorThresholdTicks, chart.Camera))
      );
      Assert.That(back.Run.Frames, Is.EqualTo(chart.Run.Frames), $"{clip}: every frame to the tick");
      Assert.That(back.Run.Pacing, Is.EqualTo(chart.Run.Pacing), $"{clip}: pacing");
      Assert.That(back.Run.Statistics, Is.EqualTo(chart.Run.Statistics), $"{clip}: statistics");
      Assert.That(back.Run.Counts, Is.EqualTo(chart.Run.Counts), $"{clip}: counts");
      Assert.That((back.Run.RunId, back.Run.SequenceId), Is.EqualTo((chart.Run.RunId, chart.Run.SequenceId)));
    }

    /// <summary>
    /// The SVG report of each clip, drawn from what the analysis wrote: a bar per frame with an animation error, a held step per frame (red for
    /// the ones held too long), and the refresh strip's cells, late ones red, as the manifest gives them.
    /// </summary>
    [TestCaseSource(typeof(VideoClips), nameof(VideoClips.Names))]
    public void ReportSvg_MatchesTheManifest(string clip)
    {
      var (manifest, report, _) = Analyze(clip);
      var chart = AnalysisOutput.Read(report.CaptureDirectory).Single().Chart;
      string svg = ReportCard.Render(RunSection.Whole(chart));
      var document = System.Xml.Linq.XDocument.Parse(svg);
      IEnumerable<System.Xml.Linq.XElement> Of(string cls) => document.Descendants().Where(e => (string?)e.Attribute("class") == cls);

      var measured = Enumerable.Range(1, manifest.FrameCount - 1).ToArray();
      Assert.That(
        Of("bar").Count(),
        Is.EqualTo(measured.Count(i => manifest.AnimationErrorTicks(i) is { } e && e != 0)),
        $"{clip}: a bar per frame with an error"
      );
      int Segments(string cls) => Of(cls).Sum(e => ((string)e.Attribute("d")!).Count(c => c == 'M'));
      Assert.That(Segments("held") + Segments("held-late"), Is.EqualTo(measured.Length), $"{clip}: a hold per frame until the next");
      Assert.That(Segments("held-late"), Is.EqualTo(measured.Count(manifest.IsLate)), $"{clip}: held too long when the next frame is late");
      var all = Enumerable.Range(0, manifest.FrameCount).ToArray();
      // A cell per refresh a frame was seen in, red for a late frame, violet for a static one; refreshes after that showed an older frame out
      // of order (grey for now). The key's swatches carry the class "key" too, so they are not counted
      int StaticCells() => Of("strip-static-a").Count() + Of("strip-static-b").Count();
      Assert.That(
        StaticCells(),
        Is.EqualTo(all.Where(i => manifest.IsStatic(i) && !(i > 0 && manifest.IsLate(i))).Sum(i => (int)manifest.SeenRefreshes(i))),
        $"{clip}: a violet cell per refresh of every static frame"
      );
      Assert.That(
        Of("strip-late").Count(),
        Is.EqualTo(all.Where(i => i > 0 && manifest.IsLate(i)).Sum(i => (int)manifest.SeenRefreshes(i))),
        $"{clip}: a red cell per refresh of every late frame"
      );
      Assert.That(
        Of("strip-late").Count() + Of("strip-a").Count() + Of("strip-b").Count() + StaticCells(),
        Is.EqualTo(all.Sum(i => (int)manifest.SeenRefreshes(i))),
        $"{clip}: a cell per refresh a frame was seen in"
      );
      Assert.That(
        Of("strip-late").Count() + Of("strip-a").Count() + Of("strip-b").Count() + StaticCells() + Of("neutral").Count(),
        Is.EqualTo(all.Sum(i => (int)manifest.RefreshesOnScreen(i))),
        $"{clip}: a cell per refresh"
      );
      // The application side: a frametime step per frame whose next frame (the next frame index) carries a CPU start too, a CPU busy bar per
      // frame that has one
      int frameTimes = Enumerable
        .Range(0, manifest.FrameCount - 1)
        .Count(i => manifest.FrameIndex(i + 1) == manifest.FrameIndex(i) + 1 && manifest.CpuStartTicks(i) != 0 && manifest.CpuStartTicks(i + 1) != 0);
      Assert.That(Segments("frametime"), Is.EqualTo(frameTimes), $"{clip}: a frametime step per frame with a frametime");
      Assert.That(Segments("cpu-busy"), Is.EqualTo(all.Count(i => manifest.CpuBusyTicks(i) != 0)), $"{clip}: a CPU busy bar per frame with CPU busy");
    }

    /// <summary>
    /// The frame timeline places the CPU boxes on the capture's clock through the markers' intended display times: in the busy clips a frame
    /// starts when the previous one is shown, so every CPU start lands on the previous frame's display time.
    /// </summary>
    [TestCase("60-busy-full-rate")]
    [TestCase("60-busy-swappy")]
    public void FrameTimeline_BusyFramesStartWhenThePreviousOneIsShown(string clip)
    {
      var (_, report, _) = Analyze(clip);
      var frames = AnalysisOutput.Read(report.CaptureDirectory).Single().Chart.Run.Frames;
      var (offset, bySchedule) = FrameTimelineCard.PacerToCapture(frames);
      Assert.That((offset.HasValue, bySchedule), Is.EqualTo((true, true)), $"{clip}: aligned by the schedule");
      int checkedFrames = 0;
      for (int i = 1; i < frames.Count; ++i)
      {
        if (frames[i].CpuStartTicks == 0)
          continue;
        // Within a tick: the manifest and the capture round 1/60 s to ticks independently
        Assert.That(frames[i].CpuStartTicks + offset!.Value, Is.EqualTo(frames[i - 1].FirstSeenTicks).Within(1), $"{clip}: frame {i}");
        ++checkedFrames;
      }
      Assert.That(checkedFrames, Is.GreaterThan(frames.Count - 3), $"{clip}: nearly every frame has a CPU start");
    }

    /// <summary>The frame timeline card of a busy stretch: a CPU box per frame, a first refresh per frame, and a section too long refused.</summary>
    [Test]
    public void FrameTimeline_DrawsEveryFrameOfTheSection()
    {
      var (_, report, _) = Analyze("60-busy-full-rate");
      var chart = AnalysisOutput.Read(report.CaptureDirectory).Single().Chart;
      var section = RunSection.Create(chart, 1.9, 2.25);
      var frames = section.Section.Run.Frames;
      string svg = FrameTimelineCard.Render(section);
      var document = System.Xml.Linq.XDocument.Parse(svg);
      IEnumerable<System.Xml.Linq.XElement> Of(string cls) => document.Descendants().Where(e => (string?)e.Attribute("class") == cls);
      // The key's box sits at x 20; every other box is a frame's CPU work
      int boxes = Of("box").Count(e => (string?)e.Attribute("x") != "20");
      Assert.That(boxes, Is.EqualTo(frames.Count(f => f.CpuStartTicks != 0 && f.CpuBusyTicks != 0)), "a CPU box per frame with CPU times");
      Assert.That(Of("arrow").Count(), Is.EqualTo(boxes + 1), "a present arrow per box, and the key's");
      // Every frame appears on a bright vsync: the line at the left edge of its first refresh (ok or off cell, 2 px inside it) is not faint
      var brightX = Of("vsync").Select(e => double.Parse((string)e.Attribute("x1")!, System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
      var firstCells = Of("ok").Concat(Of("off")).Where(e => (string?)e.Attribute("height") == "40");
      foreach (var cell in firstCells)
      {
        double x = double.Parse((string)cell.Attribute("x")!, System.Globalization.CultureInfo.InvariantCulture) - 2;
        Assert.That(brightX.Any(b => Math.Abs(b - x) < 0.11), Is.True, $"a frame switch at x {x} sits on a bright vsync");
      }
      // Every box is labelled with its frame, however narrow it is (a narrow one in the smaller font)
      int labels = document.Descendants().Count(e => (string?)e.Attribute("class") is "frame" or "box-time" && e.Value.StartsWith('#'));
      Assert.That(labels, Is.EqualTo(boxes), "a frame label per CPU box");
      // Every frame's first refresh is ok or off (the key adds one swatch of each kind it uses)
      int firstRefreshes = Of("ok").Count() + Of("off").Count() - (Of("ok").Any() ? 1 : 0) - (Of("off").Any() ? 1 : 0);
      Assert.That(firstRefreshes, Is.EqualTo(frames.Count), "a first refresh per frame");
      Assert.That(
        Of("err-pill").Count(),
        Is.EqualTo(frames.Count(f => f.AnimationErrorTicks is { } e && Math.Abs(e) > chart.ErrorThresholdTicks)),
        "an error pill per frame off by more than the threshold"
      );
      Assert.That(() => FrameTimelineCard.Render(RunSection.Create(chart, 0, 4)), Throws.InvalidOperationException.With.Message.Contains("at most"));
    }

    /// <summary>The headline tiles (the GUI's and the report's) show the run's numbers.</summary>
    [Test]
    public void Headline_ShowsTheRunsNumbers()
    {
      var (manifest, _, chart) = Analyze("60-busy-swappy");
      var tiles = RunHeadline.Tiles(chart);
      Assert.That(
        tiles.Select(t => t.Caption),
        Is.EqualTo(new[] { "Average fps", "1 % low", "0.1 % low", "Frames visibly off", "Error p99", "Error p99.9", "Worst error", "Late frames" })
      );
      var s = chart.Run.Statistics;
      var measured = Enumerable.Range(1, manifest.FrameCount - 1).ToArray();
      // The frame rates cover the steps toward the frame rate (a static frame's time on screen is left out); the clip's refreshes give the
      // fps and the nearest-rank lows
      var judged = measured.Where(manifest.IsJudged).ToArray();
      var steps = measured.Where(manifest.CountsTowardFrameRate).Select(manifest.DisplayStepTicks).OrderBy(t => t).ToArray();
      double averageFps = steps.Length * (double)TimeSpan.TicksPerSecond / steps.Sum();
      Assert.That(tiles.Single(t => t.Caption == "Average fps").Value, Is.EqualTo(averageFps.ToString("0.0", CultureInfo.InvariantCulture)));
      long p99 = steps[(int)Math.Ceiling(0.99 * steps.Length) - 1];
      Assert.That(
        tiles.Single(t => t.Caption == "1 % low").Value,
        Is.EqualTo((TimeSpan.TicksPerSecond / (double)p99).ToString("0.0", CultureInfo.InvariantCulture))
      );
      Assert.That(
        tiles.Single(t => t.Caption == "Error p99").Value,
        Is.EqualTo(s.AbsoluteAnimationErrorMs.P99.ToString("0.0", CultureInfo.InvariantCulture) + " ms")
      );
      var late = tiles.Single(t => t.Caption == "Late frames");
      Assert.That(late.Value, Is.EqualTo(measured.Count(manifest.IsLate).ToString(CultureInfo.InvariantCulture)));
      Assert.That(late.Warning, Is.EqualTo(measured.Any(manifest.IsLate)));
      double worst = judged.Max(i => Math.Abs(Ms(manifest.AnimationErrorTicks(i)!.Value)));
      Assert.That(tiles.Single(t => t.Caption == "Worst error").Value, Is.EqualTo(worst.ToString("0.0", CultureInfo.InvariantCulture) + " ms"));
    }

    /// <summary>
    /// The frame rate numbers leave out the static frames' time on screen, and say so: the average fps tile's detail, the report's
    /// description; a clip without static frames says nothing.
    /// </summary>
    [TestCase("60-naive-5ms-static-rests")]
    [TestCase("60-idle-1fps")]
    [TestCase("60-busy-swappy")]
    public void FrameRates_SayHowManyStaticFramesTheyExclude(string clip)
    {
      var (manifest, _, chart) = Analyze(clip);
      int excluded = Enumerable.Range(1, manifest.FrameCount - 1).Count(i => !manifest.CountsTowardFrameRate(i));
      Assert.That(chart.Run.Statistics.ExcludedStaticFrames, Is.EqualTo(excluded));
      var fps = RunHeadline.Tiles(chart).Single(t => t.Id == ReportItem.AverageFps);
      var texts = ReportCard.Build(RunSection.Whole(chart)).FlatShapes.OfType<TextShape>().Select(t => t.Content).ToList();
      if (excluded > 0)
      {
        Assert.That(fps.Detail, Does.EndWith($" · {excluded} static excluded"));
        Assert.That(texts, Has.One.EqualTo($"Frame rates and display time steps excluding {excluded} static frames: nothing animates in them."));
      }
      else
      {
        Assert.That(fps.Detail, Does.EndWith($" · {manifest.FrameCount} frames"));
        Assert.That(texts, Has.None.Contains("static frame"));
      }
      // A section counts its own
      var half = RunSection.Create(chart, 0, RunSection.Whole(chart).ToSeconds / 2);
      Assert.That(
        half.Section.Run.Statistics.ExcludedStaticFrames,
        Is.EqualTo(half.Section.Run.Frames.Count(f => f.DisplayDeltaTicks.HasValue && (f.Flags & PresentedFrameFlags.StaticBefore) != 0))
      );
    }

    private (ClipManifest Manifest, AnalysisReport Report, ChartRun Chart) Analyze(string clip)
    {
      var manifest = VideoClips.Manifest(clip);
      var report = CaptureAnalyzer.Analyze(VideoClips.Import(clip, m_ffmpeg, Path.Combine(m_directory, "capture")), new AnalysisOptions());
      var run = report.Timeline.Runs.Single();
      Assert.That(run.Frames, Has.Count.EqualTo(manifest.FrameCount), $"{clip}: the manifest's presented frames");
      return (manifest, report, ChartRun.From(report, run));
    }

    /// <summary>The refresh the run is measured with: the capture period, 1/60 s in whole ticks.</summary>
    private static long RefreshTicks(ChartRun chart)
    {
      long refresh = (long)Math.Round(chart.Run.Pacing!.RefreshPeriodMs * TimeSpan.TicksPerMillisecond);
      Assert.That(refresh, Is.AnyOf(166666L, 166667L));
      return refresh;
    }

    /// <summary>
    /// The share (%) of frames late or on screen longer than one refresh among the frames with a display time step first shown in the 2 s
    /// up to each frame.
    /// </summary>
    private static double[] ExpectedLateShare(ClipManifest manifest)
    {
      bool Longer(int frame) =>
        !manifest.IsStatic(frame) && manifest.PreferredRefreshes(frame) is { } preferred && manifest.DisplayStepRefreshes(frame) > preferred;
      var shares = new double[manifest.FrameCount];
      int start = 0;
      for (int i = 0; i < manifest.FrameCount; ++i)
      {
        while (manifest.ShownTicks(i) - manifest.ShownTicks(start) >= LateShare.WindowTicks)
          ++start;
        var window = Enumerable.Range(start, i - start + 1).Where(j => j > 0).ToArray();
        int late = window.Count(j => manifest.IsLate(j) || Longer(j));
        shares[i] = window.Length > 0 ? late / (double)window.Length * 100 : 0;
      }
      return shares;
    }

    /// <summary>A path's horizontal segments ("M x0 y H x1"), as the display time step panel draws its holds.</summary>
    private static IEnumerable<(double X0, double Y, double X1)> Segments(CardDrawing drawing, string cls) =>
      drawing
        .FlatShapes.OfType<PathShape>()
        .Where(p => p.Class == cls)
        .SelectMany(p => Regex.Matches(p.Data, "M(-?[0-9.]+) (-?[0-9.]+)H(-?[0-9.]+)"))
        .Select(m => (Number(m.Groups[1].Value), Number(m.Groups[2].Value), Number(m.Groups[3].Value)));

    /// <summary>A stepped line's holds: "M x0 yHx1", then "VyHx1" for each hold that starts where the one before ended.</summary>
    private static IEnumerable<(double X0, double Y, double X1)> SteppedLine(CardDrawing drawing, string cls)
    {
      foreach (var path in drawing.FlatShapes.OfType<PathShape>().Where(p => p.Class == cls))
      {
        double x = 0;
        foreach (Match m in Regex.Matches(path.Data, "M(-?[0-9.]+) (-?[0-9.]+)H(-?[0-9.]+)|V(-?[0-9.]+)H(-?[0-9.]+)"))
        {
          bool moved = m.Groups[1].Success;
          double x0 = moved ? Number(m.Groups[1].Value) : x;
          double y = Number(m.Groups[moved ? 2 : 4].Value);
          x = Number(m.Groups[moved ? 3 : 5].Value);
          yield return (x0, y, x);
        }
      }
    }

    /// <summary>A path's points (M and L commands).</summary>
    private static IEnumerable<(double X, double Y)> PathPoints(CardDrawing drawing, string cls) =>
      drawing
        .FlatShapes.OfType<PathShape>()
        .Where(p => p.Class == cls)
        .SelectMany(p => Regex.Matches(p.Data, "[ML](-?[0-9.]+) (-?[0-9.]+)"))
        .Select(m => (Number(m.Groups[1].Value), Number(m.Groups[2].Value)));

    private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    /// <summary>
    /// A distribution card's bars of <paramref name="ticks"/>: 0.1 ms bins centred on multiples of 0.1 ms (wider, a multiple, only when the
    /// range needs more than the maximum bin count), each bar's centre at its bin's centre and its top at log10 of its count, read back
    /// through the card's plot area.
    /// </summary>
    private static void AssertCardBars(CardDrawing card, IEnumerable<long> ticks, string what)
    {
      var values = ticks.ToArray();
      long width = Histogram.DefaultBinWidthTicks;
      long Bin(long value) => (long)Math.Floor((value / (double)width) + 0.5);
      long needed = Bin(values.Max()) - Bin(values.Min()) + 1;
      if (needed > Histogram.DefaultMaxBins)
        width *= (needed + Histogram.DefaultMaxBins - 1) / Histogram.DefaultMaxBins;
      double widthMs = width / (double)TimeSpan.TicksPerMillisecond;
      var expected = values.GroupBy(Bin).OrderBy(g => g.Key).Select(g => (Position: g.Key * widthMs, Height: Math.Log10(g.Count()))).ToArray();

      var plot = card.Plots.Single();
      var bars = card.FlatShapes.OfType<RectShape>().Where(r => r.Class == "hist-bar").ToArray();
      Assert.That(
        bars.Select(b => plot.ValueX(b.X.Value + (b.Width.Value / 2))),
        Is.EqualTo(expected.Select(e => e.Position)).Within(1e-9),
        what + ": centres"
      );
      Assert.That(bars.Select(b => plot.ValueY(b.Y.Value)), Is.EqualTo(expected.Select(e => e.Height)).Within(1e-9), what + ": log10 counts");
      Assert.That(bars.Select(b => b.Y.Value + b.Height.Value), Is.All.EqualTo(plot.Bottom).Within(1e-9), what + ": from the bottom");
    }

    /// <summary>A card's curve, point by point: each point where the plot puts the expected value, within the path's 0.05 px rounding.</summary>
    private static void AssertCurve(CardDrawing card, IEnumerable<(double X, double Y)> expected, string what)
    {
      var plot = card.Plots.Single();
      string data = card.FlatShapes.OfType<PathShape>().Single(p => p.Class == "curve").Data;
      var points = Regex
        .Matches(data, "[ML](-?[0-9.]+) (-?[0-9.]+)")
        .Select(m => (X: Number(m.Groups[1].Value), Y: Number(m.Groups[2].Value)))
        .ToArray();
      var want = expected.Select(e => (X: plot.PixelX(e.X), Y: plot.PixelY(e.Y))).ToArray();
      Assert.That(points, Has.Length.EqualTo(want.Length), what + ": a point per value");
      Assert.That(points.Select(p => p.X), Is.EqualTo(want.Select(w => w.X)).Within(0.0501), what + ": x");
      Assert.That(points.Select(p => p.Y), Is.EqualTo(want.Select(w => w.Y)).Within(0.0501), what + ": y");
    }

    private static double Ms(long ticks) => ticks / (double)TimeSpan.TicksPerMillisecond;
  }
}
