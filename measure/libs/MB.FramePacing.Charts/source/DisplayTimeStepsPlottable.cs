//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The display time as held steps (after mb-framepacing-explained's frame chart): each frame is a horizontal step from when it was first seen
//* until the next frame, at how long it stayed on screen (the next frame's display time), in green, or in red when it was held too long
//* because the next frame was late. Faint risers join the steps where the level changes. Zoomed out, when holds get narrower than a pixel or
//* two, each pixel column draws the range of its holds, red if any of them was held too long. The scale covers every hold, unless a few are
//* far longer than the rest (a hitch): then it covers the 99th percentile, and the holds it cuts off get a mark at the top with their
//* length. Draws only the visible holds.
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
  internal sealed class DisplayTimeStepsPlottable : IPlottable
  {
    // At most this many grid lines above the first refresh
    private const int MaxTicks = 8;

    // The scale only leaves out the longest holds when they need more than this many times the room the 99th percentile needs
    internal const double ClipFactor = 8;
    internal const double BulkPercentile = 0.99;

    // Holds narrower than this are merged per pixel column; the step's line width
    private const float MinHoldPixels = 2;
    private const float StepWidth = 2.5f;

    private readonly List<long> m_startTicks = new List<long>();
    private readonly List<long> m_endTicks = new List<long>();
    private readonly List<long> m_levelTicks = new List<long>();
    private readonly List<bool> m_heldTooLong = new List<bool>();
    private readonly double[] m_start;
    private readonly double[] m_end;
    private readonly double[] m_levelMs;

    /// <param name="frames">The run's presented frames.</param>
    /// <param name="originTicks">The time at x = 0 (the run's first frame).</param>
    /// <param name="refreshTicks">The display's refresh period: the grid and the scale.</param>
    public DisplayTimeStepsPlottable(IReadOnlyList<PresentedFrame> frames, long originTicks, long refreshTicks)
    {
      // A frame's hold is known from the next frame of its segment: its display time and whether it came late
      for (int i = 0; i + 1 < frames.Count; ++i)
      {
        var next = frames[i + 1];
        if (next.Segment != frames[i].Segment || !next.DisplayDeltaTicks.HasValue)
          continue;
        m_startTicks.Add(frames[i].FirstSeenTicks - originTicks);
        m_endTicks.Add(next.FirstSeenTicks - originTicks);
        m_levelTicks.Add(next.DisplayDeltaTicks.Value);
        m_heldTooLong.Add(next.Flags.HasFlag(PresentedFrameFlags.Late));
      }
      m_start = m_startTicks.Select(Seconds).ToArray();
      m_end = m_endTicks.Select(Seconds).ToArray();
      m_levelMs = m_levelTicks.Select(Ms).ToArray();
      RefreshMs = Ms(refreshTicks);
      TopMs = Top(m_levelMs, RefreshMs);
    }

    /// <summary>Each hold: from its frame's first sighting to the next frame's (from the origin), how long, and whether too long.</summary>
    internal IReadOnlyList<long> StartTicks => m_startTicks;

    internal IReadOnlyList<long> EndTicks => m_endTicks;

    internal IReadOnlyList<long> LevelTicks => m_levelTicks;

    internal IReadOnlyList<bool> HeldTooLong => m_heldTooLong;

    internal double RefreshMs { get; }

    /// <summary>The top of the scale: half a refresh above the longest hold (at least two refreshes), or above the 99th percentile (see <see cref="Top"/>).</summary>
    internal double TopMs { get; }

    public Color OnTimeColor { get; set; } = Colors.Green;
    public Color HeldTooLongColor { get; set; } = Colors.Red;
    public Color RiserColor { get; set; } = Colors.Black.WithAlpha(0.3);
    public Color LabelColor { get; set; } = Colors.Black;

    public bool IsVisible { get; set; } = true;

    public IAxes Axes { get; set; } = new Axes();

    public IEnumerable<LegendItem> LegendItems => Array.Empty<LegendItem>();

    /// <summary>
    /// Half a refresh above the longest hold, at least two refreshes. When that needs more than <see cref="ClipFactor"/> times the room of the
    /// 99th percentile (a few hitches far longer than everything else), half a refresh above the 99th percentile.
    /// </summary>
    internal static double Top(IReadOnlyCollection<double> levelsMs, double refreshMs)
    {
      if (levelsMs.Count == 0)
        return (2 * refreshMs) + (refreshMs / 2);
      var sorted = levelsMs.Order().ToArray();
      double all = Math.Max(2 * refreshMs, sorted[^1]) + (refreshMs / 2);
      double bulk = Math.Max(2 * refreshMs, Statistics.Percentile(sorted, BulkPercentile)) + (refreshMs / 2);
      return all <= ClipFactor * bulk ? all : bulk;
    }

    /// <summary>
    /// The y axis ticks: a line at every whole number of refreshes (16.7, 33.3, 50 ms at 60 Hz), the grid a display shows frames on. When more
    /// than <see cref="MaxTicks"/> would fit, the first refresh and every 2nd, 4th, 8th... refresh after it.
    /// </summary>
    internal static IReadOnlyList<(double Position, string Label)> Ticks(double refreshMs, double topMs)
    {
      var ticks = new List<(double, string)> { (0, "0") };
      if (refreshMs <= 0)
        return ticks;
      int count = (int)Math.Floor(topMs / refreshMs);
      int step = 1;
      while (count / step > MaxTicks)
        step *= 2;
      for (int refreshes = 1; refreshes <= count; ++refreshes)
      {
        if (refreshes == 1 || refreshes % step == 0)
        {
          double ms = refreshes * refreshMs;
          ticks.Add((ms, ms.ToString("0.0", CultureInfo.CurrentCulture) + " ms"));
        }
      }
      return ticks;
    }

    public AxisLimits GetAxisLimits() => m_start.Length > 0 ? new AxisLimits(m_start[0], m_end[^1], 0, TopMs) : AxisLimits.NoLimits;

    public void Render(RenderPack rp)
    {
      if (m_start.Length == 0)
        return;
      var canvas = rp.Canvas;
      double left = Axes.XAxis.Min;
      double right = Axes.XAxis.Max;
      using var step = new SKPaint
      {
        IsAntialias = false,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = StepWidth,
      };
      using var riser = new SKPaint
      {
        IsAntialias = false,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1,
        Color = RiserColor.ToSKColor(),
      };
      using var fill = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
      SKColor Color(bool heldTooLong) => (heldTooLong ? HeldTooLongColor : OnTimeColor).ToSKColor();

      // Holds beyond the visible scale: a mark at the top
      double visibleTop = Axes.YAxis.Max;
      var clipped = new ClippedValueMarks();

      // Zoomed out: the pixel column's holds, from the shortest to the longest
      int column = int.MinValue;
      float columnEnd = 0;
      float top = 0;
      float bottom = 0;
      bool late = false;
      double longest = 0;
      void Flush()
      {
        if (column == int.MinValue)
          return;
        if (longest > visibleTop)
          clipped.Add(column + 0.5f, longest, top: true);
        fill.Color = Color(late);
        canvas.DrawRect(new SKRect(column, top - (StepWidth / 2), Math.Max(column + 1, columnEnd), bottom + (StepWidth / 2)), fill);
        column = int.MinValue;
      }

      int first = Array.BinarySearch(m_end, left);
      first = first < 0 ? ~first : first;
      for (int i = first; i < m_start.Length && m_start[i] <= right; ++i)
      {
        float x0 = Axes.GetPixelX(m_start[i]);
        float x1 = Axes.GetPixelX(m_end[i]);
        float y = Axes.GetPixelY(m_levelMs[i]);
        if (x1 - x0 >= MinHoldPixels)
        {
          Flush();
          // The riser from the previous hold, where it ends as this one starts
          if (i > 0 && m_endTicks[i - 1] == m_startTicks[i] && m_levelTicks[i - 1] != m_levelTicks[i])
            canvas.DrawLine(x0, Axes.GetPixelY(m_levelMs[i - 1]), x0, y, riser);
          step.Color = Color(m_heldTooLong[i]);
          canvas.DrawLine(x0, y, x1, y, step);
          if (m_levelMs[i] > visibleTop)
            clipped.Add((x0 + x1) / 2, m_levelMs[i], top: true);
          continue;
        }
        int x = (int)Math.Floor(x0);
        if (x != column)
        {
          Flush();
          column = x;
          columnEnd = x1;
          top = y;
          bottom = y;
          late = false;
          longest = 0;
        }
        // Up to where its last hold ends, so the columns join into a line
        columnEnd = Math.Max(columnEnd, x1);
        top = Math.Min(top, y);
        bottom = Math.Max(bottom, y);
        late |= m_heldTooLong[i];
        longest = Math.Max(longest, m_levelMs[i]);
      }
      Flush();
      clipped.Draw(
        canvas,
        rp.DataRect,
        HeldTooLongColor.ToSKColor(),
        LabelColor.ToSKColor(),
        ms => ms.ToString("0.#", CultureInfo.CurrentCulture) + " ms"
      );
    }

    private static double Seconds(long ticks) => ticks / (double)TimeSpan.TicksPerSecond;

    private static double Ms(long ticks) => ticks / (double)TimeSpan.TicksPerMillisecond;
  }
}
