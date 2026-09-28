//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The test clips in test-data/videos (made by mb-framepacing-explained, with the marker baked in): import each video through a real ffmpeg
//* and compare with the generator's manifest (ClipManifest), exactly. First the decoding: every video frame's marker must hold the payload
//* the generator drew (see marker_payload in its generate_videos.py). Then the analysis, frame by frame. Skipped when ffmpeg is not installed.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Linq;
using MB.FramePacing.Capture;
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
      DecodedCapture capture;
      using (var reader = new CaptureFileReader(Path.Combine(Import(clip), CaptureSessionInfo.FramesFileName)))
        capture = CaptureDecoder.Decode(reader);

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
        if (expected.Kind == MarkerKind.SequenceStart)
        {
          Assert.That(actual.Start, Is.Not.Null, where + ": start metadata");
          Assert.That(actual.Start!.Name, Is.EqualTo(manifest.StartName), where + ": name");
          Assert.That(actual.Start.UtcTicks, Is.Zero, where + ": start time (the generator writes none)");
        }
      }
    }

    /// <summary>
    /// The analysis of the decoded clip, frame by frame and to the tick: display time step, animation time step and error, drift, the late flag and
    /// how late, the target, and the pacing and prediction errors against the pacer's schedule in the markers.
    /// </summary>
    [TestCaseSource(typeof(VideoClips), nameof(VideoClips.Names))]
    public void Clip_AnalysisMatchesItsManifest(string clip)
    {
      var manifest = VideoClips.Manifest(clip);
      var run = CaptureAnalyzer.Analyze(Import(clip), new AnalysisOptions()).Timeline.Runs.Single();

      Assert.That(run.Frames, Has.Count.EqualTo(manifest.FrameCount), "every frame of the clip is presented");
      Assert.That(run.Pacing!.Source, Is.EqualTo(PacingSource.Schedule), "the markers carry the pacer's schedule");
      long refresh = (long)Math.Round(run.Pacing.RefreshPeriodMs * TimeSpan.TicksPerMillisecond);
      Assert.That(refresh, Is.AnyOf(166666L, 166667L), "the refresh is the capture period: 1/60 s in whole ticks");

      // Lateness is measured from the run's on-time frames: the earliest (shown - intended) of the frames with an intended time
      long onTime = Enumerable.Range(0, manifest.FrameCount).Where(i => manifest.IntendedTicks(i) != 0).Min(i => Behind(manifest, i));
      for (int i = 0; i < manifest.FrameCount; ++i)
      {
        var frame = run.Frames[i];
        string where = $"{clip}: frame {i}";
        Assert.That(frame.DriftTicks, Is.EqualTo(manifest.DriftTicks(i)), where + ": drift");
        Assert.That(
          frame.LatenessTicks,
          Is.EqualTo(manifest.IntendedTicks(i) != 0 ? Behind(manifest, i) - onTime : null),
          where + ": how late (0 = unknown intended time)"
        );
        if (i == 0)
        {
          // The manifest measures its first frame against the last frame of the previous loop, which the capture does not hold
          Assert.That(frame.DisplayDeltaTicks, Is.Null, where + ": no display time step");
          continue;
        }
        Assert.That(frame.DisplayDeltaTicks, Is.EqualTo(manifest.DisplayStepTicks(i)), where + ": display time step");
        Assert.That(frame.AnimationDeltaTicks, Is.EqualTo(manifest.AnimationStepTicks(i)), where + ": animation time step");
        Assert.That(frame.AnimationErrorTicks, Is.EqualTo(manifest.AnimationErrorTicks(i)), where + ": animation error");
        Assert.That(frame.Flags.HasFlag(PresentedFrameFlags.Late), Is.EqualTo(manifest.IsLate(i)), where + ": late");
        Assert.That(frame.TargetTicks, Is.EqualTo(manifest.TargetRefreshes(i) * refresh), where + ": target");
        long? intended = manifest.IntendedStepTicks(i);
        Assert.That(frame.PacingErrorTicks, Is.EqualTo(manifest.DisplayStepTicks(i) - intended), where + ": pacing error");
        Assert.That(frame.PredictionErrorTicks, Is.EqualTo(manifest.AnimationStepTicks(i) - intended), where + ": prediction error");
      }
      Assert.That(run.Pacing.LateFrames, Is.EqualTo(Enumerable.Range(1, manifest.FrameCount - 1).Count(manifest.IsLate)), $"{clip}: late frames");

      // Gamers Nexus's summaries, as mb-framepacing-explained's doc/measured-errors.md lists them for its modes
      var measured = Enumerable.Range(1, manifest.FrameCount - 1).ToArray();
      long absolute = measured.Sum(i => Math.Abs(manifest.AnimationErrorTicks(i)));
      double errorPerFrameMs = absolute / (double)measured.Length / TimeSpan.TicksPerMillisecond;
      double percentError = absolute * 100.0 / measured.Sum(manifest.DisplayStepTicks);
      Assert.That(run.Statistics.ErrorPerFrameMs, Is.EqualTo(errorPerFrameMs).Within(1e-9), $"{clip}: error per frame");
      Assert.That(run.Statistics.PercentError, Is.EqualTo(percentError).Within(1e-9), $"{clip}: percent error");
      TestContext.Out.WriteLine($"{clip}: error per frame {errorPerFrameMs:0.00} ms, percent error {percentError:0.0} %");
    }

    /// <summary>How long after its intended display time the frame is first shown (the pacer's and the video's clocks differ by a constant).</summary>
    private static long Behind(ClipManifest manifest, int frame) => manifest.ShownTicks(frame) - manifest.IntendedTicks(frame);

    private string Import(string clip) => VideoClips.Import(clip, m_ffmpeg, Path.Combine(m_directory, "capture"));
  }
}
