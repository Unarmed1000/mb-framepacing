//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The animation error per frame as signed bars around zero (after Gamers Nexus and mb-framepacing-explained's charts): up is shown too soon,
//* down shown too late, and a frame without error draws nothing, so the frames that are off stand out. Behind the bars a faint band marks the
//* error threshold. The scale is symmetric around zero and covers every error, unless a few are far larger than the rest (a hitch among
//* small errors): then it covers the 99th percentile, and the bars it cuts off get a mark at the edge with their value. Each bar is one
//* refresh wide; zoomed out, when bars get narrower than a pixel or two, each pixel column draws the most negative to the most positive error
//* in it, so no spike is lost. Draws only the visible frames.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MB.FramePacing.Analysis;
using ScottPlot;
using SkiaSharp;

namespace MB.FramePacing.Charts
{
  internal sealed class AnimationErrorBarsPlottable : IPlottable
  {
    // The smallest scale (ms either way) and the room above the largest error
    internal const double MinLimitMs = 2;
    internal const double Headroom = 1.15;

    // The scale only leaves out the largest errors when they need more than this many times the room the 99th percentile needs
    internal const double ClipFactor = 8;
    internal const double BulkPercentile = 0.99;

    // At most this many grid lines on each side of zero
    private const int MaxTicksPerSide = 4;

    // Bars narrower than this are merged per pixel column; the gap between bars; the smallest visible bar
    private const float MinBarPixels = 2;
    private const float BarGapPixels = 0.6f;
    private const float MinBarHeightPixels = 0.8f;

    private readonly long[] m_timeTicks;
    private readonly long[] m_errorTicks;
    private readonly double[] m_time;
    private readonly double[] m_errorMs;
    private readonly double m_barSeconds;
    private readonly double m_thresholdMs;
    private readonly long m_thresholdTicks;

    /// <param name="frames">The run's presented frames; those without an animation error are left out.</param>
    /// <param name="originTicks">The time at x = 0 (the run's first frame).</param>
    /// <param name="refreshTicks">The display's refresh period: the width of a bar.</param>
    /// <param name="thresholdTicks">The error threshold: the band drawn behind the bars.</param>
    public AnimationErrorBarsPlottable(IReadOnlyList<PresentedFrame> frames, long originTicks, long refreshTicks, long thresholdTicks)
    {
      var withError = frames.Where(f => f.AnimationErrorTicks.HasValue).ToArray();
      m_timeTicks = withError.Select(f => f.FirstSeenTicks - originTicks).ToArray();
      m_errorTicks = withError.Select(f => f.AnimationErrorTicks!.Value).ToArray();
      m_time = m_timeTicks.Select(t => t / (double)TimeSpan.TicksPerSecond).ToArray();
      m_errorMs = m_errorTicks.Select(Ms).ToArray();
      m_barSeconds = refreshTicks / (double)TimeSpan.TicksPerSecond;
      m_thresholdTicks = thresholdTicks;
      m_thresholdMs = Ms(thresholdTicks);
      LimitMs = Limit(m_errorMs);
    }

    /// <summary>Each frame's time from the origin and its animation error, for the frames that have one.</summary>
    internal IReadOnlyList<long> TimeTicks => m_timeTicks;

    internal IReadOnlyList<long> ErrorTicks => m_errorTicks;

    /// <summary>The error threshold the band shows.</summary>
    internal long ThresholdTicks => m_thresholdTicks;

    /// <summary>The scale: from -LimitMs to +LimitMs.</summary>
    internal double LimitMs { get; }

    public Color BarColor { get; set; } = Colors.Red;
    public Color BandColor { get; set; } = Colors.Black.WithAlpha(0.08);
    public Color ZeroLineColor { get; set; } = Colors.Black.WithAlpha(0.45);
    public Color LabelColor { get; set; } = Colors.Black;

    public bool IsVisible { get; set; } = true;

    public IAxes Axes { get; set; } = new Axes();

    public IEnumerable<LegendItem> LegendItems => Array.Empty<LegendItem>();

    /// <summary>
    /// The symmetric scale: the largest error with some room above it, at least <see cref="MinLimitMs"/>. When that needs more than
    /// <see cref="ClipFactor"/> times the room of the 99th percentile (a few hitches far above everything else), the 99th percentile's.
    /// </summary>
    internal static double Limit(IReadOnlyCollection<double> errorsMs)
    {
      if (errorsMs.Count == 0)
        return MinLimitMs;
      var sorted = errorsMs.Select(Math.Abs).Order().ToArray();
      double all = Math.Max(MinLimitMs, sorted[^1] * Headroom);
      double bulk = Math.Max(MinLimitMs, Statistics.Percentile(sorted, BulkPercentile) * Headroom);
      return all <= ClipFactor * bulk ? all : bulk;
    }

