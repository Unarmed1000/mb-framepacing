//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The statistics of a run come from its values' ticks, sorted once with a radix sort (TickSort, TickList) instead of an array of doubles
//* sorted per statistic: the sort is the numbers' order, and every statistic is to the bit what the formulas over sorted milliseconds
//* give, which this file keeps as the reference.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  public class StatisticsTests
  {
    private const long Refresh = TimeSpan.TicksPerSecond / 240;

    [Test]
    public void TickSort_IsTheNumbersOrder()
    {
      var random = new Random(5);
      // Below and above where the radix sort starts, and values that differ in one byte, in every byte, or not at all
      foreach (int count in new[] { 0, 1, 2, 255, 256, 257, 1000, 70_000 })
      {
        foreach (int kind in Enumerable.Range(0, 6))
        {
          var values = new long[count];
          for (int i = 0; i < count; ++i)
            values[i] = kind switch
            {
              0 => random.Next(0, 200),
              1 => random.Next(-5000, 5000),
              2 => random.NextInt64(long.MinValue, long.MaxValue),
              3 => 41_666 * (long)random.Next(1, 4),
              4 => i % 2 == 0 ? long.MinValue : long.MaxValue,
              _ => -7,
            };
          var expected = (long[])values.Clone();
          Array.Sort(expected);

          var withScratch = (long[])values.Clone();
          TickSort.Sort(values);
          TickSort.Sort(withScratch, new long[count + 3]);

          Assert.That(values, Is.EqualTo(expected), $"{count} values of kind {kind}");
          Assert.That(withScratch, Is.EqualTo(expected), $"{count} values of kind {kind}, with the caller's scratch array");
        }
      }
    }

    [Test]
    public void TickSort_AScratchArrayTooShort_IsRefused()
    {
      Assert.Throws<ArgumentException>(() => TickSort.Sort(new long[1000], new long[999]));
      Assert.DoesNotThrow(() => TickSort.Sort(new long[10], Span<long>.Empty), "few values need none");
    }

    [Test]
    public void TickList_GrowsAndSorts()
    {
      var list = new TickList();
      for (int i = 5000; i > 0; --i)
        list.Add(i % 2 == 0 ? i : -i);
      list.Add(new TimeSpan(0));
      list.Sort();

      Assert.That(list.Count, Is.EqualTo(5001));
      Assert.That(list.Values.ToArray(), Is.Ordered.Ascending);
      Assert.That((list.Values[0], list.Values[^1]), Is.EqualTo((-4999L, 5000L)));
    }

    [Test]
    public void StatisticsOfSpans_AreTheStatisticsOfTheirMilliseconds_ToTheBit()
    {
      var random = new Random(9);
      foreach (int count in new[] { 0, 1, 2, 3, 100, 999, 1000, 1001, 50_000 })
      {
        // A run's values: a few steps many times over, and values all over the place
        var spans = Enumerable
          .Range(0, count)
          .Select(i => new TimeSpan(i % 3 == 0 ? random.NextInt64(-400_000, 400_000) : Refresh * random.Next(1, 4)))
          .ToList();

        Assert.That(Statistics.From(spans), Is.EqualTo(Statistics.From(spans.Select(s => s.TotalMilliseconds))), $"{count} values");
      }
    }

    [Test]
    public void RunStatistics_AreTheReferenceFormulas_ToTheBit()
    {
      var threshold = TimeSpan.FromMilliseconds(1);
      foreach (int count in new[] { 0, 1, 2, 99, 100, 999, 1000, 20_000 })
      {
        var frames = Frames(count, new Random(count + 1));
        foreach (var period in new[] { new TimeSpan(Refresh), TimeSpan.Zero })
          Assert.That(RunStatistics.From(frames, threshold, period), Is.EqualTo(Reference(frames, threshold, period)), $"{count} frames");
      }
    }

    /// <summary>Frames with every kind of value the statistics read, some of each missing: static and uncertain steps, no CPU values, no error.</summary>
    private static List<PresentedFrame> Frames(int count, Random random)
    {
      var frames = new List<PresentedFrame>(count);
      long time = 0;
      for (int i = 0; i < count; ++i)
      {
        long display = Refresh * random.Next(1, 4);
        long error = random.Next(5) == 0 ? random.Next(-30_000, 30_000) : 0;
        bool first = i == 0;
        bool judged = !first && random.Next(20) != 0;
        var flags =
          (random.Next(31) == 0 ? PresentedFrameFlags.StaticBefore : PresentedFrameFlags.None)
          | (random.Next(37) == 0 ? PresentedFrameFlags.UncertainStep : PresentedFrameFlags.None)
          | (random.Next(11) == 0 ? PresentedFrameFlags.Late : PresentedFrameFlags.None);
        time += display;
        frames.Add(
          new PresentedFrame(
            0,
            (ulong)i,
            new TimeSpan(time),
            i,
            new TickCount64(time),
            new TickCount64(time),
            1,
            new TimeSpan(Refresh * random.Next(1, 3)),
            0,
            first ? null : new TimeSpan(display),
            judged ? new TimeSpan(display + error) : null,
            judged ? new TimeSpan(error) : null,
            new TimeSpan(random.Next(-9000, 9000)),
            flags,
            CpuBusy: random.Next(4) == 0 ? default : new TimeSpan32((uint)random.Next(1, 40_000)),
            FrameTime: random.Next(6) == 0 ? null : new TimeSpan(display + random.Next(-500, 500)),
            CpuWait: random.Next(3) == 0 ? null : new TimeSpan(random.Next(0, 30_000))
          )
        );
      }
      return frames;
    }

    /// <summary>The statistics as they were computed before: a list and a sorted array of milliseconds per statistic.</summary>
    private static RunStatistics Reference(IReadOnlyList<PresentedFrame> frames, TimeSpan threshold, TimeSpan capturePeriod)
    {
      static Statistics Of(IEnumerable<TimeSpan> spans) => Statistics.From(spans.Select(s => s.TotalMilliseconds));
      var withMetrics = frames.Where(f => f.AnimationError.HasValue).ToList();
      var frameRate = frames.Where(RunStatistics.CountsTowardFrameRate).ToList();
      var (errorPerFrameMs, percentError) = RunStatistics.ErrorSummary(
        withMetrics.Select(f => (f.AnimationError!.Value, f.DisplayDelta!.Value)).ToList()
      );
      long frameRateTicks = frameRate.Sum(f => f.DisplayDelta!.Value.Ticks);
      double? LowFps(double fraction, int minFrames)
      {
        if (frameRate.Count < minFrames)
          return null;
        var steps = frameRate.Select(f => f.DisplayDelta!.Value).OrderBy(t => t).ToArray();
        var step = steps[Math.Max(0, (int)Math.Ceiling(fraction * steps.Length) - 1)];
        return step > TimeSpan.Zero ? TimeSpan.TicksPerSecond / (double)step.Ticks : null;
      }
      return new RunStatistics(
        Of(frameRate.Select(f => f.DisplayDelta!.Value)),
        Of(withMetrics.Select(f => f.AnimationDelta!.Value)),
        Of(withMetrics.Select(f => f.AnimationError!.Value)),
        Of(withMetrics.Select(f => f.AnimationError!.Value.Duration())),
        Of(frames.Select(f => f.Drift)),
        Of(frames.Select(f => f.OnScreen)),
        withMetrics.LongCount(f => capturePeriod > TimeSpan.Zero && f.AnimationError!.Value.Duration() > threshold),
        errorPerFrameMs,
        percentError,
        frameRateTicks > 0 ? frameRate.Count * (double)TimeSpan.TicksPerSecond / frameRateTicks : 0,
        LowFps(0.99, RunStatistics.MinFramesForOnePercentLow),
        LowFps(0.999, RunStatistics.MinFramesForPointOnePercentLow),
        Of(frames.Where(f => f.CpuBusy != TimeSpan32.Zero).Select(f => f.CpuBusy.ToTimeSpan())),
        Of(frames.Where(f => f.FrameTime.HasValue).Select(f => f.FrameTime!.Value)),
        Of(frames.Where(f => f.CpuWait.HasValue).Select(f => f.CpuWait!.Value)),
        frames.LongCount(f => f.DisplayDelta.HasValue && (f.Flags & PresentedFrameFlags.StaticBefore) != 0),
        frames.LongCount(f =>
          f.DisplayDelta.HasValue
          && (f.Flags & (PresentedFrameFlags.UncertainStep | PresentedFrameFlags.StaticBefore)) == PresentedFrameFlags.UncertainStep
        )
      );
    }
  }
}
