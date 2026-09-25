//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The EXPERIMENTAL camera rules of the timeline on hand-made rows: scanout delay, camera tears, frames only the second zone saw, and
//* uncertain starts only for gaps longer than the usual scanout transition.
//*
//* (c) 2026 Mana Battery
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
    /// zone 6 captures before the timing zone, <paramref name="secondZoneOnly"/> is never seen by the timing zone.
    /// </summary>
    private static List<CaptureRow> Rows(int frames, int longGapFrame, int tornFrame, int secondZoneOnly)
    {
      var primary = new Dictionary<long, ulong>();
      var secondary = new Dictionary<long, ulong>();
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
          secondary[c] = index;
      }

      var rows = new List<CaptureRow>();
      long end = (frames + 1) * CapturesPerFrame;
      for (long c = CapturesPerFrame; c < end; ++c)
      {
        ulong? second = secondary.TryGetValue(c, out var s) ? s : null;
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
  }
}
