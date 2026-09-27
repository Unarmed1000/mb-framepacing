//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a test clip's manifest (test-data/videos/<clip>/manifest.json, written by mb-framepacing-explained) says, and the exact values that
//* follow from it: every video frame's marker, and per frame of the clip its display time, animation time step, animation error, lateness,
//* target and drift, all in whole ticks. The VideoClip tests of the analysis and of the charts compare with it.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Analysis.UnitTest
{
  /// <summary>
  /// Per frame of the clip: the refresh it is flipped on, the animation time it shows, how many refreshes late it is and the pacer's swap
  /// interval (the refresh rate over its target rate). The clip loops; the lead-in and lead-out show the previous and next loop.
  /// </summary>
  public sealed record ClipManifest(
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
    public int FrameCount => Refresh.Length;

    public static ClipManifest Load(string folder)
    {
      using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
      var marker = document.RootElement.GetProperty("settings").GetProperty("marker");
      var video = document.RootElement.GetProperty("videos")[0];
      var box = video.GetProperty("box");
      var frames = box.GetProperty("frames");
      T[] Array<T>(string name, Func<JsonElement, T> read) => frames.GetProperty(name).EnumerateArray().Select(read).ToArray();
      return new ClipManifest(
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
        RefreshTicks(intendedRefresh),
        (uint)RefreshTicks(SwapInterval[frame])
      );
    }

    /// <summary>When the video shows refresh <paramref name="refresh"/> of the clip, in whole ticks from the video's first frame.</summary>
    public long VideoFrameTicks(long refresh) => RefreshTicks(refresh + LeadIn);

    /// <summary>When frame <paramref name="frame"/> of the clip is first shown, in whole ticks from the video's first frame.</summary>
    public long ShownTicks(int frame) => VideoFrameTicks(Refresh[frame]);

    /// <summary>How long the previous frame was on screen (frame 1 on: frame 0 follows the previous loop, which the capture does not hold).</summary>
    public long DisplayTicks(int frame) => ShownTicks(frame) - ShownTicks(frame - 1);

    public long AnimationStepTicks(int frame) => AnimationTicks[frame] - AnimationTicks[frame - 1];

    public long AnimationErrorTicks(int frame) => AnimationStepTicks(frame) - DisplayTicks(frame);

    public bool IsLate(int frame) => Late[frame] > 0;

    /// <summary>The intended display time the frame's marker carries (the pacer's clock starts at the clip's first refresh; 0 = unknown).</summary>
    public long IntendedTicks(int frame) => RefreshTicks(Refresh[frame] - Late[frame]);

    /// <summary>The intended step to <paramref name="frame"/>, when both markers carry an intended display time.</summary>
    public long? IntendedStepTicks(int frame) =>
      IntendedTicks(frame) != 0 && IntendedTicks(frame - 1) != 0 ? IntendedTicks(frame) - IntendedTicks(frame - 1) : null;

    /// <summary>The frame's target in refreshes: its intended step when there is one, else the pacer's swap interval.</summary>
    public long TargetRefreshes(int frame) =>
      IntendedStepTicks(frame).HasValue ? (Refresh[frame] - Late[frame]) - (Refresh[frame - 1] - Late[frame - 1]) : SwapInterval[frame];

    /// <summary>How many refreshes the frame is on screen: until the next frame, the last one until the clip ends.</summary>
    public long RefreshesOnScreen(int frame) => (frame + 1 < FrameCount ? Refresh[frame + 1] : RefreshCount) - Refresh[frame];

    /// <summary>Animation time minus display time since the clip's first frame.</summary>
    public long DriftTicks(int frame) => (AnimationTicks[frame] - AnimationTicks[0]) - (ShownTicks(frame) - ShownTicks(0));

    /// <summary><paramref name="refreshes"/> refreshes in whole ticks, rounded half to even like the generator.</summary>
    private long RefreshTicks(long refreshes) => RoundedDivision(refreshes * TimeSpan.TicksPerSecond, Fps);

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
