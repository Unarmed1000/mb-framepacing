//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL camera captures: the refresh period measured with a line through every first-seen time (RefreshEstimator.RefinePeriodTicks),
//* against the average of the intervals it starts from, on simulated sightings: a display's refreshes as a camera sees them, up to a
//* millisecond late and then at its next frame, with frames held longer and frames skipped.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Capture.Camera;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class RefreshRefinementTests
  {
    private const double Refresh = TimeSpan.TicksPerSecond / 60.0;

    /// <summary>
    /// When a camera first sees each frame of a 60 Hz display: at its first frame after the marker has changed, which takes up to a
    /// millisecond longer from one refresh to the next (the scanout crossing the marker, the panel's response, the exposure). A 1000 fps
    /// camera then sees a refresh after 15 to 18 ms, as the synthetic camera does. Every 37th frame is held a second refresh and every
    /// 53rd is never shown, as in the synthetic camera's scenario.
    /// </summary>
    private static List<TickCount64> Sightings(int frames, double cameraFps, int seed, double refresh = Refresh)
    {
      double camera = TimeSpan.TicksPerSecond / cameraFps;
      var random = new Random(seed);
      double phase = random.NextDouble() * camera;
      var seen = new List<TickCount64>();
      long shownOn = 0;
      for (int frame = 0; frame < frames; ++frame)
      {
        shownOn += frame % 37 == 36 ? 2 : 1;
        if (frame % 53 == 52)
          continue;
        double changed = (shownOn * refresh) + phase + (random.NextDouble() * TimeSpan.TicksPerMillisecond);
        double first = Math.Ceiling(changed / camera);
        seen.Add(new TickCount64((long)Math.Round(first * camera)));
      }
      return seen;
    }

    /// <summary>The estimate the line starts from: the average of the intervals of one refresh.</summary>
    private static double AverageInterval(List<TickCount64> seen, double cameraFps)
    {
      var intervals = new List<double>();
      for (int i = 1; i < seen.Count; ++i)
        intervals.Add((seen[i] - seen[i - 1]).Ticks);
      return RefreshEstimator.EstimatePeriodTicks(intervals, TimeSpan.TicksPerSecond / cameraFps)
        ?? throw new InvalidOperationException("no estimate");
    }

    private static double Hz(double periodTicks) => TimeSpan.TicksPerSecond / periodTicks;

    /// <summary>
    /// Two seconds of frames, as the calibration reads, in many runs: the root mean square error of the average of the intervals and of
    /// the line, and the line's worst run, in Hz.
    /// </summary>
    private static (double Average, double Line, double LineWorst) Errors(double cameraFps)
    {
      double averageSquares = 0;
      double lineSquares = 0;
      double worst = 0;
      const int Runs = 200;
      for (int seed = 0; seed < Runs; ++seed)
      {
        var seen = Sightings(120, cameraFps, seed);
        double average = AverageInterval(seen, cameraFps);
        double line = RefreshEstimator.RefinePeriodTicks(seen, average);
        averageSquares += Math.Pow(Hz(average) - 60, 2);
        lineSquares += Math.Pow(Hz(line) - 60, 2);
        worst = Math.Max(worst, Math.Abs(Hz(line) - 60));
      }
      var errors = (Average: Math.Sqrt(averageSquares / Runs), Line: Math.Sqrt(lineSquares / Runs), LineWorst: worst);
      TestContext.Out.WriteLine(
        $"{cameraFps} fps: average of intervals {errors.Average:0.0000} Hz rms, line {errors.Line:0.0000} Hz rms, line's worst {errors.LineWorst:0.0000} Hz"
      );
      return errors;
    }

    /// <summary>
    /// The line is many times closer to the display's rate than the average of the intervals, at every camera rate from about twice the
    /// refresh rate, and within half of the tenth of a hertz the wizard shows.
    /// </summary>
    [TestCase(130)]
    [TestCase(250)]
    [TestCase(500)]
    [TestCase(1000)]
    [TestCase(2000)]
    public void TwoSeconds_TheLineIsCloserThanTheAverage(double cameraFps)
    {
      var errors = Errors(cameraFps);

      Assert.That(errors.Line, Is.LessThan(errors.Average / 5), "the line's error against the average's");
      Assert.That(errors.LineWorst, Is.LessThan(0.05), "the line's worst run: the tenth of a hertz the wizard shows is right");
    }

    /// <summary>
    /// A camera slower than twice the refresh rate can not tell which refresh every sighting fell on. A line through wrongly numbered
    /// times would be worse than the average; it is not taken when its numbering does not hold, so it never is.
    /// </summary>
    [TestCase(70)]
    [TestCase(80)]
    [TestCase(90)]
    public void SlowerThanTwiceTheRefresh_TheLineIsNotWorseThanTheAverage(double cameraFps)
    {
      var errors = Errors(cameraFps);

      Assert.That(errors.Line, Is.LessThanOrEqualTo(errors.Average));
    }

    /// <summary>A longer run measures better still: the error falls with the run's length times the square root of its sightings.</summary>
    [Test]
    public void AMinute_IsMeasuredToAThousandthOfAHertz()
    {
      var seen = Sightings(3600, 1000, 7);

      double line = RefreshEstimator.RefinePeriodTicks(seen, AverageInterval(seen, 1000));

      Assert.That(Hz(line), Is.EqualTo(60).Within(0.0001));
    }

    /// <summary>The period it starts from only says which refresh a sighting fell on: one that is a little off gives the same line.</summary>
    [TestCase(0.997)]
    [TestCase(1.003)]
    public void AStartThatIsALittleOff_GivesTheSameLine(double factor)
    {
      var seen = Sightings(120, 1000, 3);
      double average = AverageInterval(seen, 1000);

      Assert.That(
        RefreshEstimator.RefinePeriodTicks(seen, average * factor),
        Is.EqualTo(RefreshEstimator.RefinePeriodTicks(seen, average)).Within(1e-6)
      );
    }

    [Test]
    public void TheOrderAndRepeatedTimesDoNotMatter()
    {
      var seen = Sightings(120, 1000, 5);
      double average = AverageInterval(seen, 1000);
      var shuffled = seen.Concat(seen.Take(10)).Reverse().ToList();

      Assert.That(RefreshEstimator.RefinePeriodTicks(shuffled, average), Is.EqualTo(RefreshEstimator.RefinePeriodTicks(seen, average)));
    }

    [Test]
    public void TooFewTimes_KeepThePeriod()
    {
      var seen = Sightings(RefreshEstimator.MinIntervals, 1000, 1);

      Assert.That(seen, Has.Count.EqualTo(RefreshEstimator.MinIntervals));
      Assert.That(RefreshEstimator.RefinePeriodTicks(seen, 166000), Is.EqualTo(166000));
      Assert.That(RefreshEstimator.RefinePeriodTicks(Array.Empty<TickCount64>(), 166000), Is.EqualTo(166000));
    }

    /// <summary>Times on another grid than the period's (a display at another rate than the estimate) are not taken for a refinement of it.</summary>
    [Test]
    public void ALineFarFromThePeriod_KeepsThePeriod()
    {
      var seen = Sightings(120, 1000, 2, refresh: Refresh * 1.03);

      Assert.That(RefreshEstimator.RefinePeriodTicks(seen, Refresh), Is.EqualTo(Refresh));
    }

    [Test]
    public void APeriodThatIsNotOne_IsKept()
    {
      var seen = Sightings(120, 1000, 2);

      Assert.That(RefreshEstimator.RefinePeriodTicks(seen, 0), Is.Zero);
      Assert.That(RefreshEstimator.RefinePeriodTicks(seen, double.NaN), Is.NaN);
    }

    /// <summary>Every sighting at one time has no line through it.</summary>
    [Test]
    public void TimesTooCloseForARefresh_KeepThePeriod()
    {
      var seen = Enumerable.Range(0, 20).Select(i => new TickCount64(1000 + i)).ToList();

      Assert.That(RefreshEstimator.RefinePeriodTicks(seen, Refresh), Is.EqualTo(Refresh));
    }
  }
}
