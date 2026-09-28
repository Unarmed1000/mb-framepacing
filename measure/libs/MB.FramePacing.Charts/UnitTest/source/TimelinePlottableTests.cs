//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The Timeline's animation error bars and display time steps on synthetic frames: their scales and grids, which holds they draw, and that
//* zoomed out (many frames per pixel) a single bad frame still shows.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Analysis;
using NUnit.Framework;
using ScottPlot;
using SkiaSharp;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class TimelinePlottableTests
  {
    private const long Refresh = TimeSpan.TicksPerSecond / 60;

    [TestCase(new double[] { 0, 0 }, 2.0)]
    [TestCase(new double[] { 1, -1.5 }, 2.0)]
    [TestCase(new double[] { 5, -3 }, 5 * 1.15)]
    [TestCase(new double[] { 2, -700 }, 700 * 1.15)]
    public void ErrorLimit_IsSymmetricWithRoomAboveTheLargestError(double[] errorsMs, double expected)
    {
      Assert.That(AnimationErrorBarsPlottable.Limit(errorsMs), Is.EqualTo(expected));
    }

    [TestCase(2.0, 1.0)]
    [TestCase(4.0, 1.0)]
    [TestCase(5.75, 2.0)]
    [TestCase(8.0, 2.0)]
    [TestCase(11.5, 4.0)]
    [TestCase(805.0, 256.0)]
    public void ErrorTickStep_KeepsAtMostFourLinesEachSide(double limitMs, double expected)
    {
      Assert.That(AnimationErrorBarsPlottable.TickStep(limitMs), Is.EqualTo(expected));
    }

    [Test]
    public void ErrorTicks_AreZeroAndSignedSteps()
    {
      var ticks = AnimationErrorBarsPlottable.Ticks(5.75);
      Assert.That(ticks.Select(t => t.Position), Is.EquivalentTo(new[] { 0.0, 2, -2, 4, -4 }));
      Assert.That(ticks.Single(t => t.Position == 2).Label, Is.EqualTo("+2 ms"));
      Assert.That(ticks.Single(t => t.Position == -4).Label, Is.EqualTo("-4 ms"));
      Assert.That(ticks.Single(t => t.Position == 0).Label, Is.EqualTo("0"));
    }

    [Test]
    public void DisplayTicks_AreWholeRefreshes()
    {
      double refreshMs = 1000.0 / 60;
      var ticks = DisplayTimeStepsPlottable.Ticks(refreshMs, DisplayTimeStepsPlottable.Top(new[] { refreshMs, 3 * refreshMs }, refreshMs));
      Assert.That(ticks.Select(t => t.Position), Is.EqualTo(new[] { 0, refreshMs, 2 * refreshMs, 3 * refreshMs }));
    }

    [Test]
    public void DisplayTicks_ThinOutForLongHolds()
    {
      // A 1 s hitch at 100 Hz: the first refresh, then every 16th
      var ticks = DisplayTimeStepsPlottable.Ticks(10, DisplayTimeStepsPlottable.Top(new[] { 10.0, 1000 }, 10));
      Assert.That(ticks.Select(t => t.Position), Is.EqualTo(new[] { 0.0, 10, 160, 320, 480, 640, 800, 960 }));
    }

    [Test]
    public void DisplayTop_IsHalfARefreshAboveTheLongestHold_AtLeastTwoRefreshes()
    {
      Assert.That(DisplayTimeStepsPlottable.Top(new[] { 10.0 }, 10), Is.EqualTo(25));
      Assert.That(DisplayTimeStepsPlottable.Top(new[] { 10.0, 40 }, 10), Is.EqualTo(45));
    }

    [Test]
    public void DisplayHolds_RunUntilTheNextFrameOfTheSegment()
    {
      // Segment 0: three frames, the third late (the second is held too long); segment 1: two frames
      var frames = new[]
      {
        Frame(0, 0, null),
        Frame(0, Refresh, Refresh),
        Frame(0, 3 * Refresh, 2 * Refresh, late: true),
        Frame(1, 10 * Refresh, null),
        Frame(1, 11 * Refresh, Refresh),
      };
      var steps = new DisplayTimeStepsPlottable(frames, 0, Refresh);
      Assert.That(steps.StartTicks, Is.EqualTo(new[] { 0, Refresh, 10 * Refresh }));
      Assert.That(steps.EndTicks, Is.EqualTo(new[] { Refresh, 3 * Refresh, 11 * Refresh }));
      Assert.That(steps.LevelTicks, Is.EqualTo(new[] { Refresh, 2 * Refresh, Refresh }));
      Assert.That(steps.HeldTooLong, Is.EqualTo(new[] { false, true, false }));
    }

    [Test]
    public void ErrorBars_LeaveOutFramesWithoutAnError()
    {
      var frames = new[] { Frame(0, 0, null), Frame(0, Refresh, Refresh, errorTicks: 5000), Frame(0, 2 * Refresh, Refresh, errorTicks: 0) };
      var bars = new AnimationErrorBarsPlottable(frames, 0, Refresh, TimeSpan.TicksPerMillisecond);
      Assert.That(bars.TimeTicks, Is.EqualTo(new[] { Refresh, 2 * Refresh }));
      Assert.That(bars.ErrorTicks, Is.EqualTo(new[] { 5000L, 0 }));
    }

    /// <summary>Ten minutes at 60 Hz in 400 pixels: the one frame with an error still gets its bar.</summary>
    [Test]
    public void ErrorBars_ZoomedOut_KeepASingleSpike()
    {
      var frames = Steady(36000, spike: 20000);
      using var plot = new Plot();
      plot.Add.Plottable(new AnimationErrorBarsPlottable(frames, 0, Refresh, TimeSpan.TicksPerMillisecond) { BarColor = ChartTheme.Late });
      plot.Axes.AutoScale();
      var (count, _, bottom) = RedPixels(plot, 400, 200);
      Assert.That(count, Is.GreaterThan(0), "the spike is drawn");
      Assert.That(bottom, Is.GreaterThan(140), "down (shown too late) at its full height, near the bottom of the scale");
    }

    /// <summary>The same for the display time: the one frame held too long is drawn in red.</summary>
    [Test]
    public void DisplaySteps_ZoomedOut_KeepASingleHoldTooLong()
    {
      var frames = Steady(36000, spike: 20000);
      using var plot = new Plot();
      plot.Add.Plottable(new DisplayTimeStepsPlottable(frames, 0, Refresh) { HeldTooLongColor = ChartTheme.Late });
      plot.Axes.AutoScale();
      var (count, _, _) = RedPixels(plot, 400, 200);
      Assert.That(count, Is.GreaterThan(0));
    }

    /// <summary>Steady frames, one per refresh, except that frame <paramref name="spike"/> comes a refresh late: held, off by 10 ms.</summary>
    private static PresentedFrame[] Steady(int count, int spike)
    {
      var frames = new List<PresentedFrame> { Frame(0, 0, null) };
      long time = 0;
      for (int i = 1; i < count; ++i)
      {
        bool late = i == spike;
        long display = late ? 2 * Refresh : Refresh;
        time += display;
        frames.Add(Frame(0, time, display, late, late ? -10 * TimeSpan.TicksPerMillisecond : 0));
      }
      return frames.ToArray();
    }

    private static PresentedFrame Frame(int segment, long firstSeenTicks, long? displayTicks, bool late = false, long errorTicks = 0) =>
      new PresentedFrame(
        segment,
        0,
        0,
        0,
        firstSeenTicks,
        firstSeenTicks,
        1,
        Refresh,
        0,
        displayTicks,
        displayTicks,
        displayTicks.HasValue ? errorTicks : null,
        0,
        late ? PresentedFrameFlags.Late : PresentedFrameFlags.None
      );

    /// <summary>How many pixels have the late colour, and the topmost and bottommost of them.</summary>
    private static (int Count, int Top, int Bottom) RedPixels(Plot plot, int width, int height)
    {
      using var bitmap = SKBitmap.Decode(plot.GetImage(width, height).GetImageBytes());
      var red = ChartTheme.Late.ToSKColor();
      int count = 0;
      int top = int.MaxValue;
      int bottom = -1;
      for (int y = 0; y < bitmap.Height; ++y)
      {
        for (int x = 0; x < bitmap.Width; ++x)
        {
          if (bitmap.GetPixel(x, y) == red)
          {
            ++count;
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
          }
        }
      }
      return (count, top, bottom);
    }
  }
}
