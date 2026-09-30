//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A deterministic model of "a game presenting frames on vsync, captured by a capture card". It produces the ground truth the analyzer must
//* recover: which application frame is on screen at every capture instant.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using MB.FramePacing.Marker;

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

    public long RefreshIntervalTicks => (long)Math.Round(TimeSpan.TicksPerSecond / Options.RefreshHz);

    /// <summary>Where the synthetic pacer's steady clock starts.</summary>
    public const long PacerEpochTicks = TimeSpan.TicksPerSecond;

    /// <summary>Capture instant of capture index <paramref name="captureIndex"/>, in TimeSpan ticks.</summary>
    public long CaptureTicks(long captureIndex) =>
      (long)Math.Round((captureIndex + Options.CapturePhase) * TimeSpan.TicksPerSecond / Options.CaptureFps);

    /// <summary>Index into <see cref="PresentedFrames"/> of the frame on screen at a capture, -1 if nothing is shown yet.</summary>
    public int PresentedIndexAt(long captureIndex) => PresentedIndexAtTicks(CaptureTicks(captureIndex));

    /// <summary>Index into <see cref="PresentedFrames"/> of the latest frame presented at or before <paramref name="ticks"/>, -1 if none.</summary>
    public int PresentedIndexAtTicks(long ticks)
    {
      int lo = 0;
      int hi = m_presented.Count - 1;
      int found = -1;
      while (lo <= hi)
      {
        int mid = (lo + hi) / 2;
        if (m_presented[mid].DisplayTicks <= ticks)
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
      long refresh = RefreshIntervalTicks;
      long leadInEnd = SecondsToTicks(o.LeadInSeconds);
      long startEnd = leadInEnd + SecondsToTicks(o.StartMarkerSeconds);
      long runEnd = startEnd + SecondsToTicks(o.RunSeconds);
      long endEnd = runEnd + SecondsToTicks(o.EndMarkerSeconds);
      long totalEnd = SecondsToTicks(o.TotalSeconds);

      long slot = 0;
      // The pacer's plan: every frame one vsync after the previous one. A stall shows a frame later than planned, a skipped frame (replaced
      // before its scanout) makes the next one show a vsync early; either way the pacer plans on from the frame it actually showed
      long planned = 0;
      bool replan = false;
      ulong frameIndex = o.FirstFrameIndex;
      long animationTicks = 0;
      long previousCpuEnd = long.MinValue;
      for (long k = 0; ; ++k, ++frameIndex, animationTicks += refresh)
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
        // skipped frame can leave two frames planned for one vsync), and is busy for 60 % of a refresh; a stalled frame is busy that many
        // refreshes longer, so it is shown late and the next frame starts late
        long intendedTicks = PacerEpochTicks + (intendedSlot * refresh);
        long cpuStartTicks = Math.Max(intendedTicks - refresh, previousCpuEnd);
        long cpuBusyTicks = refresh * 6 / 10;
        if (k > 0 && o.StallEvery > 0 && k % o.StallEvery == 0)
        {
          slot += o.StallSlots;
          planned += o.StallSlots;
          cpuBusyTicks += o.StallSlots * refresh;
        }
        previousCpuEnd = cpuStartTicks + cpuBusyTicks;
        long displayTicks = slot * refresh;
        if (displayTicks >= totalEnd)
          break;

        // Vsync off: the frame is presented part way through the scanout, so the scanout shows the old frame above and the new one below
        if (o.TearEvery > 0 && k > 0 && k % o.TearEvery == 0)
          displayTicks += (long)Math.Round(o.TearFraction * refresh);

        // Skipped frames are rendered (frame index and animation advance) but never shown: the next frame takes this vsync.
        if (o.SkipEvery > 0 && k > 0 && k % o.SkipEvery == 0)
        {
          slot -= 1;
          replan = true;
          continue;
        }

        // Idle frame markers (run id 0) before the start and after the end marker
        bool idle = displayTicks < leadInEnd || displayTicks >= endEnd;
        var kind =
          idle ? MarkerKind.Frame
          : displayTicks < startEnd ? MarkerKind.SequenceStart
          : displayTicks < runEnd ? MarkerKind.Frame
          : MarkerKind.SequenceEnd;
        // The pacer's clock has its own epoch: intended display times never start at 0 (which means unknown). The game aims for one frame
        // per refresh, and that is also the rate it prefers
        var payload = o.PacingInformation
          ? new MarkerPayload(
            kind,
            idle ? 0u : o.RunId,
            frameIndex,
            MB.FrameMarker.MarkerFlags.None,
            animationTicks,
            PreferredFrameTicks: (uint)refresh,
            TargetFrameTicks: (uint)refresh,
            IntendedDisplayTicks: intendedTicks,
            CpuStartTicks: cpuStartTicks,
            CpuBusyTicks: (uint)cpuBusyTicks
          )
          : new MarkerPayload(kind, idle ? 0u : o.RunId, frameIndex, MB.FrameMarker.MarkerFlags.None, animationTicks);
        m_presented.Add(new SyntheticPresentedFrame(payload, displayTicks));
      }
    }

    private static long SecondsToTicks(double seconds) => (long)Math.Round(seconds * TimeSpan.TicksPerSecond);
  }
}
