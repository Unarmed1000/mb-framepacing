//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The charts of the test clips in test-data/videos: every series each chart draws (the GUI shows them, the command line and the GUI write
//* them as PNG files) is compared with the values the clip's manifest gives, point by point and exactly. The x axis is seconds since the
//* run's first frame and the values are milliseconds, both converted from whole ticks the same way. Skipped when ffmpeg is not installed.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MB.FramePacing.Analysis;
using MB.FramePacing.Analysis.UnitTest;
using NUnit.Framework;
using ScottPlot;
using ScottPlot.Plottables;

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
    /// The Timeline: the animation error bar of every frame and the scale, every frame's hold on the display time chart (until the next frame,
    /// at its display time, held too long when the next frame is late) and every frame's target over it, the 2 s late share at every frame,
    /// and the refresh strip's span, refreshes and late flag of every frame.
    /// </summary>
    [TestCaseSource(typeof(VideoClips), nameof(VideoClips.Names))]
    public void Timeline_MatchesTheManifest(string clip)
    {
      var (manifest, report, chart) = Analyze(clip);
      using var error = new Plot();
      using var displayTime = new Plot();
      using var lateShare = new Plot();
      using var strip = new Plot();
      RunCharts.Timeline(chart, ChartTheme.Light, error, displayTime, lateShare, strip);

      var measured = Enumerable.Range(1, manifest.FrameCount - 1).ToArray(); // frame 0 follows the previous loop, not in the capture
      double Seconds(int i) => (manifest.ShownTicks(i) - manifest.ShownTicks(0)) / (double)TimeSpan.TicksPerSecond;
      long Origin(int i) => manifest.ShownTicks(i) - manifest.ShownTicks(0);
      long refresh = RefreshTicks(chart);

      var bars = error.GetPlottables<AnimationErrorBarsPlottable>().Single();
      Assert.That(bars.TimeTicks, Is.EqualTo(measured.Select(Origin)), $"{clip}: error bar times");
      Assert.That(bars.ErrorTicks, Is.EqualTo(measured.Select(manifest.AnimationErrorTicks)), $"{clip}: animation error");
      double largest = measured.Max(i => Math.Abs(Ms(manifest.AnimationErrorTicks(i))));
      Assert.That(bars.LimitMs, Is.EqualTo(Math.Max(2, largest * 1.15)), $"{clip}: symmetric scale");
      Assert.That(bars.ThresholdTicks, Is.EqualTo(TimeSpan.TicksPerMillisecond), $"{clip}: 1 ms threshold band");
      Assert.That(error.Axes.GetLimits().Bottom, Is.EqualTo(-bars.LimitMs), $"{clip}: centred on zero");
      Assert.That(error.Axes.GetLimits().Top, Is.EqualTo(bars.LimitMs), $"{clip}: centred on zero");

      // Frame i is held until frame i + 1: its display time is the hold's length, and it is held too long when frame i + 1 is late
      var holds = measured.Select(i => i - 1).ToArray();
      var steps = displayTime.GetPlottables<DisplayTimeStepsPlottable>().Single();
      Assert.That(steps.StartTicks, Is.EqualTo(holds.Select(Origin)), $"{clip}: holds start");
      Assert.That(steps.EndTicks, Is.EqualTo(holds.Select(i => Origin(i + 1))), $"{clip}: holds end at the next frame");
      Assert.That(steps.LevelTicks, Is.EqualTo(holds.Select(i => manifest.DisplayTicks(i + 1))), $"{clip}: display time");
      Assert.That(steps.HeldTooLong, Is.EqualTo(holds.Select(i => manifest.IsLate(i + 1))), $"{clip}: held too long");
      Assert.That(steps.RefreshMs, Is.EqualTo(Ms(refresh)), $"{clip}: refresh grid");
      AssertSeries(displayTime, RunCharts.TargetLegend, holds.Select(Seconds), measured.Select(i => Ms(manifest.TargetRefreshes(i) * refresh)), clip);

      var all = Enumerable.Range(0, manifest.FrameCount).ToArray();
      var share = lateShare.GetPlottables<Scatter>().Single();
      Assert.That(share.Data.GetScatterPoints().Select(p => p.X), Is.EqualTo(all.Select(Seconds)), $"{clip}: late share times");
      Assert.That(share.Data.GetScatterPoints().Select(p => p.Y), Is.EqualTo(ExpectedLateShare(manifest)), $"{clip}: late share (%)");

      var cells = strip.GetPlottables<RefreshStripPlottable>().Single();
      long capturePeriod = report.Timeline.CapturePeriodTicks;
      Assert.That(cells.StartTicks, Is.EqualTo(all.Select(Origin)), $"{clip}: strip, first shown");
      Assert.That(
        cells.EndTicks,
        Is.EqualTo(
          all.Select(i =>
            i + 1 < manifest.FrameCount
              ? Math.Min(manifest.VideoFrameTicks(manifest.Refresh[i + 1] - 1) + capturePeriod - manifest.ShownTicks(0), Origin(i + 1))
              : manifest.VideoFrameTicks(manifest.RefreshCount - 1) + capturePeriod - manifest.ShownTicks(0)
          )
        ),
        $"{clip}: strip, until the next frame (or one capture period after the last capture)"
      );
      Assert.That(cells.Cells, Is.EqualTo(all.Select(i => (int)manifest.RefreshesOnScreen(i))), $"{clip}: strip, refreshes per frame");
      Assert.That(
        cells.Flags.Select(f => f.HasFlag(PresentedFrameFlags.Late)),
        Is.EqualTo(all.Select(i => i > 0 && manifest.IsLate(i))),
        $"{clip}: strip, late frames"
      );
    }

    /// <summary>Both histograms: one bar per occupied 0.1 ms bin at its centre, log10 of its count high; the threshold and median lines.</summary>
    [TestCaseSource(typeof(VideoClips), nameof(VideoClips.Names))]
    public void Histograms_MatchTheManifest(string clip)
    {
      var (manifest, _, chart) = Analyze(clip);
      var measured = Enumerable.Range(1, manifest.FrameCount - 1).ToArray();

      using var errors = new Plot();
      RunCharts.ErrorHistogram(chart, ChartTheme.Light, errors);
      AssertBars(errors, measured.Select(manifest.AnimationErrorTicks), clip + ": animation error histogram");
      Assert.That(errors.GetPlottables<VerticalLine>().Select(l => l.X), Is.EquivalentTo(new[] { 1.0, -1.0 }), $"{clip}: ±1 ms threshold");

      using var display = new Plot();
      RunCharts.DisplayTimeHistogram(chart, ChartTheme.Light, display);
      AssertBars(display, measured.Select(manifest.DisplayTicks), clip + ": display time histogram");
      double median = Analysis.Statistics.FromTicks(measured.Select(manifest.DisplayTicks)).P50;
      Assert.That(display.GetPlottables<VerticalLine>().Single().X, Is.EqualTo(median), $"{clip}: median display time");
    }

    /// <summary>The percentile curve (|animation error| at every 0.1 percentile) and the drift of every frame.</summary>
    [TestCaseSource(typeof(VideoClips), nameof(VideoClips.Names))]
    public void PercentilesAndDrift_MatchTheManifest(string clip)
    {
      var (manifest, _, chart) = Analyze(clip);

      using var percentiles = new Plot();
      RunCharts.ErrorPercentiles(chart, ChartTheme.Light, percentiles);
      var sorted = Enumerable.Range(1, manifest.FrameCount - 1).Select(i => Ms(Math.Abs(manifest.AnimationErrorTicks(i)))).Order().ToArray();
      var p = Enumerable.Range(0, 1001).Select(i => i / 10.0).ToArray();
      AssertSeries(percentiles, RunCharts.ErrorPercentileLegend, p, p.Select(x => Analysis.Statistics.Percentile(sorted, x / 100)), clip);
      Assert.That(percentiles.GetPlottables<Scatter>().Single().Data.GetScatterPoints()[^1].Y, Is.EqualTo(sorted[^1]), $"{clip}: p100 = worst");

      using var drift = new Plot();
      RunCharts.Drift(chart, ChartTheme.Light, drift);
      var all = Enumerable.Range(0, manifest.FrameCount).ToArray();
      var points = drift.GetPlottables<Scatter>().Single().Data.GetScatterPoints();
      Assert.That(
        points.Select(c => c.X),
        Is.EqualTo(all.Select(i => (manifest.ShownTicks(i) - manifest.ShownTicks(0)) / (double)TimeSpan.TicksPerSecond)),
        $"{clip}: drift times"
      );
      Assert.That(points.Select(c => c.Y), Is.EqualTo(all.Select(i => Ms(manifest.DriftTicks(i)))), $"{clip}: drift");
    }

    /// <summary>The report files: every chart of the run as a PNG of the documented size.</summary>
    [Test]
    public void ChartFiles_WriteEveryChart()
    {
      var (_, report, _) = Analyze("60-busy-swappy");
      var files = ChartFiles.Write(report, ChartTheme.Light);
      Assert.That(
        files.Select(Path.GetFileName),
        Is.EqualTo(
          new[]
          {
            "run-1-timeline.png",
            "run-1-error-histogram.png",
            "run-1-error-percentiles.png",
            "run-1-display-time-histogram.png",
            "run-1-drift.png",
          }
        )
      );
      foreach (var file in files)
      {
        Assert.That(Path.GetDirectoryName(file), Is.EqualTo(report.OutputDirectory), file);
        var (width, height) = PngSize(file);
        Assert.That(width, Is.EqualTo(ChartFiles.Width), file);
        Assert.That(height, Is.GreaterThanOrEqualTo(ChartFiles.DistributionHeight), file);
      }
    }

    private (ClipManifest Manifest, AnalysisReport Report, ChartRun Chart) Analyze(string clip)
    {
      var manifest = VideoClips.Manifest(clip);
      var report = CaptureAnalyzer.Analyze(VideoClips.Import(clip, m_ffmpeg, Path.Combine(m_directory, "capture")), new AnalysisOptions());
      var run = report.Timeline.Runs.Single();
      Assert.That(run.Frames, Has.Count.EqualTo(manifest.FrameCount), $"{clip}: every frame of the clip is presented");
      return (manifest, report, ChartRun.From(report, run));
    }

    /// <summary>The refresh the run is measured with: the capture period, 1/60 s in whole ticks.</summary>
    private static long RefreshTicks(ChartRun chart)
    {
      long refresh = (long)Math.Round(chart.Run.Pacing!.RefreshPeriodMs * TimeSpan.TicksPerMillisecond);
      Assert.That(refresh, Is.AnyOf(166666L, 166667L));
      return refresh;
    }

    /// <summary>The share of late frames (%) among the frames with a display time first shown in the 2 s up to each frame.</summary>
    private static double[] ExpectedLateShare(ClipManifest manifest)
    {
      var shares = new double[manifest.FrameCount];
      int start = 0;
      for (int i = 0; i < manifest.FrameCount; ++i)
      {
        while (manifest.ShownTicks(i) - manifest.ShownTicks(start) >= LateShare.WindowTicks)
          ++start;
        var window = Enumerable.Range(start, i - start + 1).Where(j => j > 0).ToArray();
        int late = window.Count(manifest.IsLate);
        shares[i] = window.Length > 0 ? late / (double)window.Length * 100 : 0;
      }
      return shares;
    }

    private static void AssertSeries(Plot plot, string legend, IEnumerable<double> xs, IEnumerable<double> ys, string clip)
    {
      var expectedX = xs.ToArray();
      var series = plot.GetPlottables<Scatter>().Where(s => s.LegendText == legend).ToArray();
      if (expectedX.Length == 0)
      {
        Assert.That(series, Is.Empty, $"{clip}: no '{legend}' points");
        return;
      }
      Assert.That(series, Has.Length.EqualTo(1), $"{clip}: one '{legend}' series");
      var points = series[0].Data.GetScatterPoints();
      Assert.That(points.Select(p => p.X), Is.EqualTo(expectedX), $"{clip}: '{legend}' x");
      Assert.That(points.Select(p => p.Y), Is.EqualTo(ys.ToArray()), $"{clip}: '{legend}' y");
    }

    /// <summary>
    /// The bars of a histogram of <paramref name="ticks"/>: 0.1 ms bins centred on multiples of 0.1 ms (wider, a multiple, only when the range
    /// needs more than the maximum bin count), one bar per occupied bin at its centre with log10(count) as its height.
    /// </summary>
    private static void AssertBars(Plot plot, IEnumerable<long> ticks, string what)
    {
      var values = ticks.ToArray();
      long width = Histogram.DefaultBinWidthTicks;
      long Bin(long value) => (long)Math.Floor((value / (double)width) + 0.5);
      long needed = Bin(values.Max()) - Bin(values.Min()) + 1;
      if (needed > Histogram.DefaultMaxBins)
        width *= (needed + Histogram.DefaultMaxBins - 1) / Histogram.DefaultMaxBins;
      double widthMs = width / (double)TimeSpan.TicksPerMillisecond;
      var expected = values.GroupBy(Bin).OrderBy(g => g.Key).Select(g => (Position: g.Key * widthMs, Height: Math.Log10(g.Count()))).ToArray();

      var bars = plot.GetPlottables<BarPlot>().Single().Bars;
      Assert.That(bars.Select(b => b.Position), Is.EqualTo(expected.Select(e => e.Position)), what + ": bar positions (ms)");
      Assert.That(bars.Select(b => b.Value), Is.EqualTo(expected.Select(e => e.Height)), what + ": bar heights (log10 count)");
      Assert.That(values.Length, Is.EqualTo(values.GroupBy(Bin).Sum(g => g.Count())), what + ": every frame in a bar");
    }

    private static (int Width, int Height) PngSize(string path)
    {
      // PNG: 8 byte signature, then the IHDR chunk (length, type) with the width and height as big endian 32 bit numbers
      var header = new byte[24];
      using (var stream = File.OpenRead(path))
        stream.ReadExactly(header);
      int BigEndian(int offset) => (header[offset] << 24) | (header[offset + 1] << 16) | (header[offset + 2] << 8) | header[offset + 3];
      return (BigEndian(16), BigEndian(20));
    }

    private static double Ms(long ticks) => ticks / (double)TimeSpan.TicksPerMillisecond;
  }
}
