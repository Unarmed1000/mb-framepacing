//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL camera captures: which grid of refreshes the first-seen times are on (RefreshEstimator.GridPeriodTicks, GridFit). A
//* camera that sees a refresh in two or three frames, with sightings that come a camera frame late, gives intervals whose clusters run
//* into each other; the times themselves still say the refresh, unless half of them are late at exactly twice the refresh rate.
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
  public class RefreshGridTests
  {
    /// <summary>The share of sightings a camera frame late that the synthetic camera gives at twice the refresh rate.</summary>
    private const double LateShare = 0.22;

    /// <summary>
    /// When a camera first sees each frame: at its first frame after the marker has changed, and for a share of the frames one camera
    /// frame later (the marker was changing in that first frame, so it did not decode). Every 37th frame is held a second refresh and
    /// every 53rd is never shown, as in the synthetic camera's scenario; <paramref name="refreshesPerFrame"/> is the game's rate.
    /// </summary>
    private static List<TickCount64> Sightings(double refreshHz, double cameraFps, int frames, double lateShare, int seed, int refreshesPerFrame = 1)
    {
      double refresh = TimeSpan.TicksPerSecond / refreshHz;
      double camera = TimeSpan.TicksPerSecond / cameraFps;
      var random = new Random(seed);
      double phase = 0.3 * camera;
      var seen = new List<TickCount64>();
      long shownOn = 0;
      for (int frame = 0; frame < frames; ++frame)
      {
        shownOn += refreshesPerFrame * (frame % 37 == 36 ? 2 : 1);
        if (frame % 53 == 52)
          continue;
        double first = Math.Ceiling(((shownOn * refresh) + phase) / camera) + (random.NextDouble() < lateShare ? 1 : 0);
        seen.Add(new TickCount64((long)Math.Round(first * camera)));
      }
      return seen;
    }

    private static List<double> Intervals(List<TickCount64> seen)
    {
      var intervals = new List<double>();
      for (int i = 1; i < seen.Count; ++i)
        intervals.Add((seen[i] - seen[i - 1]).Ticks);
      return intervals;
    }

    private static double Estimate(List<TickCount64> seen, double cameraFps) =>
      RefreshEstimator.EstimatePeriodTicks(Intervals(seen), TimeSpan.TicksPerSecond / cameraFps)
      ?? throw new InvalidOperationException("no estimate");

    private static double Hz(double periodTicks) => TimeSpan.TicksPerSecond / periodTicks;

    /// <summary>The period the analysis goes on with: the grid's, else the intervals' estimate.</summary>
    private static double Settle(List<TickCount64> seen, double estimate, List<double> intervals, double camera, double? expected = null) =>
      RefreshEstimator.GridPeriodTicks(seen, estimate, intervals, camera, expected) ?? estimate;

    /// <summary>
    /// A camera at exactly twice the refresh rate, on the refresh rates monitors have: the intervals are 1, 2 and 3 camera periods, and
    /// their clusters give a rate well above the display's. The times are on the refresh's grid, and the search of them finds the
    /// refresh, so the settled period is the display's, with the expected rate and without one.
    /// </summary>
    [TestCase(50)]
    [TestCase(60)]
    [TestCase(75)]
    [TestCase(90)]
    [TestCase(100)]
    [TestCase(120)]
    [TestCase(144)]
    [TestCase(160)]
    [TestCase(165)]
    [TestCase(240)]
    [TestCase(360)]
    [TestCase(500)]
    public void AtTwiceTheRefreshRate_TheIntervalsMisleadAndTheTimesGiveTheRefresh(double refreshHz)
    {
      double cameraFps = 2 * refreshHz;
      double camera = TimeSpan.TicksPerSecond / cameraFps;
      var seen = Sightings(refreshHz, cameraFps, 300, LateShare, 1);
      var intervals = Intervals(seen);

      double estimate = Estimate(seen, cameraFps);
      double alone = Settle(seen, estimate, intervals, camera);
      double withExpected = Settle(seen, estimate, intervals, camera, TimeSpan.TicksPerSecond / refreshHz);

      Assert.That(Hz(estimate), Is.GreaterThan(1.1 * refreshHz), "the intervals' estimate (the failure this test is about)");
      Assert.That(Hz(alone), Is.EqualTo(refreshHz).Within(0.001 * refreshHz), "settled by the search of the times");
      Assert.That(Hz(withExpected), Is.EqualTo(refreshHz).Within(0.001 * refreshHz), "settled by the expected rate");
      Assert.That(Hz(RefreshEstimator.RefinePeriodTicks(seen, alone)), Is.EqualTo(refreshHz).Within(0.01 * refreshHz), "and measured");
    }

    /// <summary>
    /// Cameras that see a refresh in two or three frames, with late sightings: the intervals' estimate is a few percent off or on
    /// another period altogether. The settled period is the refresh, alone, with the display's rate expected and with a wrong one.
    /// </summary>
    [TestCase(60, 100, 0.1)]
    [TestCase(60, 121, 0.1)]
    [TestCase(60, 130, 0.1)]
    [TestCase(60, 150, 0.2)]
    [TestCase(50, 110, 0.1)]
    [TestCase(144, 300, 0.2)]
    [TestCase(160, 330, 0.2)]
    [TestCase(240, 500, 0.2)]
    [TestCase(500, 1100, 0.2)]
    public void FewCameraFramesPerRefresh_TheSettledPeriodIsTheRefresh(double refreshHz, double cameraFps, double lateShare)
    {
      double camera = TimeSpan.TicksPerSecond / cameraFps;
      double refresh = TimeSpan.TicksPerSecond / refreshHz;
      var seen = Sightings(refreshHz, cameraFps, 300, lateShare, 2);
      var intervals = Intervals(seen);
      double estimate = Estimate(seen, cameraFps);
      TestContext.Out.WriteLine($"{refreshHz} Hz at {cameraFps} fps: the intervals give {Hz(estimate):0.00} Hz");

      double alone = Settle(seen, estimate, intervals, camera);
      double expected = Settle(seen, estimate, intervals, camera, refresh);
      double wronglyExpected = Settle(seen, estimate, intervals, camera, refresh * 1.2);

      Assert.That(Hz(alone), Is.EqualTo(refreshHz).Within(0.005 * refreshHz), "alone");
      Assert.That(Hz(expected), Is.EqualTo(refreshHz).Within(0.005 * refreshHz), "with the display's rate expected");
      Assert.That(Hz(wronglyExpected), Is.EqualTo(refreshHz).Within(0.005 * refreshHz), "with another rate expected");
    }

    /// <summary>
    /// Where the times are on the grid of the intervals' estimate, it stays exactly as estimated, whatever rate is expected: cameras
    /// from three times the refresh rate up, on several displays, with a tenth of the sightings late.
    /// </summary>
    [TestCase(60, 180)]
    [TestCase(60, 250)]
    [TestCase(60, 1000)]
    [TestCase(50, 240)]
    [TestCase(144, 480)]
    [TestCase(144, 1000)]
    [TestCase(240, 1000)]
    [TestCase(500, 2000)]
    public void ARightEstimate_StaysAsItIs(double refreshHz, double cameraFps)
    {
      double camera = TimeSpan.TicksPerSecond / cameraFps;
      var seen = Sightings(refreshHz, cameraFps, 300, 0.1, 2);
      var intervals = Intervals(seen);
      double estimate = Estimate(seen, cameraFps);
      Assert.That(Hz(estimate), Is.EqualTo(refreshHz).Within(0.03 * refreshHz), "the intervals' estimate");

      Assert.That(Settle(seen, estimate, intervals, camera), Is.EqualTo(estimate));
      Assert.That(Settle(seen, estimate, intervals, camera, estimate * 1.2), Is.EqualTo(estimate), "a wrong expected rate");
    }

    /// <summary>
    /// The refresh rates monitors have, filmed from 1.7 to 16.7 times as fast, with no and with a fifth of the sightings late, by a
    /// game at full and at half rate: the settled period is the game's frame time (the refresh at full rate) alone and
    /// with another rate expected, the refresh with the display's rate expected, and the line through the times stays on it.
    /// </summary>
    [Test]
    public void MonitorRefreshRates_AtEveryCameraRate_SettleOnTheRefresh()
    {
      double[] refreshRates = { 50, 60, 75, 90, 100, 120, 144, 160, 165, 240, 360, 500 };
      double[] cameraMultiples = { 1.7, 2.0, 2.02, 2.17, 2.5, 3.0, 3.3, 4.17, 8.3, 16.7 };
      double[] lateShares = { 0, LateShare };
      const int Runs = 2;
      var wrong = new List<string>();
      int misledIntervals = 0;
      foreach (double refreshHz in refreshRates)
      {
        foreach (double multiple in cameraMultiples)
        {
          foreach (double late in lateShares)
          {
            for (int run = 0; run < Runs; ++run)
            {
              int refreshesPerFrame = 1 + (run % 2);
              double cameraFps = multiple * refreshHz;
              double camera = TimeSpan.TicksPerSecond / cameraFps;
              double refresh = TimeSpan.TicksPerSecond / refreshHz;
              double frameTime = refresh * refreshesPerFrame;
              var seen = Sightings(refreshHz, cameraFps, 300, late, run, refreshesPerFrame);
              var intervals = Intervals(seen);
              double estimate = Estimate(seen, cameraFps);

              double alone = Settle(seen, estimate, intervals, camera);
              double expected = Settle(seen, estimate, intervals, camera, refresh);
              double wronglyExpected = Settle(seen, estimate, intervals, camera, frameTime * 1.2);
              double measured = RefreshEstimator.RefinePeriodTicks(seen, alone);

              string scenario = $"{refreshHz} Hz at {cameraFps:0} fps, {late:0.##} late, {refreshesPerFrame} refreshes per frame, run {run}";
              misledIntervals += Math.Abs(estimate - frameTime) > 0.005 * frameTime ? 1 : 0;
              if (Math.Abs(alone - frameTime) > 0.005 * frameTime)
                wrong.Add($"{scenario}: {Hz(alone):0.00} Hz alone (the intervals: {Hz(estimate):0.00} Hz)");
              if (Math.Abs(expected - refresh) > 0.005 * refresh)
                wrong.Add($"{scenario}: {Hz(expected):0.00} Hz with the display's rate expected");
              if (Math.Abs(wronglyExpected - frameTime) > 0.005 * frameTime)
                wrong.Add($"{scenario}: {Hz(wronglyExpected):0.00} Hz with another rate expected");
              if (Math.Abs(measured - frameTime) > 0.005 * frameTime)
                wrong.Add($"{scenario}: {Hz(measured):0.00} Hz by the line");
            }
          }
        }
      }
      TestContext.Out.WriteLine(
        $"The intervals alone were off in {misledIntervals} of {refreshRates.Length * cameraMultiples.Length * lateShares.Length * Runs} runs"
      );

      Assert.That(wrong, Is.Empty);
      Assert.That(misledIntervals, Is.GreaterThan(50), "the runs this test is about: where the intervals mislead");
    }

    /// <summary>An expected rate the times are not on is not taken: the period the search finds is.</summary>
    [Test]
    public void AWrongExpectedRate_IsNotTaken()
    {
      const double CameraFps = 120;
      double camera = TimeSpan.TicksPerSecond / CameraFps;
      var seen = Sightings(60, CameraFps, 300, LateShare, 3);
      var intervals = Intervals(seen);

      double settled = Settle(seen, Estimate(seen, CameraFps), intervals, camera, TimeSpan.TicksPerSecond / 75.0);

      Assert.That(Hz(settled), Is.EqualTo(60).Within(0.06));
    }

    /// <summary>A steady game at half the refresh rate: the intervals give its frame time, the times are on that grid, and it stays.</summary>
    [Test]
    public void ASteadyGameAtHalfRate_StaysAsEstimated()
    {
      const double CameraFps = 1000;
      double camera = TimeSpan.TicksPerSecond / CameraFps;
      var seen = Sightings(60, CameraFps, 300, 0, 4, refreshesPerFrame: 2);
      var intervals = Intervals(seen);
      double estimate = Estimate(seen, CameraFps);
      Assert.That(Hz(estimate), Is.EqualTo(30).Within(0.3), "the intervals' estimate");

      Assert.That(Settle(seen, estimate, intervals, camera), Is.EqualTo(estimate));
      // The times of a game at half rate are on the refresh's grid too: the expected rate says which it is, as it does for the intervals
      double refresh = TimeSpan.TicksPerSecond / 60.0;
      Assert.That(Settle(seen, estimate, intervals, camera, refresh), Is.EqualTo(refresh));
    }

    /// <summary>Fewer times than a stretch say nothing: the estimate stays, right or wrong.</summary>
    [Test]
    public void TooFewTimes_SayNothing()
    {
      const double CameraFps = 120;
      double camera = TimeSpan.TicksPerSecond / CameraFps;
      var seen = Sightings(60, CameraFps, 300, LateShare, 1).Take(RefreshEstimator.MinGridTimes - 1).ToList();
      var intervals = Intervals(seen);
      double estimate = Estimate(seen, CameraFps);

      Assert.That(RefreshEstimator.GridPeriodTicks(seen, estimate, intervals, camera, TimeSpan.TicksPerSecond / 60.0), Is.Null);
    }

    /// <summary>Times on no grid at all (vsync off): nothing fits, with a rate expected or without.</summary>
    [Test]
    public void TimesOnNoGrid_GiveNoPeriod()
    {
      var random = new Random(5);
      var seen = new List<TickCount64>();
      long time = 0;
      for (int i = 0; i < 300; ++i)
      {
        time += 100_000 + random.Next(150_000);
        seen.Add(new TickCount64(time));
      }
      var intervals = Intervals(seen);

      Assert.That(RefreshEstimator.GridPeriodTicks(seen, 166_667, intervals, 10_000), Is.Null);
      Assert.That(RefreshEstimator.GridPeriodTicks(seen, 166_667, intervals, 10_000, 200_000), Is.Null);
    }

    /// <summary>
    /// The limit: a camera at exactly twice the refresh rate with half of the sightings a camera frame late. Every time is then on one
    /// of two camera frames half a refresh apart, as often on the one as on the other: all that is left is the camera's own grid, and
    /// no period is given, with the display's rate expected or without.
    /// </summary>
    [TestCase(60)]
    [TestCase(75)]
    [TestCase(144)]
    [TestCase(240)]
    public void HalfTheSightingsLate_AtTwiceTheRefreshRate_GiveNoPeriod(double refreshHz)
    {
      double cameraFps = 2 * refreshHz;
      double camera = TimeSpan.TicksPerSecond / cameraFps;
      var seen = Sightings(refreshHz, cameraFps, 600, 0.5, 8);
      var intervals = Intervals(seen);
      double estimate = Estimate(seen, cameraFps);

      Assert.That(RefreshEstimator.GridPeriodTicks(seen, estimate, intervals, camera), Is.Null);
      Assert.That(RefreshEstimator.GridPeriodTicks(seen, estimate, intervals, camera, TimeSpan.TicksPerSecond / refreshHz), Is.Null);
    }

    /// <summary>Values that are not a period or a camera period change nothing.</summary>
    [Test]
    public void NoPeriodOrCameraPeriod_GivesNoPeriod()
    {
      var seen = Sightings(60, 120, 300, LateShare, 1);
      var intervals = Intervals(seen);

      Assert.That(RefreshEstimator.GridPeriodTicks(seen, 0, intervals, 83_333), Is.Null);
      Assert.That(RefreshEstimator.GridPeriodTicks(seen, double.NaN, intervals, 83_333), Is.Null);
      Assert.That(RefreshEstimator.GridPeriodTicks(seen, 139_000, intervals, 0), Is.Null);
      Assert.That(RefreshEstimator.GridPeriodTicks(seen, 139_000, new List<double>(), 83_333), Is.Null, "no intervals");
    }

    /// <summary>
    /// The fit: 1 on the grid, about 0.1 off it, and nearly unchanged for a period two thousandths off however long the run is (the
    /// fit is taken over stretches of times, so the period need not be exact to be recognised).
    /// </summary>
    [Test]
    public void GridFit_IsHighOnTheGridAndLowOffIt()
    {
      const double Refresh = TimeSpan.TicksPerSecond / 60.0;
      var times = Enumerable.Range(0, 10_000).Select(i => (long)Math.Round(i * Refresh)).ToArray();

      Assert.That(RefreshEstimator.GridFit(times, Refresh), Is.GreaterThan(0.999));
      Assert.That(RefreshEstimator.GridFit(times, Refresh * 1.002), Is.GreaterThan(0.95), "a period that is a little off");
      Assert.That(RefreshEstimator.GridFit(times, Refresh * 1.37), Is.LessThan(0.2), "another period");
      Assert.That(RefreshEstimator.GridFit(times, 0), Is.Zero);
      Assert.That(RefreshEstimator.GridFit(times.Take(RefreshEstimator.MinGridTimes - 1).ToArray(), Refresh), Is.Zero, "too few times");
    }

    /// <summary>The camera's quantisation costs a little: at twice the refresh rate the true period still fits with about 0.6.</summary>
    [TestCase(121, 0.5)]
    [TestCase(250, 0.8)]
    [TestCase(1000, 0.95)]
    public void GridFit_OfTheTruePeriod_IsWellAboveTheLimit(double cameraFps, double atLeast)
    {
      var times = Sightings(60, cameraFps, 600, 0, 6).Select(time => time.Ticks).ToArray();

      Assert.That(RefreshEstimator.GridFit(times, TimeSpan.TicksPerSecond / 60.0), Is.GreaterThan(atLeast));
      Assert.That(atLeast, Is.GreaterThan(RefreshEstimator.MinGridFit(times.Length)));
    }

    /// <summary>The limit follows how many stretches of times there are: fewer stretches say less.</summary>
    [Test]
    public void MinGridFit_FallsWithTheNumberOfStretches()
    {
      Assert.That(RefreshEstimator.MinGridFit(RefreshEstimator.MinGridTimes), Is.EqualTo(0.40).Within(0.005));
      Assert.That(RefreshEstimator.MinGridFit((2 * RefreshEstimator.MinGridTimes) - 1), Is.EqualTo(0.40).Within(0.005), "still one stretch");
      Assert.That(RefreshEstimator.MinGridFit(4 * RefreshEstimator.MinGridTimes), Is.EqualTo(0.256).Within(0.005));
      Assert.That(RefreshEstimator.MinGridFit(16 * RefreshEstimator.MinGridTimes), Is.EqualTo(0.183).Within(0.005));
      Assert.That(RefreshEstimator.MinGridFit(0), Is.EqualTo(RefreshEstimator.MinGridFit(RefreshEstimator.MinGridTimes)));
    }
  }
}
