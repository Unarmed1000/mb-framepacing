//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Writes the charts of every run as PNG files next to the other reports (the command line and the GUI both do after an analysis), and
//* stacks plots into one image (the Timeline).
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.IO;
using System.Linq;
using MB.FramePacing.Analysis;
using ScottPlot;
using SkiaSharp;

namespace MB.FramePacing.Charts
{
  public static class ChartFiles
  {
    public const int Width = 1600;
    public const int DistributionHeight = 600;

    // The Timeline's four plots, top to bottom
    private static readonly int[] g_timelineHeights = { 450, 450, 300, 180 };

    /// <summary>The Timeline image's plots, under the headline band.</summary>
    internal static int TimelinePlotsHeight => g_timelineHeights.Sum();

    /// <summary>
    /// Writes each run's charts into the report directory, named like its frames CSV (<see cref="CaptureAnalyzer.RunFilePrefix"/>):
    /// -timeline.png (under the run's headline band), -error-histogram.png, -error-percentiles.png, -display-time-histogram.png and -drift.png. Returns the files written.
    /// </summary>
    public static IReadOnlyList<string> Write(AnalysisReport report, ChartTheme theme)
    {
      var written = new List<string>();
      var ordinals = new Dictionary<uint, int>();
      foreach (var run in report.Timeline.Runs)
      {
        int ordinal = ordinals.TryGetValue(run.RunId, out int seen) ? seen : 0;
        ordinals[run.RunId] = ordinal + 1;
        string prefix = Path.Combine(report.OutputDirectory, CaptureAnalyzer.RunFilePrefix(run, ordinal));
        written.AddRange(WriteRun(ChartRun.From(report, run), theme, prefix));
      }
      return written;
    }

    /// <summary>Writes one run's charts to files named <paramref name="prefix"/> plus the chart's name.</summary>
    public static IReadOnlyList<string> WriteRun(ChartRun run, ChartTheme theme, string prefix)
    {
      var written = new List<string>();
      var timeline = new[] { new Plot(), new Plot(), new Plot(), new Plot() };
      try
      {
        RunCharts.Timeline(run, theme, timeline[0], timeline[1], timeline[2], timeline[3]);
        using var headline = HeadlineBand.Render(run, theme, Width);
        written.Add(Stack(prefix + "-timeline.png", Width, headline, timeline.Zip(g_timelineHeights).ToArray()));
      }
      finally
      {
        foreach (var plot in timeline)
          plot.Dispose();
      }

      foreach (
        var (name, draw) in new (string, System.Action<ChartRun, ChartTheme, Plot>)[]
        {
          ("error-histogram", RunCharts.ErrorHistogram),
          ("error-percentiles", RunCharts.ErrorPercentiles),
          ("display-time-histogram", RunCharts.DisplayTimeHistogram),
          ("drift", RunCharts.Drift),
        }
      )
      {
        using var plot = new Plot();
        draw(run, theme, plot);
        string path = $"{prefix}-{name}.png";
        plot.SavePng(path, Width, DistributionHeight);
        written.Add(path);
      }
      return written;
    }

    /// <summary>Renders the plots one below the other, each at its height, into one PNG file; returns its path.</summary>
    public static string SaveStacked(string path, int width, params (Plot Plot, int Height)[] parts) => Stack(path, width, null, parts);

    /// <summary>The plots one below the other, under <paramref name="header"/> when there is one.</summary>
    private static string Stack(string path, int width, SKImage? header, (Plot Plot, int Height)[] parts)
    {
      int height = (header?.Height ?? 0) + parts.Sum(p => p.Height);
      using var surface = SKSurface.Create(new SKImageInfo(width, height));
      int y = 0;
      if (header != null)
      {
        surface.Canvas.DrawImage(header, 0, 0);
        y = header.Height;
      }
      foreach (var (plot, partHeight) in parts)
      {
        using var image = SKImage.FromEncodedData(plot.GetImage(width, partHeight).GetImageBytes());
        surface.Canvas.DrawImage(image, 0, y);
        y += partHeight;
      }
      using var png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
      File.WriteAllBytes(path, png.ToArray());
      return path;
    }
  }
}
