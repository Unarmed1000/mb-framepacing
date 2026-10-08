//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A deterministic model of "a game presenting frames on vsync, captured by a capture card". It produces the ground truth the analyzer must
//* recover: which application frame is on screen at every capture instant.
//*
//* The scenario is worked out in whole nanoseconds: the refresh period is its rate's period rounded to the nearest nanosecond, every display
//* time a whole number of those periods, and a capture's time, a length given in seconds and a torn frame's share of a refresh are rounded
//* to the nearest nanosecond.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Capture.Synthetic
{
  public sealed class SyntheticScenario
  {
    private readonly List<SyntheticPresentedFrame> m_presented = new List<SyntheticPresentedFrame>();

    public SyntheticScenario(SyntheticScenarioOptions options)
    {
      Options = options ?? throw new ArgumentNullException(nameof(options));
      if (options.CaptureFps <= 0 || options.RefreshHz <= 0)
        throw new ArgumentOutOfRangeException(nameof(options), "Rates must be positive");
      StartMetadata = StartMetadata.FromTag(options.RunStartUtcTicks, options.SequenceTag);
      BuildTimeline();
      CaptureCount = (long)Math.Floor(options.TotalSeconds * options.CaptureFps);
    }

    public SyntheticScenarioOptions Options { get; }

    public StartMetadata StartMetadata { get; }

    /// <summary>Every presented frame in display order.</summary>
    public IReadOnlyList<SyntheticPresentedFrame> PresentedFrames => m_presented;

    public long CaptureCount { get; }

    /// <summary>The display's refresh period, rounded to the nearest nanosecond.</summary>
    public NanosecondTimeSpan RefreshInterval =>
      new NanosecondTimeSpan((long)Math.Round(NanosecondTimeSpan.NanosecondsPerSecond / Options.RefreshHz));

    /// <summary>Where the synthetic pacer's steady clock starts.</summary>
    public static readonly NanosecondTickCount PacerEpoch = NanosecondTickCount.FromSeconds(1);

    /// <summary>Capture instant of capture index <paramref name="captureIndex"/>, rounded to the nearest nanosecond.</summary>
    public NanosecondTickCount CaptureTime(long captureIndex) =>
      new NanosecondTickCount((long)Math.Round((captureIndex + Options.CapturePhase) * NanosecondTimeSpan.NanosecondsPerSecond / Options.CaptureFps));

    /// <summary>Index into <see cref="PresentedFrames"/> of the frame on screen at a capture, -1 if nothing is shown yet.</summary>
    public int PresentedIndexAt(long captureIndex) => PresentedIndexAtTime(CaptureTime(captureIndex));

    /// <summary>Index into <see cref="PresentedFrames"/> of the latest frame presented at or before <paramref name="time"/>, -1 if none.</summary>
    public int PresentedIndexAtTime(NanosecondTickCount time)
    {
      int lo = 0;
      int hi = m_presented.Count - 1;
      int found = -1;
      while (lo <= hi)
      {
        int mid = (lo + hi) / 2;
        if (m_presented[mid].DisplayTime <= time)
        {
          found = mid;
          lo = mid + 1;
        }
        else
          hi = mid - 1;
      }
      return found;
    }

    private void BuildTimeline()
    {
      var o = Options;
      var refresh = RefreshInterval;
      // Display times, on the capture's clock
      var leadInEnd = new NanosecondTickCount(Seconds(o.LeadInSeconds));
      var startEnd = leadInEnd + Seconds(o.StartMarkerSeconds);
      var runEnd = startEnd + Seconds(o.RunSeconds);
      var endEnd = runEnd + Seconds(o.EndMarkerSeconds);
      var totalEnd = new NanosecondTickCount(Seconds(o.TotalSeconds));

      long slot = 0;
      // The pacer's plan: every frame one vsync after the previous one. A stall shows a frame later than planned, a skipped frame (replaced
      // before its scanout) makes the next one show a vsync early; either way the pacer plans on from the frame it actually showed
      long planned = 0;
      bool replan = false;
      ulong frameIndex = o.FirstFrameIndex;
      var animationTime = NanosecondTimeSpan.Zero;
      NanosecondTickCount? previousCpuEnd = null;
      for (long k = 0; ; ++k, ++frameIndex, animationTime += refresh)
      {
        if (k > 0)
        {
          slot += 1;
          planned += 1;
        }
        long intendedSlot = planned;
        if (replan)
        {
          planned = slot;
          replan = false;
        }
        // The CPU starts each frame one refresh before the vsync it is rendered for, but not before it has finished the previous frame (a
        // skipped frame can leave two frames planned for one vsync), and is busy for 60 % of a refresh (cut to the nanosecond); a stalled
        // frame is busy that many refreshes longer, so it is shown late and the next frame starts late
        var intendedDisplayTime = PacerEpoch + new NanosecondTimeSpan(intendedSlot * refresh.Nanoseconds);
        var cpuStartTime = intendedDisplayTime - refresh;
        if (previousCpuEnd is { } busyUntil && busyUntil > cpuStartTime)
          cpuStartTime = busyUntil;
        var cpuBusy = new NanosecondTimeSpan(refresh.Nanoseconds * 6 / 10);
        if (k > 0 && o.StallEvery > 0 && k % o.StallEvery == 0)
        {
          slot += o.StallSlots;
          planned += o.StallSlots;
          cpuBusy += new NanosecondTimeSpan(o.StallSlots * refresh.Nanoseconds);
        }
        previousCpuEnd = cpuStartTime + cpuBusy;
        var displayTime = new NanosecondTickCount(slot * refresh.Nanoseconds);
        if (displayTime >= totalEnd)
          break;

        // Vsync off: the frame is presented part way through the scanout, so the scanout shows the old frame above and the new one below
        if (o.TearEvery > 0 && k > 0 && k % o.TearEvery == 0)
          displayTime += new NanosecondTimeSpan((long)Math.Round(o.TearFraction * refresh.Nanoseconds));

        // Skipped frames are rendered (frame index and animation advance) but never shown: the next frame takes this vsync.
        if (o.SkipEvery > 0 && k > 0 && k % o.SkipEvery == 0)
        {
          slot -= 1;
          replan = true;
          continue;
        }

        // Idle frame markers (run id 0) before the start and after the end marker
        bool idle = displayTime < leadInEnd || displayTime >= endEnd;
        var kind =
          idle ? MarkerKind.Frame
          : displayTime < startEnd ? MarkerKind.SequenceStart
          : displayTime < runEnd ? MarkerKind.Frame
          : MarkerKind.SequenceEnd;
        // The pacer's clock has its own epoch: intended display times never start at 0 (which means unknown). The game aims for one frame
        // per refresh, and that is also the rate it prefers
        var payload = o.PacingInformation
          ? new MarkerPayload(
            kind,
            idle ? 0u : o.RunId,
            frameIndex,
            MB.FramePacing.Marker.MarkerFlags.NoFlags,
            animationTime,
            PreferredFrameTime: new NanosecondTimeDuration(refresh),
            TargetFrameTime: new NanosecondTimeDuration(refresh),
            IntendedDisplayTime: intendedDisplayTime,
            CpuStartTime: cpuStartTime,
            CpuBusy: new NanosecondTimeDuration(cpuBusy)
          )
          : new MarkerPayload(kind, idle ? 0u : o.RunId, frameIndex, MB.FramePacing.Marker.MarkerFlags.NoFlags, animationTime);
        m_presented.Add(new SyntheticPresentedFrame(payload, displayTime));
      }
    }

    /// <summary>A length in seconds, rounded to the nearest nanosecond.</summary>
    private static NanosecondTimeSpan Seconds(double seconds) =>
      new NanosecondTimeSpan((long)Math.Round(seconds * NanosecondTimeSpan.NanosecondsPerSecond));
  }
}
