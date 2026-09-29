//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The EXPERIMENTAL camera rules of the timeline on hand-made rows: scanout delay, camera tears, frames only the second zone saw, and
//* uncertain starts only for gaps longer than the usual scanout transition.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  public class CameraTimelineTests
  {
    private const long Period = TimeSpan.TicksPerMillisecond; // 1000 fps camera
    private const int CapturesPerFrame = 17;
    private const int Transition = 4; // undecodable captures while the scanout crosses the timing marker
    private const int SecondZoneDelay = 10; // captures

    /// <summary>
    /// Rows of a camera capture: frame k is first decoded at capture k * 17 after 4 undecodable captures; the second zone shows each frame 10
    /// captures after the timing zone. <paramref name="longGapFrame"/> gets a 12 capture gap, <paramref name="tornFrame"/> reaches the second
    /// zone 6 captures before the timing zone, <paramref name="secondZoneOnly"/> is never seen by the timing zone. The run id is 1; the second
    /// zone shows <paramref name="otherRunFrame"/> with run id 2, as another run's frame of the same index.
    /// </summary>
    private static List<CaptureRow> Rows(int frames, int longGapFrame, int tornFrame, int secondZoneOnly, int otherRunFrame = -1)
    {
      var primary = new Dictionary<long, ulong>();
      var secondary = new Dictionary<long, (ulong Index, uint RunId)>();
      for (int k = 0; k < frames; ++k)
      {
        long first = (k + 1) * CapturesPerFrame;
        ulong index = (ulong)(100 + k);
        if (k != secondZoneOnly)
        {
          for (long c = first; c < first + CapturesPerFrame - (k + 1 == longGapFrame ? 12 : Transition); ++c)
            primary[c] = index;
        }
        long secondFirst = k == tornFrame ? first - 6 : first + SecondZoneDelay;
        // A newer frame replaces the older one in the second zone
        for (long c = secondFirst; c < secondFirst + CapturesPerFrame - Transition; ++c)
          secondary[c] = (index, k == otherRunFrame ? 2u : 1u);
      }

      var rows = new List<CaptureRow>();
      long end = (frames + 1) * CapturesPerFrame;
      for (long c = CapturesPerFrame; c < end; ++c)
      {
        MarkerPayload? second = secondary.TryGetValue(c, out var s) ? new MarkerPayload(s.Index, 0, s.RunId, MarkerKind.Sync) : null;
        rows.Add(
          primary.TryGetValue(c, out var p)
            ? new CaptureRow(c, c * Period, CaptureStatus.Decoded, new MarkerPayload(p, (long)(p - 100) * 16 * Period, 1), null, false, second)
            : new CaptureRow(c, c * Period, CaptureStatus.Undecodable, default, null, false, second)
        );
      }
      return rows;
    }

    private static RunAnalysis Analyze(List<CaptureRow> rows) =>
      TimelineAnalyzer.Analyze(rows, new TimelineOptions { Scanout = ScanoutModel.Camera }).Runs.Single();

    [Test]
    public void ScanoutDelay_IsTheSecondZoneLag()
    {
      var run = Analyze(Rows(30, longGapFrame: -1, tornFrame: -1, secondZoneOnly: -1));

      Assert.That(run.Camera, Is.Not.Null);
      Assert.That(run.Camera!.ScanoutDelay.P50, Is.EqualTo(SecondZoneDelay).Within(0.01));
      Assert.That(run.Camera.TornFrames, Is.EqualTo(0));
      Assert.That(run.Camera.SecondZoneOnlyFrames, Is.EqualTo(0));
      Assert.That(run.Frames.Where(f => f.Flags.HasFlag(PresentedFrameFlags.UncertainStart)), Is.Empty, "the usual transition is not uncertain");
    }

    [Test]
    public void Refresh_IsCalculatedFromTheFrames()
    {
      var rows = Rows(30, longGapFrame: -1, tornFrame: -1, secondZoneOnly: -1);
      var run = Analyze(rows);

      Assert.That(run.Pacing, Is.Not.Null);
      Assert.That(run.Pacing!.RefreshCalculated);
      Assert.That(run.Pacing.RefreshPeriodMs, Is.EqualTo(CapturesPerFrame).Within(0.01), "one frame per refresh, 17 camera periods apart");
      Assert.That(run.Pacing.LateFrames, Is.Zero);

      var mismatch = TimelineAnalyzer.Analyze(rows, new TimelineOptions { Scanout = ScanoutModel.Camera, CalibratedRefreshHz = 60 }).Runs.Single();
      Assert.That(mismatch.Warnings, Has.Some.Contains("the camera rig measured 60 Hz"));
    }

    [Test]
    public void ExpectedDisplayRate_IsComparedWithTheCalculatedOne()
    {
      var rows = Rows(30, longGapFrame: -1, tornFrame: -1, secondZoneOnly: -1);
      RunAnalysis Expecting(double hz) =>
        TimelineAnalyzer.Analyze(rows, new TimelineOptions { Scanout = ScanoutModel.Camera, ExpectedRefreshHz = hz }).Runs.Single();

      // One frame every 17 ms: a 58.8 Hz display
      var matching = Expecting(1000.0 / CapturesPerFrame);
      Assert.That(matching.Pacing!.MatchesExpectedRefresh, Is.True);
      Assert.That(matching.Pacing.RefreshDeviation, Is.EqualTo(0).Within(0.001));
      Assert.That(matching.Warnings, Has.None.Contains("was expected"));

      var wrong = Expecting(60);
      Assert.That(wrong.Pacing!.MatchesExpectedRefresh, Is.False);
      Assert.That(wrong.Pacing.ExpectedRefreshHz, Is.EqualTo(60));
      Assert.That(wrong.Warnings, Has.Some.Contains("58.82 Hz display, but 60 Hz was expected"));
    }

    [Test]
    public void ExpectedDisplayRate_SettlesASteadyGameBelowTheRefreshRate()
    {
      // Every frame is 17 camera periods apart; told the display runs twice as fast, the game is at half rate
      var rows = Rows(30, longGapFrame: -1, tornFrame: -1, secondZoneOnly: -1);
      var run = TimelineAnalyzer
        .Analyze(rows, new TimelineOptions { Scanout = ScanoutModel.Camera, ExpectedRefreshHz = 2000.0 / CapturesPerFrame })
        .Runs.Single();

      Assert.That(run.Pacing!.RefreshPeriodMs, Is.EqualTo(CapturesPerFrame / 2.0).Within(0.01));
      Assert.That(run.Pacing.MatchesExpectedRefresh, Is.True);
      // Without pacing information the target is the native rate, so every frame of the half rate game is a refresh late
      Assert.That(run.Pacing.TargetFrameMs, Is.EqualTo(CapturesPerFrame / 2.0).Within(0.01));
      Assert.That(run.Pacing.LateFrames, Is.EqualTo(run.Frames.Count - 1));
    }

    [Test]
    public void LongGap_IsUncertain()
    {
      var run = Analyze(Rows(30, longGapFrame: 12, tornFrame: -1, secondZoneOnly: -1));

      var uncertain = run.Frames.Where(f => f.Flags.HasFlag(PresentedFrameFlags.UncertainStart)).Select(f => f.FrameIndex).ToList();
      Assert.That(uncertain, Is.EqualTo(new[] { 112UL }));
    }

    [Test]
    public void EarlySecondZone_IsATear_AndSecondZoneOnlyFramesAreCounted()
    {
      var run = Analyze(Rows(30, longGapFrame: -1, tornFrame: 8, secondZoneOnly: 20));

      Assert.That(run.Frames.Where(f => f.Flags.HasFlag(PresentedFrameFlags.Torn)).Select(f => f.FrameIndex), Is.EqualTo(new[] { 108UL }));
      Assert.That(run.Camera!.TornFrames, Is.EqualTo(1));
      Assert.That(run.Camera.SecondZoneOnlyFrames, Is.EqualTo(1));
      Assert.That(run.Frames.Single(f => f.FrameIndex == 121).SkippedBefore, Is.EqualTo(1UL));
    }

    [Test]
    public void SecondZone_OfAnotherRun_IsNotThisRunsFrame()
    {
      // The early second zone of frame 108 belongs to run 2: it neither times frame 108 of run 1 nor makes it a tear
      var run = Analyze(Rows(30, longGapFrame: -1, tornFrame: 8, secondZoneOnly: -1, otherRunFrame: 8));

      Assert.That(run.Frames.Where(f => f.Flags.HasFlag(PresentedFrameFlags.Torn)), Is.Empty);
      Assert.That(run.Camera!.TornFrames, Is.EqualTo(0));
      Assert.That(run.Camera.ScanoutDelay.P50, Is.EqualTo(SecondZoneDelay).Within(0.01));
    }
  }
}
