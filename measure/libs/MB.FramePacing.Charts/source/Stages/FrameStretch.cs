//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The frames a frame stages chart shows, and where a time is on its x axis: time runs from left to right, from a little before the first
//* thing done for the first frame to a little after the last frame left the screen (or its work ended).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Charts.Stages
{
  internal sealed class FrameStretch
  {
    private FrameStretch(
      IReadOnlyList<FrameLogFrame> frames,
      int position,
      IReadOnlyList<FrameLogFrame> rows,
      long period,
      long timeFirst,
      long timeLast,
      double left,
      double plotWidth
    )
    {
      Frames = frames;
      Position = position;
      Rows = rows;
      Period = period;
      TimeFirst = timeFirst;
      TimeLast = timeLast;
      Left = left;
      PlotWidth = plotWidth;
    }

    /// <summary>Every frame of the log.</summary>
    public IReadOnlyList<FrameLogFrame> Frames { get; }

    /// <summary>Where the chart's first frame is among them.</summary>
    public int Position { get; }

    /// <summary>The frames the chart shows.</summary>
    public IReadOnlyList<FrameLogFrame> Rows { get; }

    /// <summary>The display's refresh period, in the log's ticks; 0 when the log does not say.</summary>
    public long Period { get; }

    public long TimeFirst { get; }

    public long TimeLast { get; }

    public double Left { get; }

    public double PlotWidth { get; }

    public double Scale => PlotWidth / (TimeLast - TimeFirst);

    public double X(long ticks) => Left + ((ticks - TimeFirst) * Scale);

    public bool Inside(long ticks) => TimeFirst <= ticks && ticks <= TimeLast;

    /// <summary>The frame before the row at <paramref name="offset"/>, also when it is before the chart's first row.</summary>
    public FrameLogFrame? Before(int offset) => Position + offset > 0 ? Frames[Position + offset - 1] : null;

    /// <summary>
    /// The stretch of <paramref name="count"/> frames from frame <paramref name="first"/> on. A log without that frame or a later one is
    /// an <see cref="ArgumentException"/>.
    /// </summary>
    public static FrameStretch Of(IReadOnlyList<FrameLogFrame> frames, long first, int count, double left, double plotWidth)
    {
      int position = -1;
      for (int i = 0; i < frames.Count && position < 0; ++i)
      {
        if (frames[i].Index >= first)
          position = i;
      }
      if (position < 0)
        throw new ArgumentException($"The log has no frame {first} or later (its last is {frames[^1].Index})");
      var rows = frames.Skip(position).Take(count).ToList();
      long period = FrameLogStages.RefreshPeriod(rows);
      long timeFirst = long.MaxValue;
      long timeLast = long.MinValue;
      for (int offset = 0; offset < rows.Count; ++offset)
      {
        var frame = rows[offset];
        foreach (string key in new[] { "hostCpuStartTicks", "frameWaitStartTicks", "frameStartTicks" })
        {
          if (frame.At(key) is { } begin)
            timeFirst = Math.Min(timeFirst, begin);
        }
        timeLast = Math.Max(timeLast, frame.Start + period);
        foreach (string key in new[] { "presentReturnTicks", "gpuWorkEndTicks" })
        {
          if (frame.At(key) is { } end)
            timeLast = Math.Max(timeLast, end);
        }
        if (FrameLogStages.ScreenEnd(frames, position + offset, period) is { } screenEnd)
          timeLast = Math.Max(timeLast, screenEnd);
      }
      long margin = Math.Max(FrameLogStages.FloorDivide(timeLast - timeFirst, 80), 1);
      return new FrameStretch(frames, position, rows, period, timeFirst - margin, timeLast + margin, left, plotWidth);
    }

    /// <summary>A step in ticks for the millisecond axis that gives about a dozen labels: 1, 2 or 5 times a power of ten.</summary>
    public static long AxisStep(long spanTicks)
    {
      long step = 1_000;
      foreach (long factor in new long[] { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1_000, 2_000, 5_000 })
      {
        step = 1_000 * factor;
        if (spanTicks / step <= 14)
          break;
      }
      return step;
    }

    public string Subtitle(string view)
    {
      string hz =
        Period != 0
          ? $"{SvgMarkup.Fixed(10_000_000 / (double)Period, 2)} Hz ({FrameStagesTips.Ms(Period, 2)} ms a refresh)"
          : "an unknown refresh rate";
      var intervals = Rows.Select(frame => frame.SwapInterval).Distinct().OrderBy(interval => interval).Select(FrameStagesTips.Whole);
      return $"Frames {FrameStagesTips.Whole(Rows[0].Index)} to {FrameStagesTips.Whole(Rows[^1].Index)} on a display at {hz}, "
        + $"swap interval {string.Join(" and ", intervals)}. {view}; times on the application's CPU clock.";
    }

    /// <summary>The refresh grid: the vertical blanks the sample read on the CPU clock, else the reported display times. Says which.</summary>
    public string DrawGrid(FrameStagesCanvas canvas, double top, double bottom)
    {
      long? anchor = Rows.Select(frame => frame.At("displayVSyncTicks")).FirstOrDefault(value => value != null);
      string grid = "the vertical blank times the application read on its CPU clock";
      if (anchor == null)
      {
        anchor = Rows.Select(frame => frame.Shown).FirstOrDefault(value => value != null);
        grid = "the display times the driver reported";
      }
      if (anchor is { } from && Period > 0)
      {
        long number = -FrameLogStages.FloorDivide(from - TimeFirst, Period);
        long labelEvery = Math.Max(1, (long)Math.Round(26 / (Period * Scale)));
        while (from + (number * Period) <= TimeLast)
        {
          double at = X(from + (number * Period));
          canvas.Line(at, top, at, bottom, "vsync");
          if (number % labelEvery == 0)
            canvas.Text(at, top - 6, FrameStagesTips.Whole(number), "vsync-n", "middle");
          ++number;
        }
        canvas.Text(Left - 8, top - 6, "refresh", "vsync-n", "end");
      }
      return grid;
    }

    /// <summary>Milliseconds on the CPU clock, from the first frame's start.</summary>
    public void DrawAxis(FrameStagesCanvas canvas, double bottom)
    {
      long zero = Rows[0].Start;
      long step = AxisStep(TimeLast - TimeFirst);
      long tick = -(FrameLogStages.FloorDivide(zero - TimeFirst, step) * step);
      while (zero + tick <= TimeLast)
      {
        double at = X(zero + tick);
        canvas.Line(at, bottom, at, bottom + 5, "zero-line");
        canvas.Text(at, bottom + 20, FrameStagesTips.Ms(tick, 0), "axis", "middle");
        tick += step;
      }
      canvas.Text(Left - 8, bottom + 20, "ms", "axis", "end");
    }
  }
}
