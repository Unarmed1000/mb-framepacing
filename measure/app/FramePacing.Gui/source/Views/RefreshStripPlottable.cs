//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The refresh strip: one cell per display refresh, shaded by the frame on screen, so hold patterns (3-then-1, 2-2-2) show at a glance
//* when zoomed in. Draws only the visible frames.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using MB.FramePacing.Analysis;
using ScottPlot;
using SkiaSharp;

namespace MB.FramePacing.Gui.Views
{
  internal sealed class RefreshStripPlottable : IPlottable
  {
    // Cell and frame edges are only drawn when they are at least this far apart on screen
    private const float MinCellPixels = 4;

    private readonly double[] m_start;
    private readonly double[] m_end;
    private readonly int[] m_cells;
    private readonly PresentedFrameFlags[] m_flags;
    private readonly bool[] m_skippedBefore;

    /// <param name="frames">The run's presented frames.</param>
    /// <param name="seconds">A frame's first-seen time on the chart's x axis (seconds).</param>
    /// <param name="refreshSeconds">The display's refresh period.</param>
    /// <param name="capturePeriodSeconds">The capture period.</param>
    /// <param name="camera">
    /// A camera sees each frame until the next one (the undecodable captures between are the scanout crossing the marker); a capture card
    /// sees whole refreshes, so captures between two frames that could not be decoded stay unknown (grey).
    /// </param>
    public RefreshStripPlottable(
      IReadOnlyList<PresentedFrame> frames,
      Func<PresentedFrame, double> seconds,
      double refreshSeconds,
      double capturePeriodSeconds,
      bool camera
    )
    {
      int n = frames.Count;
      m_start = new double[n];
      m_end = new double[n];
      m_cells = new int[n];
      m_flags = new PresentedFrameFlags[n];
      m_skippedBefore = new bool[n];
      for (int i = 0; i < n; ++i)
      {
        var frame = frames[i];
        bool hasNext = i + 1 < n && frames[i + 1].Segment == frame.Segment;
        double start = seconds(frame);
        double lastSeen = start + ((frame.LastSeenTicks - frame.FirstSeenTicks) / (double)TimeSpan.TicksPerSecond) + capturePeriodSeconds;
        double end = camera && hasNext ? seconds(frames[i + 1]) : lastSeen;
        if (hasNext)
          end = Math.Min(end, seconds(frames[i + 1]));
        m_start[i] = start;
        m_end[i] = end;
        m_cells[i] = Math.Max(1, (int)Math.Round((end - start) / refreshSeconds));
        m_flags[i] = frame.Flags;
        m_skippedBefore[i] = frame.SkippedBefore > 0;
      }
    }

    public Color EvenColor { get; set; } = Colors.SteelBlue;
    public Color OddColor { get; set; } = Colors.LightSteelBlue;
    public Color LateColor { get; set; } = Colors.OrangeRed;
    public Color UnknownColor { get; set; } = Colors.Gray;
    public Color EdgeColor { get; set; } = Colors.White;
    public Color MarkColor { get; set; } = Colors.Black;

    public bool IsVisible { get; set; } = true;

    public IAxes Axes { get; set; } = new Axes();

    public IEnumerable<LegendItem> LegendItems => Array.Empty<LegendItem>();

    public AxisLimits GetAxisLimits() => m_start.Length > 0 ? new AxisLimits(m_start[0], m_end[^1], 0, 1) : AxisLimits.NoLimits;

    public void Render(RenderPack rp)
    {
      if (m_start.Length == 0)
        return;
      var canvas = rp.Canvas;
      float top = Axes.GetPixelY(0.85);
      float bottom = Axes.GetPixelY(0.15);
      double left = Axes.XAxis.Min;
      double right = Axes.XAxis.Max;
      using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
      using var line = new SKPaint
      {
        IsAntialias = false,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1,
      };

      // Unknown (grey) behind everything: captures between frames that could not be decoded
      paint.Color = UnknownColor.ToSKColor();
      canvas.DrawRect(new SKRect(Axes.GetPixelX(Math.Max(left, m_start[0])), top, Axes.GetPixelX(Math.Min(right, m_end[^1])), bottom), paint);

      int first = Array.BinarySearch(m_end, left);
      first = first < 0 ? ~first : first;
      for (int i = first; i < m_start.Length && m_start[i] <= right; ++i)
      {
        float x0 = Axes.GetPixelX(m_start[i]);
        float x1 = Axes.GetPixelX(m_end[i]);
        bool late = (m_flags[i] & PresentedFrameFlags.Late) != 0;
        paint.Color = (
          late ? LateColor
          : i % 2 == 0 ? EvenColor
          : OddColor
        ).ToSKColor();
        canvas.DrawRect(new SKRect(x0, top, Math.Max(x1, x0 + 1), bottom), paint);

        float cell = (x1 - x0) / m_cells[i];
        if (cell >= MinCellPixels)
        {
          // Frame edge (solid) and the refreshes inside it (thin)
          line.Color = EdgeColor.ToSKColor();
          line.StrokeWidth = 2;
          canvas.DrawLine(x0, top, x0, bottom, line);
          line.StrokeWidth = 1;
          line.Color = EdgeColor.WithAlpha(0.6).ToSKColor();
          for (int c = 1; c < m_cells[i]; ++c)
          {
            float x = x0 + (c * cell);
            canvas.DrawLine(x, top + ((bottom - top) * 0.3f), x, bottom, line);
          }
        }

        // Frames that were rendered but never shown, and tears (camera), get a mark above the strip
        if (m_skippedBefore[i] || (m_flags[i] & PresentedFrameFlags.Torn) != 0)
        {
          line.Color = MarkColor.ToSKColor();
          line.StrokeWidth = 2;
          canvas.DrawLine(x0, top - 6, x0, top, line);
        }
      }
    }
  }
}
