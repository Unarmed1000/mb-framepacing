//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The test clips in test-data/videos (made by mb-framepacing-explained, with the marker baked in): import each video through a real ffmpeg
//* and compare with the generator's manifest (ClipManifest), exactly. First the decoding: every video frame's marker must hold the payload
//* the generator drew (see marker_payload in its generate_videos.py). Then the analysis, frame by frame. Skipped when ffmpeg is not installed.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Linq;
using MB.FramePacing.Capture;
using MB.FramePacing.Data;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  [Category("ffmpeg")]
  public class VideoClipTests
  {
    private string m_directory = string.Empty;
    private string m_ffmpeg = string.Empty;

    [SetUp]
    public void SetUp()
    {
      m_ffmpeg = VideoClips.FindFfmpegOrIgnore();
      m_directory = Path.Combine(Path.GetTempPath(), "mb-framepacing-tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
      try
      {
        if (Directory.Exists(m_directory))
          Directory.Delete(m_directory, true);
      }
      catch (IOException) { }
    }

    /// <summary>
    /// Every video frame decodes, in order, to the marker the generator drew into it: the start marker in the lead-in (with the clip's label as
    /// its name), the frame on screen in every refresh of the clip (a held frame repeats its marker), the end marker in the lead-out. Each
    /// payload field must be exactly what the manifest gives: the video is lossless for the marker, so there is no tolerance.
    /// </summary>
    [TestCaseSource(typeof(VideoClips), nameof(VideoClips.Names))]
    public void Clip_EveryVideoFrameDecodesToItsMarker(string clip)
    {
      var manifest = VideoClips.Manifest(clip);
      // The capture data, decoded live during the import
      DecodedCapture capture;
      using (var reader = new CaptureDataReader(Path.Combine(Import(clip), CaptureSessionInfo.DataFileName)))
        capture = CaptureDecoder.FromData(reader.Header, reader.ReadAll());

      Assert.That(capture.Rows, Has.Count.EqualTo(manifest.VideoFrameCount), $"{clip}: one capture per video frame");
      for (int row = 0; row < capture.Rows.Count; ++row)
      {
        var actual = capture.Rows[row];
        var expected = manifest.MarkerOfVideoFrame(row - manifest.LeadIn);
        string where = $"{clip}: video frame {row} ({expected.Kind} marker, frame index {expected.FrameIndex})";
        Assert.That(actual.Status, Is.EqualTo(CaptureStatus.Decoded), where);
        var payload = actual.Payload;
        Assert.That(payload.Kind, Is.EqualTo(expected.Kind), where + ": kind");
        Assert.That(payload.FrameIndex, Is.EqualTo(expected.FrameIndex), where + ": frame index");
        Assert.That(payload.RunId, Is.EqualTo(expected.RunId), where + ": run id");
        Assert.That(payload.AnimationTicks, Is.EqualTo(expected.AnimationTicks), where + ": animation time");
        Assert.That(payload.IntendedDisplayTicks, Is.EqualTo(expected.IntendedDisplayTicks), where + ": intended display time");
        Assert.That(payload.TargetFrameTicks, Is.EqualTo(expected.TargetFrameTicks), where + ": target frame time");
        Assert.That(payload.CpuStartTicks, Is.EqualTo(expected.CpuStartTicks), where + ": CPU start time");
        Assert.That(payload.CpuBusyTicks, Is.EqualTo(expected.CpuBusyTicks), where + ": CPU busy");
        Assert.That(payload.PreferredFrameTicks, Is.EqualTo(expected.PreferredFrameTicks), where + ": preferred frame time");
        Assert.That(payload.Flags, Is.EqualTo(expected.Flags), where + ": flags");
        if (expected.Kind == MarkerKind.SequenceStart)
        {
          Assert.That(actual.Start, Is.Not.Null, where + ": start metadata");
          Assert.That(actual.Start!.SequenceText, Is.EqualTo(manifest.SequenceId), where + ": sequence id");
          Assert.That(actual.Start.UtcTicks, Is.Zero, where + ": start time (the generator writes none)");
        }
      }
    }

    /// <summary>
    /// The analysis of the decoded clip, frame by frame and to the tick: the presented frames (dropped frames never appear, a frame shown out of
    /// order is not presented again) with their frame index and the indices skipped before them, display time step, animation time step and
    /// error (not judged from or to a static frame), drift, the late flag and how late, the target and preferred frame time, the pacing and
    /// prediction errors against the pacer's schedule in the markers, and the CPU start time, CPU busy, frametime and CPU wait.
    /// </summary>
    [TestCaseSource(typeof(VideoClips), nameof(VideoClips.Names))]
    public void Clip_AnalysisMatchesItsManifest(string clip)
    {
      var manifest = VideoClips.Manifest(clip);
      var run = CaptureAnalyzer.Analyze(Import(clip), new AnalysisOptions()).Timeline.Runs.Single();

      Assert.That(run.Frames, Has.Count.EqualTo(manifest.FrameCount), "the manifest's presented frames");
      Assert.That(run.Counts.SkippedFrameIndices, Is.EqualTo(manifest.ExpectedSkippedFrameIndices), $"{clip}: frame indices never presented");
      Assert.That(run.Counts.OutOfOrderCaptures, Is.EqualTo(manifest.ExpectedOutOfOrderCaptures), $"{clip}: captures out of order");
      Assert.That(run.Pacing!.Source, Is.EqualTo(PacingSource.Schedule), "the markers carry the pacer's schedule");
      long refresh = (long)Math.Round(run.Pacing.RefreshPeriodMs * TimeSpan.TicksPerMillisecond);
      Assert.That(refresh, Is.AnyOf(166666L, 166667L), "the refresh is the capture period: 1/60 s in whole ticks");

      // Lateness is measured from the run's on-time frames: the earliest (shown - intended) of the frames on time (an out-of-order frame is early)
      long onTime = Enumerable
        .Range(0, manifest.FrameCount)
        .Where(i => manifest.IntendedTicks(i) != 0 && manifest.LateRefreshes(i) == 0)
        .Min(i => Behind(manifest, i));
      for (int i = 0; i < manifest.FrameCount; ++i)
      {
        var frame = run.Frames[i];
        string where = $"{clip}: frame {i}";
        Assert.That(frame.FrameIndex, Is.EqualTo(manifest.FrameIndex(i)), where + ": frame index");
        // The refreshes after it that showed an older frame out of order: kept with the frame, at their capture's time
        Assert.That(
          (frame.OlderFrames ?? Array.Empty<OlderFrameCapture>()).Select(o => (o.FrameIndex, o.CaptureTicks)),
          Is.EqualTo(manifest.OlderFramesAfter(i).Select(o => (o.FrameIndex, manifest.VideoFrameTicks(o.Refresh)))),
          where + ": older frames shown out of order after it"
        );
        Assert.That(frame.Flags.HasFlag(PresentedFrameFlags.UncertainStep), Is.False, where + ": the clip's capture has no gap");
        Assert.That(frame.SkippedBefore, Is.EqualTo(manifest.SkippedBefore(i)), where + ": frame indices skipped before it");
        Assert.That(frame.Flags.HasFlag(PresentedFrameFlags.Static), Is.EqualTo(manifest.IsStatic(i)), where + ": static");
        Assert.That(frame.PreferredTicks, Is.EqualTo(manifest.PreferredRefreshes(i) * refresh), where + ": preferred frame time (null on demand)");
        Assert.That(frame.DriftTicks, Is.EqualTo(manifest.DriftTicks(i)), where + ": drift");
        Assert.That(
          frame.LatenessTicks,
          Is.EqualTo(manifest.IntendedTicks(i) != 0 ? Behind(manifest, i) - onTime : null),
          where + ": how late (0 = unknown intended time)"
        );
        // The application side: CPU start and CPU busy from the marker; the frametime to the next frame's CPU start (the clip's last frame
        // is followed by the end marker, not a measured frame) and CPU wait = frametime - CPU busy
        long cpuStart = manifest.CpuStartTicks(i);
        long cpuBusy = manifest.CpuBusyTicks(i);
        // Only to the next frame index: after dropped or out-of-order frames the next presented frame is not the next one rendered
        long? frameTime =
          i + 1 < manifest.FrameCount
          && manifest.FrameIndex(i + 1) == manifest.FrameIndex(i) + 1
          && cpuStart != 0
          && manifest.CpuStartTicks(i + 1) != 0
            ? manifest.CpuStartTicks(i + 1) - cpuStart
            : null;
        Assert.That(frame.CpuStartTicks, Is.EqualTo(cpuStart), where + ": CPU start time");
        Assert.That(frame.CpuBusyTicks, Is.EqualTo((uint)cpuBusy), where + ": CPU busy");
        Assert.That(frame.FrameTimeTicks, Is.EqualTo(frameTime), where + ": frametime");
        Assert.That(frame.CpuWaitTicks, Is.EqualTo(frameTime.HasValue && cpuBusy != 0 ? frameTime - cpuBusy : null), where + ": CPU wait");
        if (i == 0)
        {
          // The manifest measures its first frame against the last frame of the previous loop, which the capture does not hold
          Assert.That(frame.DisplayDeltaTicks, Is.Null, where + ": no display time step");
          Assert.That(frame.Flags.HasFlag(PresentedFrameFlags.StaticBefore), Is.False, where + ": nothing before it");
          continue;
        }
        Assert.That(
          frame.Flags.HasFlag(PresentedFrameFlags.StaticBefore),
          Is.EqualTo(!manifest.CountsTowardFrameRate(i)),
          where + ": the frame before it is static"
        );
        Assert.That(frame.DisplayDeltaTicks, Is.EqualTo(manifest.DisplayStepTicks(i)), where + ": display time step");
        Assert.That(frame.AnimationDeltaTicks, Is.EqualTo(manifest.AnimationStepTicks(i)), where + ": animation time step");
        Assert.That(frame.AnimationErrorTicks, Is.EqualTo(manifest.AnimationErrorTicks(i)), where + ": animation error");
        Assert.That(frame.Flags.HasFlag(PresentedFrameFlags.Late), Is.EqualTo(manifest.IsLate(i)), where + ": late");
        Assert.That(frame.TargetTicks, Is.EqualTo(manifest.TargetRefreshes(i) * refresh), where + ": target");
        long? intended = manifest.IntendedStepTicks(i);
        Assert.That(frame.PacingErrorTicks, Is.EqualTo(manifest.DisplayStepTicks(i) - intended), where + ": pacing error");
        Assert.That(
          frame.PredictionErrorTicks,
          Is.EqualTo(manifest.IsJudged(i) ? manifest.AnimationStepTicks(i) - intended : null),
          where + ": prediction error (not judged from or to a static frame)"
        );
      }
      Assert.That(run.Pacing.LateFrames, Is.EqualTo(Enumerable.Range(1, manifest.FrameCount - 1).Count(manifest.IsLate)), $"{clip}: late frames");

      // Gamers Nexus's summaries over the judged frames: the |animation error| per frame, and as a percentage of their display time
      var measured = Enumerable.Range(1, manifest.FrameCount - 1).Where(manifest.IsJudged).ToArray();
      long absolute = measured.Sum(i => Math.Abs(manifest.AnimationErrorTicks(i)!.Value));
      double errorPerFrameMs = absolute / (double)measured.Length / TimeSpan.TicksPerMillisecond;
      double percentError = absolute * 100.0 / measured.Sum(manifest.DisplayStepTicks);
      Assert.That(run.Statistics.ErrorPerFrameMs, Is.EqualTo(errorPerFrameMs).Within(1e-9), $"{clip}: error per frame");
      Assert.That(run.Statistics.PercentError, Is.EqualTo(percentError).Within(1e-9), $"{clip}: percent error");
      TestContext.Out.WriteLine($"{clip}: error per frame {errorPerFrameMs:0.00} ms, percent error {percentError:0.0} %");

      // The frame rate numbers describe the frames that animate: every display time step but a static frame's time on screen
      var steps = Enumerable.Range(1, manifest.FrameCount - 1).Where(manifest.CountsTowardFrameRate).Select(manifest.DisplayStepTicks).ToArray();
      Assert.That(run.Statistics.DisplayDeltaMs.Count, Is.EqualTo(steps.Length), $"{clip}: display time steps");
      Assert.That(run.Statistics.ExcludedStaticFrames, Is.EqualTo(manifest.FrameCount - 1 - steps.Length), $"{clip}: static frames left out");
      Assert.That(run.Statistics.UncertainSteps, Is.Zero, $"{clip}: no step across a capture gap");
      Assert.That(
        run.Statistics.AverageFps,
        Is.EqualTo(steps.Length * (double)TimeSpan.TicksPerSecond / steps.Sum()).Within(1e-9),
        $"{clip}: average fps"
      );
      var sortedSteps = steps.Order().ToArray();
      Assert.That(
        run.Statistics.OnePercentLowFps,
        Is.EqualTo(
          sortedSteps.Length >= RunStatistics.MinFramesForOnePercentLow
            ? TimeSpan.TicksPerSecond / (double)sortedSteps[(int)Math.Ceiling(0.99 * sortedSteps.Length) - 1]
            : (double?)null
        ),
        $"{clip}: 1 % low"
      );
    }

    /// <summary>
    /// The capture data decoded live during the import is exactly what decoding the stored frames afterwards gives: the same layout, and for
    /// every captured frame the same capture index, timestamps, flags, status and encoded marker bytes.
    /// </summary>
    [TestCaseSource(typeof(VideoClips), nameof(VideoClips.Names))]
    public void Clip_LiveDataEqualsTheDataDecodedFromTheFrames(string clip)
    {
      string output = VideoClips.Import(clip, m_ffmpeg, Path.Combine(m_directory, "capture"), keepFrames: true);
      using var live = new CaptureDataReader(Path.Combine(output, CaptureSessionInfo.DataFileName));
      using var frames = new CaptureFileReader(Path.Combine(output, CaptureSessionInfo.FramesFileName));
      var (header, records) = CaptureDecoder.DecodeFrames(frames, camera: false);

      Assert.That(live.Header.Markers, Is.EqualTo(header.Markers), $"{clip}: the same marker layout");
      var liveRecords = live.ReadAll();
      Assert.That(liveRecords, Has.Length.EqualTo(records.Length), $"{clip}: one record per stored frame");
      for (int i = 0; i < records.Length; ++i)
      {
        var (a, b) = (liveRecords[i], records[i]);
        string where = $"{clip}: record {i}";
        Assert.That(
          (a.CaptureIndex, a.HostTicks, a.DeviceTicks, a.SourceDrops, a.Status),
          Is.EqualTo((b.CaptureIndex, b.HostTicks, b.DeviceTicks, b.SourceDrops, b.Status)),
          where
        );
        Assert.That(a.MainBytes, Is.EqualTo(b.MainBytes), where + ": main marker bytes");
        Assert.That(a.SecondBytes, Is.EqualTo(b.SecondBytes), where + ": second marker bytes");
      }
    }

    /// <summary>How long after its intended display time the frame is first shown (the pacer's and the video's clocks differ by a constant).</summary>
    private static long Behind(ClipManifest manifest, int frame) => manifest.ShownTicks(frame) - manifest.IntendedTicks(frame);

    private string Import(string clip) => VideoClips.Import(clip, m_ffmpeg, Path.Combine(m_directory, "capture"));
  }
}
