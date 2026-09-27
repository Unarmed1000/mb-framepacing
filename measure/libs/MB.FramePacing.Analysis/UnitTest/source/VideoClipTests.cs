//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The test clips in test-data/videos (made by mb-framepacing-explained, with the marker baked in): import each video through a real ffmpeg
//* and compare with the generator's manifest. First the decoding: every video frame's marker must hold exactly the payload the generator drew
//* (see marker_payload in its generate_videos.py). Then the analysis: display times, animation errors and late frames, frame by frame.
//* Skipped when ffmpeg is not installed.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Ffmpeg;
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
      try
      {
        m_ffmpeg = FfmpegLocator.Find(null, FramePacingConfig.Load());
      }
      catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
      {
        Assert.Ignore("ffmpeg is not installed: " + ex.Message);
      }
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

    private static IEnumerable<string> Clips() =>
      Directory.GetDirectories(ClipDirectory()).Where(d => File.Exists(Path.Combine(d, "video.mp4"))).Select(Path.GetFileName).Order()!;

    /// <summary>
    /// Every video frame decodes, in order, to the marker the generator drew into it: the start marker in the lead-in (with the clip's label as
    /// its name), the frame on screen in every refresh of the clip (a held frame repeats its marker), the end marker in the lead-out. Each
    /// payload field must be exactly what the manifest gives: the video is lossless for the marker, so there is no tolerance.
    /// </summary>
    [TestCaseSource(nameof(Clips))]
    public void Clip_EveryVideoFrameDecodesToItsMarker(string clip)
    {
      var manifest = Manifest.Load(Path.Combine(ClipDirectory(), clip));
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
    /// The analysis of the decoded clip: one presented frame per frame of the clip, timed by the pacer's schedule, with the manifest's display
    /// time, animation error and late frames.
    /// </summary>
    [TestCaseSource(nameof(Clips))]
    public void Clip_AnalysisMatchesItsManifest(string clip)
    {
      var manifest = Manifest.Load(Path.Combine(ClipDirectory(), clip));
      var run = CaptureAnalyzer.Analyze(Import(clip), new AnalysisOptions()).Timeline.Runs.Single();

      Assert.That(run.Frames, Has.Count.EqualTo(manifest.Refresh.Length), "every frame of the clip is presented");
      Assert.That(run.Pacing!.Source, Is.EqualTo(PacingSource.Schedule), "the markers carry the pacer's schedule");
      // The manifest measures its first frame against the last frame of the previous loop; the analysis starts with the run's first frame
      for (int i = 1; i < manifest.Refresh.Length; ++i)
      {
        var frame = run.Frames[i];
        long display = manifest.VideoFrameTicks(manifest.Refresh[i]) - manifest.VideoFrameTicks(manifest.Refresh[i - 1]);
        long animation = manifest.AnimationTicks[i] - manifest.AnimationTicks[i - 1];
        Assert.That(frame.DisplayDeltaTicks, Is.EqualTo(display), $"{clip}: display time of frame {i}");
        Assert.That(frame.AnimationDeltaTicks, Is.EqualTo(animation), $"{clip}: animation time step of frame {i}");
        Assert.That(frame.AnimationErrorTicks, Is.EqualTo(animation - display), $"{clip}: animation error of frame {i}");
      }
      Assert.That(run.Pacing.LateFrames, Is.EqualTo(manifest.Late.Skip(1).Count(l => l > 0)), $"{clip}: late frames");
    }

    /// <summary>Imports the clip's video as a capture and returns the capture's folder.</summary>
    private string Import(string clip)
    {
      var output = Path.Combine(m_directory, "capture");
      var media = MediaInput.Create(Path.Combine(ClipDirectory(), clip, "video.mp4"), new MediaInputOptions(), output);
      using (var source = FfmpegCaptureSource.Start(media.ToCaptureOptions(m_ffmpeg), TimeSpan.FromSeconds(30)))
        CaptureRunner.Run(source, new CaptureRunOptions { OutputDirectory = output }, null, CancellationToken.None);
      return output;
    }

    private static string ClipDirectory()
    {
      var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
      while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "test-data", "videos")))
        directory = directory.Parent;
      return directory != null
        ? Path.Combine(directory.FullName, "test-data", "videos")
        : throw new DirectoryNotFoundException("Could not locate test-data/videos above " + TestContext.CurrentContext.TestDirectory);
    }

    /// <summary>
    /// What a clip's manifest says: per frame of the clip the refresh it is flipped on, the animation time it shows, how
    /// many refreshes late it is and the pacer's swap interval (the refresh rate over its target rate); the clip loops, and the lead-in and lead-out show the previous and next loop.
    /// </summary>
    private sealed record Manifest(
      int Fps,
      int RefreshCount,
      long DurationTicks,
      int VideoFrameCount,
      int LeadIn,
      uint RunId,
      ulong FirstFrameIndex,
      string StartName,
      long[] Refresh,
      long[] AnimationTicks,
      int[] Late,
      long[] SwapInterval
    )
    {
      public static Manifest Load(string folder)
      {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
        var marker = document.RootElement.GetProperty("settings").GetProperty("marker");
        var video = document.RootElement.GetProperty("videos")[0];
        var box = video.GetProperty("box");
        var frames = box.GetProperty("frames");
        T[] Array<T>(string name, Func<JsonElement, T> read) => frames.GetProperty(name).EnumerateArray().Select(read).ToArray();
        return new Manifest(
          video.GetProperty("fps").GetInt32(),
          video.GetProperty("frameCount").GetInt32(),
          WholeTicks(video.GetProperty("durationSeconds").GetDecimal() * TimeSpan.TicksPerSecond),
          video.GetProperty("videoFrameCount").GetInt32(),
          marker.GetProperty("leadInRefreshes").GetInt32(),
          marker.GetProperty("runId").GetUInt32(),
          video.GetProperty("markerFirstFrameIndex").GetUInt64(),
          TruncateUtf8(box.GetProperty("label").GetString()!, MarkerPayload.MaxStartNameBytes),
          Array("refresh", e => e.GetInt64()),
          Array("animationMs", e => WholeTicks(e.GetDecimal() * TimeSpan.TicksPerMillisecond)),
          Array("late", e => e.GetInt32()),
          Array("targetFps", e => WholeNumber(video.GetProperty("fps").GetDecimal() / e.GetDecimal(), "swap interval"))
        );
      }

      /// <summary>
      /// The marker in video frame <paramref name="refresh"/> (counted from the clip's first refresh; negative in the lead-in): the frame on screen,
      /// in the previous loop before the clip and the next loop after it, with its frame index and animation time counted on across loops.
      /// </summary>
      public MarkerPayload MarkerOfVideoFrame(int refresh)
      {
        int loop = refresh >= 0 ? refresh / RefreshCount : ((refresh + 1) / RefreshCount) - 1;
        long within = refresh - ((long)loop * RefreshCount);
        int frame = System.Array.FindLastIndex(Refresh, r => r <= within);
        long intendedRefresh = Refresh[frame] - Late[frame] + ((long)loop * RefreshCount);
        var kind =
          refresh < 0 ? MarkerKind.SequenceStart
          : refresh >= RefreshCount ? MarkerKind.SequenceEnd
          : MarkerKind.Frame;
        return new MarkerPayload(
          ((ulong)(loop + 1) * FirstFrameIndex) + (ulong)frame,
          AnimationTicks[frame] + (loop * DurationTicks),
          RunId,
          kind,
          RoundedDivision(intendedRefresh * TimeSpan.TicksPerSecond, Fps),
          (uint)RoundedDivision(SwapInterval[frame] * TimeSpan.TicksPerSecond, Fps)
        );
      }

      /// <summary>When the video shows refresh <paramref name="refresh"/> of the clip, in whole ticks from the video's first frame.</summary>
      public long VideoFrameTicks(long refresh) => RoundedDivision((refresh + LeadIn) * TimeSpan.TicksPerSecond, Fps);

      /// <summary>A manifest time that must be a whole number of ticks, as the marker stores it.</summary>
      private static long WholeTicks(decimal ticks) => WholeNumber(ticks, "ticks");

      private static long WholeNumber(decimal value, string what) =>
        value == decimal.Truncate(value) ? (long)value : throw new InvalidDataException($"{value} {what} is not a whole number");

      /// <summary><paramref name="value"/> / <paramref name="divisor"/> rounded half to even, as the generator (Python's round) rounds.</summary>
      private static long RoundedDivision(long value, long divisor)
      {
        long quotient = Math.DivRem(value, divisor, out long remainder);
        if (remainder < 0)
        {
          --quotient;
          remainder += divisor;
        }
        long twice = 2 * remainder;
        return twice > divisor || (twice == divisor && (quotient & 1) != 0) ? quotient + 1 : quotient;
      }

      /// <summary>The generator cuts the start marker's name to the marker's limit of UTF-8 bytes on a character boundary.</summary>
      private static string TruncateUtf8(string text, int maxBytes)
      {
        var result = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
          if (Encoding.UTF8.GetByteCount(result.ToString()) + rune.Utf8SequenceLength > maxBytes)
            break;
          result.Append(rune.ToString());
        }
        return result.ToString();
      }
    }
  }
}
