//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Timeline rules on hand-built capture rows.
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
  public class TimelineAnalyzerTests
  {
    private const long Ms = TimeSpan.TicksPerMillisecond;
    private const long Period = 4 * Ms; // 250 fps capture

    /// <summary>Builds rows at a fixed capture period. Each call appends <paramref name="captures"/> captures showing one marker.</summary>
    private sealed class RowBuilder
    {
      public readonly List<CaptureRow> Rows = new List<CaptureRow>();

      private long Next => Rows.Count;

      public RowBuilder Show(
        ulong frameIndex,
        long animationMs,
        int captures,
        uint runId = 1,
        MarkerKind kind = MarkerKind.Frame,
        StartMetadata? start = null
      )
      {
        for (int i = 0; i < captures; ++i)
          Rows.Add(new CaptureRow(Next, Next * Period, CaptureStatus.Decoded, new MarkerPayload(frameIndex, animationMs * Ms, runId, kind), start));
        return this;
      }

      public RowBuilder Start(uint runId, int captures = 3, string name = "test") =>
        Show(0, 0, captures, runId, MarkerKind.SequenceStart, new StartMetadata(new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc).Ticks, name));

      public RowBuilder End(uint runId, int captures = 3) => Show(999_999, 0, captures, runId, MarkerKind.SequenceEnd);

      public RowBuilder Status(CaptureStatus status, int captures = 1)
      {
        for (int i = 0; i < captures; ++i)
          Rows.Add(new CaptureRow(Next, status == CaptureStatus.NotRecorded ? 0 : Next * Period, status, default));
        return this;
      }
    }

    [Test]
    public void PerfectPacing_HasNoAnimationError()
    {
      // 62.5 Hz game (4 captures per frame), animation advances 16 ms per frame
      var rows = new RowBuilder().Start(1);
      for (ulong f = 0; f < 20; ++f)
        rows.Show(100 + f, (long)f * 16, 4);
      rows.End(1);

      var result = TimelineAnalyzer.Analyze(rows.Rows);

      Assert.That(result.CapturePeriodTicks, Is.EqualTo(Period));
      var run = result.Runs.Single();
      Assert.That(run.Name, Is.EqualTo("test"));
      Assert.That(run.HasStartMarker && run.HasEndMarker);
      Assert.That(run.Counts.PresentedFrames, Is.EqualTo(20));
      Assert.That(run.Frames.Skip(1).All(f => f.AnimationErrorTicks == 0), Is.True);
      Assert.That(run.Statistics.DisplayDeltaMs.Mean, Is.EqualTo(16).Within(1e-9));
      Assert.That(run.Statistics.FramesWithAnimationError, Is.Zero);
      Assert.That(run.Frames[5].OnScreenTicks, Is.EqualTo(16 * Ms));
    }

    [Test]
    public void Stall_ShowsAsNegativeErrorOnTheLateFrame()
    {
      var rows = new RowBuilder().Start(1);
      rows.Show(1, 0, 4).Show(2, 16, 4).Show(3, 32, 8).Show(4, 48, 4).Show(5, 64, 4);
      rows.End(1);

      var frames = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single().Frames;

      // Frame 3 stays on screen for 32 ms, so frame 4 appears 32 ms after frame 3 but its animation advanced only 16 ms
      Assert.That(frames[2].OnScreenTicks, Is.EqualTo(32 * Ms));
      Assert.That(frames[3].DisplayDeltaTicks, Is.EqualTo(32 * Ms));
      Assert.That(frames[3].AnimationErrorTicks, Is.EqualTo(-16 * Ms));
      Assert.That(frames[3].DriftTicks, Is.EqualTo(-16 * Ms));
      Assert.That(frames[4].AnimationErrorTicks, Is.EqualTo(0));
    }

    [Test]
    public void SkippedFrameIndex_IsCounted_AndShowsAsPositiveError()
    {
      var rows = new RowBuilder().Start(1);
      rows.Show(1, 0, 4).Show(2, 16, 4).Show(4, 48, 4).Show(5, 64, 4);
      rows.End(1);

      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();

      Assert.That(run.Counts.SkippedFrameIndices, Is.EqualTo(1));
      Assert.That(run.Frames[2].SkippedBefore, Is.EqualTo(1UL));
      Assert.That(run.Frames[2].Flags.HasFlag(PresentedFrameFlags.SkippedBefore));
      Assert.That(run.Frames[2].AnimationErrorTicks, Is.EqualTo(16 * Ms));
    }

    [Test]
    public void CaptureSlowerThanTheDisplay_IsReported()
    {
      // Every captured frame index is two after the previous one: every second displayed frame was never captured, like a 60 fps
      // recording of a 120 Hz display
      var rows = new RowBuilder().Start(1);
      for (ulong f = 0; f < 40; ++f)
        rows.Show(100 + (2 * f), (long)f * 16, 4);
      rows.End(1);

      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();

      Assert.That(run.Counts.SkippedFrameIndices, Is.EqualTo(39));
      Assert.That(run.Warnings, Has.Some.Contains("slower than the display's refresh rate"));
      Assert.That(run.Warnings, Has.Some.Contains("advances about 125 times per second; the capture records 250 frames per second"));
    }

    [Test]
    public void OccasionalSkips_AreNotReportedAsASlowCapture()
    {
      var rows = new RowBuilder().Start(1);
      for (ulong f = 0; f < 40; ++f)
        rows.Show(f == 20 ? 1000 + f + 1 : 1000 + f + (f > 20 ? 1UL : 0UL), (long)f * 16, 4);
      rows.End(1);

      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();

      Assert.That(run.Counts.SkippedFrameIndices, Is.EqualTo(1));
      Assert.That(run.Warnings, Has.None.Contains("slower than the display's refresh rate"));
    }

    [Test]
    public void OnlyFramesBetweenStartAndEndAreMeasured()
    {
      var rows = new RowBuilder();
      rows.Show(1, 0, 4, runId: 7).Show(2, 16, 4, runId: 7); // before the run
      rows.Start(1).Show(10, 100, 4).Show(11, 116, 4).End(1);
      rows.Show(12, 132, 4); // after the run

      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();

      Assert.That(run.RunId, Is.EqualTo(1));
      Assert.That(run.Frames.Select(f => f.FrameIndex), Is.EqualTo(new ulong[] { 10, 11 }));
    }

    [Test]
    public void SeveralRuns_AreReportedSeparately_AndCanBeSelected()
    {
      var rows = new RowBuilder();
      rows.Start(1, name: "first").Show(10, 0, 4).Show(11, 16, 4).End(1);
      rows.Start(2, name: "second").Show(50, 0, 4, runId: 2).Show(51, 16, 4, runId: 2).Show(52, 32, 4, runId: 2).End(2);

      var all = TimelineAnalyzer.Analyze(rows.Rows);
      Assert.That(all.Runs.Select(r => r.Name), Is.EqualTo(new[] { "first", "second" }));
      Assert.That(all.Runs[1].Counts.PresentedFrames, Is.EqualTo(3));

      var selected = TimelineAnalyzer.Analyze(rows.Rows, new TimelineOptions { RunId = 2 });
      Assert.That(selected.Runs.Single().RunId, Is.EqualTo(2));
    }

    [Test]
    public void MissingEndMarker_IsAWarning()
    {
      var rows = new RowBuilder().Start(1).Show(10, 0, 4).Show(11, 16, 4);
      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();
      Assert.That(run.HasEndMarker, Is.False);
      Assert.That(run.Warnings, Has.Some.Contains("no end marker"));
      Assert.That(run.Counts.PresentedFrames, Is.EqualTo(2));
    }

    [Test]
    public void WithoutStartMarkers_TheWholeCaptureIsOneRun()
    {
      var rows = new RowBuilder().Show(1, 0, 4, runId: 0).Show(2, 16, 4, runId: 0).Show(3, 32, 4, runId: 0);
      var result = TimelineAnalyzer.Analyze(rows.Rows);
      Assert.That(result.Warnings, Has.Some.Contains("No start markers"));
      Assert.That(result.Runs.Single().Counts.PresentedFrames, Is.EqualTo(3));
    }

    [Test]
    public void FrameIndexRestart_StartsANewSegment()
    {
      var rows = new RowBuilder().Start(1).Show(5000, 0, 4).Show(5001, 16, 4).Show(1, 0, 4).Show(2, 16, 4).End(1);
      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();

      Assert.That(run.Counts.Segments, Is.EqualTo(2));
      Assert.That(run.Frames[2].Segment, Is.EqualTo(1));
      Assert.That(run.Frames[2].AnimationErrorTicks, Is.Null, "the first frame of a segment has no predecessor");
      Assert.That(run.Frames[3].AnimationErrorTicks, Is.EqualTo(0));
      Assert.That(run.Warnings, Has.Some.Contains("jumped back"));
    }

    [Test]
    public void SmallBackwardsStep_IsOutOfOrder_NotANewFrame()
    {
      var rows = new RowBuilder().Start(1).Show(10, 0, 4).Show(11, 16, 3).Show(10, 0, 1).Show(12, 32, 4).End(1);
      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();
      Assert.That(run.Counts.OutOfOrderCaptures, Is.EqualTo(1));
      Assert.That(run.Frames.Select(f => f.FrameIndex), Is.EqualTo(new ulong[] { 10, 11, 12 }));
    }

    [Test]
    public void UndecodableAndNotRecordedCaptures_AreCounted_AndFlagTheNextFrame()
    {
      var rows = new RowBuilder().Start(1).Show(10, 0, 4).Status(CaptureStatus.Undecodable).Status(CaptureStatus.NotRecorded).Show(11, 16, 2);
      rows.Status(CaptureStatus.Torn).Show(12, 32, 4).End(1);

      var run = TimelineAnalyzer.Analyze(rows.Rows).Runs.Single();

      Assert.That(run.Counts.Undecodable, Is.EqualTo(1));
      Assert.That(run.Counts.NotRecorded, Is.EqualTo(1));
      Assert.That(run.Counts.Torn, Is.EqualTo(1));
      Assert.That(run.Frames[1].Flags.HasFlag(PresentedFrameFlags.UncertainStart));
      Assert.That(run.Frames[2].Flags.HasFlag(PresentedFrameFlags.UncertainStart));
    }

    [Test]
    public void Statistics_Percentiles()
    {
      var stats = Statistics.From(Enumerable.Range(1, 100).Select(i => (double)i));
      Assert.That(stats.Count, Is.EqualTo(100));
      Assert.That(stats.Min, Is.EqualTo(1));
      Assert.That(stats.Max, Is.EqualTo(100));
      Assert.That(stats.Mean, Is.EqualTo(50.5));
      Assert.That(stats.P50, Is.EqualTo(50.5));
      Assert.That(stats.P95, Is.EqualTo(95.05).Within(1e-9));
      Assert.That(Statistics.From(Array.Empty<double>()), Is.EqualTo(Statistics.Empty));
    }
  }
}