    /// <summary>The grid step: 1 ms, doubled until at most <see cref="MaxTicksPerSide"/> lines fit on each side of zero.</summary>
    internal static double TickStep(double limitMs)
    {
      double step = 1;
      while (limitMs / step > MaxTicksPerSide)
        step *= 2;
      return step;
    }

    /// <summary>The y axis ticks: zero, and the grid step either way inside the limit, labelled with their sign ("+2 ms", "-2 ms").</summary>
    internal static IReadOnlyList<(double Position, string Label)> Ticks(double limitMs)
    {
      double step = TickStep(limitMs);
      var ticks = new List<(double, string)> { (0, "0") };
      for (double value = step; value < limitMs; value += step)
      {
        string text = value.ToString("0.###", CultureInfo.CurrentCulture);
        ticks.Add((value, $"+{text} ms"));
        ticks.Add((-value, $"-{text} ms"));
      }
      return ticks;
    }

    public AxisLimits GetAxisLimits() =>
      m_time.Length > 0 ? new AxisLimits(m_time[0], m_time[^1] + m_barSeconds, -LimitMs, LimitMs) : AxisLimits.NoLimits;

    public void Render(RenderPack rp)
    {
      if (m_time.Length == 0)
        return;
      var canvas = rp.Canvas;
      var data = rp.DataRect;
      double left = Axes.XAxis.Min;
      double right = Axes.XAxis.Max;
      float zero = Axes.GetPixelY(0);
      using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

      // The band of errors within the threshold, behind the bars
      paint.Color = BandColor.ToSKColor();
      canvas.DrawRect(new SKRect(data.Left, Axes.GetPixelY(m_thresholdMs), data.Right, Axes.GetPixelY(-m_thresholdMs)), paint);

      // A bar from zero to the error, never thinner than MinBarHeightPixels
      float Tip(double ms)
      {
        float y = Axes.GetPixelY(ms);
        return ms > 0 ? Math.Min(y, zero - MinBarHeightPixels) : Math.Max(y, zero + MinBarHeightPixels);
      }

      // Errors beyond the visible scale: a mark at the edge
      double top = Axes.YAxis.Max;
      double bottom = Axes.YAxis.Min;
      var clipped = new ClippedValueMarks();
      void Clip(float x, double ms)
      {
        if (ms > top)
          clipped.Add(x, ms, top: true);
        else if (ms < bottom)
          clipped.Add(x, ms, top: false);
      }

      paint.Color = BarColor.ToSKColor();
      float barPixels = Axes.GetPixelX(left + m_barSeconds) - Axes.GetPixelX(left);
      int first = Array.BinarySearch(m_time, left - m_barSeconds);
      first = first < 0 ? ~first : first;
      if (barPixels >= MinBarPixels)
      {
        for (int i = first; i < m_time.Length && m_time[i] <= right; ++i)
        {
          if (m_errorTicks[i] == 0)
            continue;
          float x = Axes.GetPixelX(m_time[i]);
          float tip = Tip(m_errorMs[i]);
          canvas.DrawRect(new SKRect(x, Math.Min(zero, tip), x + barPixels - BarGapPixels, Math.Max(zero, tip)), paint);
          Clip(x + ((barPixels - BarGapPixels) / 2), m_errorMs[i]);
        }
      }
      else
      {
        // Zoomed out: one bar per pixel column, from its most negative to its most positive error
        int column = int.MinValue;
        float up = zero;
        float down = zero;
        double most = 0;
        double least = 0;
        void Flush()
        {
          if (up < down)
            canvas.DrawRect(new SKRect(column, up, column + 1, down), paint);
          Clip(column + 0.5f, most);
          Clip(column + 0.5f, least);
        }
        for (int i = first; i < m_time.Length && m_time[i] <= right; ++i)
        {
          int x = (int)Math.Floor(Axes.GetPixelX(m_time[i]));
          if (x != column)
          {
            Flush();
            column = x;
            up = zero;
            down = zero;
            most = 0;
            least = 0;
          }
          if (m_errorTicks[i] == 0)
            continue;
          float tip = Tip(m_errorMs[i]);
          up = Math.Min(up, tip);
          down = Math.Max(down, tip);
          most = Math.Max(most, m_errorMs[i]);
          least = Math.Min(least, m_errorMs[i]);
        }
        Flush();
      }

      // The zero line on top
      using var line = new SKPaint
      {
        IsAntialias = false,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1,
        Color = ZeroLineColor.ToSKColor(),
      };
      canvas.DrawLine(data.Left, zero, data.Right, zero, line);
      clipped.Draw(canvas, data, BarColor.ToSKColor(), LabelColor.ToSKColor(), ms => ms.ToString("+0.#;-0.#", CultureInfo.CurrentCulture) + " ms");
    }

    private static double Ms(long ticks) => ticks / (double)TimeSpan.TicksPerMillisecond;
  }
}
