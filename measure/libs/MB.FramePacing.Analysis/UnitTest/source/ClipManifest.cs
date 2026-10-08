//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a test clip's manifest (measure/test-data/videos/<clip>/manifest.json, written by mb-framepacing-explained) says, and the exact values that
//* follow from it: every video frame's marker, and per presented frame its display time step, animation time step, animation error,
//* lateness, target, preferred frame time and drift, all in whole ticks. The VideoClip tests of the analysis and of the charts compare with it.
//*
//* The manifest and the clips' markers count in nanoseconds; the tools still count in ticks and take a marker's times to the nearest tick
//* where they decode it (MarkerPayload.FromFrameMarker). So every marker time here is the manifest's nanoseconds through that same
//* conversion, and the video's own times (when a refresh is shown) are ticks as the tools read them from the file. This goes when the tools
//* count in nanoseconds.
//*
//* Two views: every rendered frame of the clip (the markers: a frame index, animation time, pacing fields and flags each), and the presented
//* frames, the ones the analysis counts. A frame is presented when it first appears with an index above every frame shown before it:
//* dropped frames never appear, and a frame shown out of order after a later one is not presented again. The per-frame methods take a
//* presented frame's position (0 = the clip's first frame).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Analysis.UnitTest
{
  /// <summary>
  /// Per rendered frame of the clip: the refresh it is first shown on (null: dropped), the animation time it shows, how many refreshes late
  /// it is (negative: early, out of order), the pacer's swap interval and the preferred one (the refresh rate over the target and the
  /// preferred rate; null on demand), its CPU start and busy, and whether it is static (nothing animates while it is on screen). <paramref name="Screen"/> is the frame on screen
  /// in each refresh, <paramref name="Presented"/> the frames the analysis counts. The clip loops; the lead-in and lead-out show the
  /// previous and next loop.
  /// </summary>
  public sealed record ClipManifest(
    int Fps,
    int RefreshCount,
    long DurationNs,
    int VideoFrameCount,
    int LeadIn,
    uint RunId,
    ulong FirstFrameIndex,
    string SequenceId,
    long?[] RenderedRefresh,
    long[] RenderedAnimationNs,
    int?[] RenderedLate,
    long?[] RenderedSwapInterval,
    long?[] RenderedPreferredInterval,
    long[] RenderedCpuStartNs,
    long[] RenderedCpuBusyNs,
    bool[] RenderedStaticAfter,
    bool[] RenderedStaticBefore,
    int[] Screen,
    int[] Presented,
    long ExpectedSkippedFrameIndices,
    long ExpectedOutOfOrderCaptures,
    int[]? ExpectedStaticSteps
  )
  {
    /// <summary>The analysis's |animation error| threshold: the slack of the static guess's clock rule.</summary>
    private const long ErrorThresholdTicks = TimeSpan.TicksPerMillisecond;

    /// <summary>The expectations follow the analysis's static guess (TimelineOptions.AssumeStatic, its default) or the flags alone.</summary>
    public bool AssumeStatic { get; init; } = true;

    /// <summary>The presented frames.</summary>
    public int FrameCount => Presented.Length;

    public static ClipManifest Load(string folder)
    {
      using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
      var marker = document.RootElement.GetProperty("settings").GetProperty("marker");
      var video = document.RootElement.GetProperty("videos")[0];
      var box = video.GetProperty("box");
      var frames = box.GetProperty("frames");
      decimal fps = video.GetProperty("fps").GetDecimal();
      T[] Array<T>(string name, Func<JsonElement, T> read) => frames.GetProperty(name).EnumerateArray().Select(read).ToArray();
      // The static flags per rendered frame (the idle clips); all false when a clip has none
      bool[] Flags(string name) =>
        frames.TryGetProperty(name, out var element)
          ? element.EnumerateArray().Select(e => e.GetBoolean()).ToArray()
          : new bool[frames.GetProperty("refresh").GetArrayLength()];
      T? Nullable<T>(JsonElement element, Func<JsonElement, T> read)
        where T : struct => element.ValueKind == JsonValueKind.Null ? null : read(element);

      var refresh = Array("refresh", e => Nullable(e, x => x.GetInt64()));
      int refreshCount = video.GetProperty("frameCount").GetInt32();
      // The frame on screen in each refresh: given for fault clips; otherwise the frames follow each other in order
      int[] screen = box.TryGetProperty("screen", out var screenElement)
        ? screenElement.EnumerateArray().Select(e => e.GetInt32()).ToArray()
        : Enumerable.Range(0, refreshCount).Select(r => System.Array.FindLastIndex(refresh, f => f is { } shown && shown <= r)).ToArray();
      int[] presented = box.TryGetProperty("presented", out var presentedElement)
        ? presentedElement.EnumerateArray().Select(e => e.GetInt32()).ToArray()
        : Enumerable.Range(0, refresh.Length).ToArray();
      var expected = box.TryGetProperty("expected", out var expectedElement) ? expectedElement : (JsonElement?)null;
      long Expected(string name) => expected is { } e && e.TryGetProperty(name, out var value) ? value.GetInt64() : 0;

      return new ClipManifest(
        (int)fps,
        refreshCount,
        WholeNumber(video.GetProperty("durationSeconds").GetDecimal() * NanosecondTimeSpan.NanosecondsPerSecond, "nanoseconds"),
        video.GetProperty("videoFrameCount").GetInt32(),
        marker.GetProperty("leadInRefreshes").GetInt32(),
        marker.GetProperty("runId").GetUInt32(),
        video.GetProperty("markerFirstFrameIndex").GetUInt64(),
        video.GetProperty("sequenceId").GetString()!,
        refresh,
        Array("animationNs", e => e.GetInt64()),
        Array("late", e => Nullable(e, x => x.GetInt32())),
        Array("targetFps", e => Nullable(e, x => WholeNumber(fps / x.GetDecimal(), "swap interval"))),
        Array("preferredFps", e => Nullable(e, x => WholeNumber(fps / x.GetDecimal(), "preferred swap interval"))),
        Array("cpuStartNs", e => e.GetInt64()),
        Array("cpuBusyNs", e => e.GetInt64()),
        Flags("staticAfter"),
        Flags("staticBefore"),
        screen,
        presented,
        Expected("skippedFrameIndices"),
        Expected("outOfOrderRefreshes"),
        // The rendered frames whose step from the presented frame before them is static by the flags alone (the idle fault clips)
        expected is { } given
        && given.TryGetProperty("staticSteps", out var staticSteps)
          ? staticSteps.EnumerateArray().Select(e => e.GetInt32()).ToArray()
          : null
      );
    }

    /// <summary>
    /// The marker in video frame <paramref name="refresh"/> (counted from the clip's first refresh; negative in the lead-in): the frame on screen,
    /// in the previous loop before the clip and the next loop after it, with its frame index and animation time counted on across loops.
    /// </summary>
    public MarkerPayload MarkerOfVideoFrame(int refresh)
    {
      int loop = refresh >= 0 ? refresh / RefreshCount : ((refresh + 1) / RefreshCount) - 1;
      int within = (int)(refresh - ((long)loop * RefreshCount));
      int frame = Screen[within];
      long intendedRefresh = RenderedRefresh[frame]!.Value - RenderedLate[frame]!.Value + ((long)loop * RefreshCount);
      var kind =
        refresh < 0 ? MarkerKind.SequenceStart
        : refresh >= RefreshCount ? MarkerKind.SequenceEnd
        : MarkerKind.Frame;
      // The marker as the generator wrote it, in nanoseconds, then as the tools hold it
      return MarkerPayload.FromFrameMarker(
        new MB.FramePacing.Marker.Payload(
          (MB.FramePacing.Marker.MarkerKind)kind,
          RunId,
          ((ulong)(loop + 1) * FirstFrameIndex) + (ulong)frame,
          (RenderedStaticAfter[frame] ? MB.FramePacing.Marker.MarkerFlags.StaticAfter : MB.FramePacing.Marker.MarkerFlags.NoFlags)
            | (RenderedStaticBefore[frame] ? MB.FramePacing.Marker.MarkerFlags.StaticBefore : MB.FramePacing.Marker.MarkerFlags.NoFlags),
          new NanosecondTimeSpan(RenderedAnimationNs[frame] + (loop * DurationNs)),
          MarkerFrameTime(RenderedPreferredInterval[frame]),
          MarkerFrameTime(RenderedSwapInterval[frame]),
          new NanosecondTickCount(MarkerRefreshNs(intendedRefresh)),
          new NanosecondTickCount(RenderedCpuStartNs[frame] + (loop * DurationNs)),
          NanosecondTimeDuration.FromNanoseconds(RenderedCpuBusyNs[frame])
        )
      );
    }

    /// <summary>The animation time rendered frame <paramref name="rendered"/> shows, as the tools hold it.</summary>
    private long AnimationTicks(int rendered) => SpanTicks(RenderedAnimationNs[rendered]);

    /// <summary>The frame index of presented frame <paramref name="frame"/> in the clip's first loop.</summary>
    public ulong FrameIndex(int frame) => FirstFrameIndex + (ulong)Presented[frame];

    /// <summary>Frame indices before presented frame <paramref name="frame"/> that were never shown, or shown only out of order.</summary>
    public ulong SkippedBefore(int frame) => frame > 0 ? (ulong)(Presented[frame] - Presented[frame - 1] - 1) : 0;

    /// <summary>When the video shows refresh <paramref name="refresh"/> of the clip, in whole ticks from the video's first frame.</summary>
    public long VideoFrameTicks(long refresh) => RefreshTicks(refresh + LeadIn);

    /// <summary>The refresh presented frame <paramref name="frame"/> is first shown on.</summary>
    public long FirstRefresh(int frame) => RenderedRefresh[Presented[frame]]!.Value;

    /// <summary>When presented frame <paramref name="frame"/> is first shown, in whole ticks from the video's first frame.</summary>
    public long ShownTicks(int frame) => VideoFrameTicks(FirstRefresh(frame));

    /// <summary>How long the previous frame was on screen (frame 1 on: frame 0 follows the previous loop, which the capture does not hold).</summary>
    public long DisplayStepTicks(int frame) => ShownTicks(frame) - ShownTicks(frame - 1);

    /// <summary>The display time step in refreshes.</summary>
    public long DisplayStepRefreshes(int frame) => FirstRefresh(frame) - FirstRefresh(frame - 1);

    public long AnimationStepTicks(int frame) => AnimationTicks(Presented[frame]) - AnimationTicks(Presented[frame - 1]);

    /// <summary>
    /// Nothing animates while presented frame <paramref name="frame"/> is on screen, by the flags alone: its own StaticAfter, or StaticBefore on
    /// the next presented frame when that is the next rendered frame. The last one's next frame is after the capture.
    /// </summary>
    public bool IsStaticByFlags(int frame) =>
      RenderedStaticAfter[Presented[frame]]
      || (frame + 1 < FrameCount && Presented[frame + 1] == Presented[frame] + 1 && RenderedStaticBefore[Presented[frame + 1]]);

    /// <summary>
    /// The analysis's guess (TimelineOptions.AssumeStatic): presented frame <paramref name="frame"/> held a rest whose flag a dropped frame
    /// took. Frames were dropped before the next presented frame (none of them shown out of order), and either that frame carries
    /// StaticBefore, or the clip uses the static flags on frames it shows, the next frame is on demand or the hold is half a refresh beyond
    /// what the frames in between were due, and the animation clock stood still: the animation step is at most one frame time per frame
    /// rendered in between and two refreshes or more short of the display step (the frame time: the next frame's target, else the animation
    /// step into the held frame).
    /// </summary>
    public bool IsAssumedStatic(int frame)
    {
      if (frame + 1 >= FrameCount || IsStaticByFlags(frame))
        return false;
      int held = Presented[frame];
      int next = Presented[frame + 1];
      int dropped = next - held - 1;
      if (dropped <= 0 || Enumerable.Range(held + 1, dropped).Any(Screen.Contains))
        return false;
      if (RenderedStaticBefore[next])
        return true;
      if (!Presented.Any(shown => RenderedStaticAfter[shown] || RenderedStaticBefore[shown]))
        return false;
      long refresh = RefreshTicks(1) - RefreshTicks(0);
      bool onDemand = RenderedSwapInterval[next] == null;
      // The frame time: the next frame's target, else (on demand) the animation step into the held frame, at least one refresh
      long frameTime =
        !onDemand ? RenderedSwapInterval[next]!.Value * refresh
        : frame >= 1 ? Math.Max(refresh, AnimationStepTicks(frame))
        : refresh;
      long due = (dropped + 1) * frameTime;
      long hold = DisplayStepTicks(frame + 1);
      long animation = AnimationStepTicks(frame + 1);
      return (onDemand || hold >= due + (refresh / 2)) && animation <= due + ErrorThresholdTicks && animation <= hold - (2 * refresh);
    }

    /// <summary>Nothing animates while presented frame <paramref name="frame"/> is on screen: by the flags, or assumed when the guess is on.</summary>
    public bool IsStatic(int frame) => IsStaticByFlags(frame) || (AssumeStatic && IsAssumedStatic(frame));

    /// <summary>A step from a static frame is not judged: it has no animation error (frame 1 on). The step into one is.</summary>
    public bool IsJudged(int frame) => !IsStatic(frame - 1);

    /// <summary>
    /// The display time step counts toward the frame rate numbers: it is the previous frame's time on screen, left out when that frame is
    /// static (frame 1 on).
    /// </summary>
    public bool CountsTowardFrameRate(int frame) => !IsStatic(frame - 1);

    public long? AnimationErrorTicks(int frame) => IsJudged(frame) ? AnimationStepTicks(frame) - DisplayStepTicks(frame) : null;

    public bool IsLate(int frame) => RenderedLate[Presented[frame]] > 0;

    /// <summary>How many refreshes after the one it was rendered for the frame is first shown (negative: early, out of order).</summary>
    public int LateRefreshes(int frame) => RenderedLate[Presented[frame]]!.Value;

    public long CpuStartTicks(int frame) => PointTicks(RenderedCpuStartNs[Presented[frame]]);

    public long CpuBusyTicks(int frame) => DurationTicks(RenderedCpuBusyNs[Presented[frame]]);

    /// <summary>The intended display time the frame's marker carries (the pacer's clock starts at the clip's first refresh; 0 = unknown).</summary>
    public long IntendedTicks(int frame) => PointTicks(MarkerRefreshNs(FirstRefresh(frame) - RenderedLate[Presented[frame]]!.Value));

    /// <summary>The intended step to <paramref name="frame"/>, when both markers carry an intended display time.</summary>
    public long? IntendedStepTicks(int frame) =>
      IntendedTicks(frame) != 0 && IntendedTicks(frame - 1) != 0 ? IntendedTicks(frame) - IntendedTicks(frame - 1) : null;

    /// <summary>The frame's target in refreshes: its intended step when there is one, else the pacer's swap interval (null on demand).</summary>
    public long? TargetRefreshes(int frame) =>
      IntendedStepTicks(frame).HasValue
        ? (FirstRefresh(frame) - RenderedLate[Presented[frame]]!.Value) - (FirstRefresh(frame - 1) - RenderedLate[Presented[frame - 1]]!.Value)
        : RenderedSwapInterval[Presented[frame]];

    /// <summary>The pacer's swap interval before the frame (the marker's target frame time), in refreshes; null on demand.</summary>
    public long? SwapIntervalRefreshes(int frame) => RenderedSwapInterval[Presented[frame]];

    /// <summary>The frame time the application prefers, in refreshes; null on demand.</summary>
    public long? PreferredRefreshes(int frame) => RenderedPreferredInterval[Presented[frame]];

    /// <summary>How many refreshes the frame is on screen: until the next presented frame, the last one until the clip ends.</summary>
    public long RefreshesOnScreen(int frame) => (frame + 1 < FrameCount ? FirstRefresh(frame + 1) : RefreshCount) - FirstRefresh(frame);

    /// <summary>
    /// How many refreshes from its first one the frame itself was seen: up to its last refresh before the next presented frame. Refreshes after
    /// that showed an older frame out of order.
    /// </summary>
    public long SeenRefreshes(int frame)
    {
      long first = FirstRefresh(frame);
      long end = frame + 1 < FrameCount ? FirstRefresh(frame + 1) : RefreshCount;
      long last = first;
      for (long r = first; r < end; ++r)
      {
        if (Screen[r] == Presented[frame])
          last = r;
      }
      return last - first + 1;
    }

    /// <summary>
    /// The refreshes after the frame was first seen, before the next presented frame, that showed an older frame out of order: the refresh
    /// and the frame index it showed.
    /// </summary>
    public IReadOnlyList<(long Refresh, ulong FrameIndex)> OlderFramesAfter(int frame)
    {
      long first = FirstRefresh(frame);
      long end = frame + 1 < FrameCount ? FirstRefresh(frame + 1) : RefreshCount;
      var older = new List<(long Refresh, ulong FrameIndex)>();
      for (long r = first; r < end; ++r)
      {
        if (Screen[r] < Presented[frame])
          older.Add((r, FirstFrameIndex + (ulong)Screen[r]));
      }
      return older;
    }

    /// <summary>The frame indices skipped before the frame that were never on screen, not even out of order: the target dropped them.</summary>
    public long DroppedBefore(int frame) =>
      frame > 0 ? Enumerable.Range(Presented[frame - 1] + 1, Presented[frame] - Presented[frame - 1] - 1).Count(o => !Screen.Contains(o)) : 0;

    /// <summary>The judged animation errors up to the frame, since the clip's first frame (animation minus display time without static steps).</summary>
    public long DriftTicks(int frame) => Enumerable.Range(1, frame).Sum(f => AnimationErrorTicks(f) ?? 0);

    /// <summary>When the video shows a frame <paramref name="refreshes"/> refreshes in: whole ticks, as the tools read the file's times.</summary>
    private long RefreshTicks(long refreshes) => RoundedDivision(refreshes * TimeSpan.TicksPerSecond, Fps);

    /// <summary><paramref name="refreshes"/> refreshes as a marker carries them: whole nanoseconds, rounded half to even like the generator.</summary>
    private long MarkerRefreshNs(long refreshes) => RoundedDivision(refreshes * NanosecondTimeSpan.NanosecondsPerSecond, Fps);

    // A marker's time in nanoseconds as the tools hold it, in ticks: through the tools' own conversion, so the two can not differ
    private static long SpanTicks(long nanoseconds) => FromMarker(animationTime: new NanosecondTimeSpan(nanoseconds)).AnimationTime.Ticks;

    private static long PointTicks(long nanoseconds) => FromMarker(cpuStartTime: new NanosecondTickCount(nanoseconds)).CpuStartTime.Ticks;

    private static long DurationTicks(long nanoseconds) => FromMarker(cpuBusy: NanosecondTimeDuration.FromNanoseconds(nanoseconds)).CpuBusy.Ticks;

    private static MarkerPayload FromMarker(
      NanosecondTimeSpan animationTime = default,
      NanosecondTickCount cpuStartTime = default,
      NanosecondTimeDuration cpuBusy = default
    ) =>
      MarkerPayload.FromFrameMarker(
        new MB.FramePacing.Marker.Payload(
          MB.FramePacing.Marker.MarkerKind.Frame,
          0,
          0,
          MB.FramePacing.Marker.MarkerFlags.NoFlags,
          animationTime,
          cpuStartTime: cpuStartTime,
          cpuBusy: cpuBusy
        )
      );

    /// <summary>A marker's target or preferred frame time: whole refreshes in nanoseconds, or on demand.</summary>
    private NanosecondTimeDuration MarkerFrameTime(long? refreshes) =>
      refreshes is { } count ? NanosecondTimeDuration.FromNanoseconds(MarkerRefreshNs(count)) : MB.FramePacing.Marker.Payload.OnDemandFrameTime;

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
  }
}
