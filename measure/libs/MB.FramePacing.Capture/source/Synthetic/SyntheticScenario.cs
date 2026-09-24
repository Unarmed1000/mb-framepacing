//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A deterministic model of "a game presenting frames on vsync, captured by a capture card". It produces the ground truth the analyzer must
//* recover: which application frame is on screen at every capture instant.
//*
//* (c) 2026 Mana Battery
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
      StartMetadata = new StartMetadata(options.RunStartUtcTicks, options.RunName);
      BuildTimeline();
      CaptureCount = (long)Math.Floor(options.TotalSeconds * options.CaptureFps);
    }

    public SyntheticScenarioOptions Options { get; }

    public StartMetadata StartMetadata { get; }

    /// <summary>Every presented frame in display order.</summary>
    public IReadOnlyList<SyntheticPresentedFrame> PresentedFrames => m_presented;

    public long CaptureCount { get; }

    public long RefreshIntervalTicks => (long)Math.Round(TimeSpan.TicksPerSecond / Options.RefreshHz);

    /// <summary>Capture instant of capture index <paramref name="captureIndex"/>, in TimeSpan ticks.</summary>
    public long CaptureTicks(long captureIndex) =>
      (long)Math.Round((captureIndex + Options.CapturePhase) * TimeSpan.TicksPerSecond / Options.CaptureFps);

    /// <summary>Index into <see cref="PresentedFrames"/> of the frame on screen at a capture, -1 if nothing is shown yet.</summary>
    public int PresentedIndexAt(long captureIndex)
    {
      long ticks = CaptureTicks(captureIndex);
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
      ulong frameIndex = o.FirstFrameIndex;
      long animationTicks = 0;
      for (long k = 0; ; ++k, ++frameIndex, animationTicks += refresh)
      {
        if (k > 0)
        {
          slot += 1;
          if (o.StallEvery > 0 && k % o.StallEvery == 0)
            slot += o.StallSlots;
        }
        long displayTicks = slot * refresh;
        if (displayTicks >= totalEnd)
          break;

        // Skipped frames are rendered (frame index and animation advance) but never shown: the next frame takes this vsync.
        if (o.SkipEvery > 0 && k > 0 && k % o.SkipEvery == 0)
        {
          slot -= 1;
          continue;
        }

        // Idle frame markers (run id 0) before the start and after the end marker
        bool idle = displayTicks < leadInEnd || displayTicks >= endEnd;
        var kind =
          idle ? MarkerKind.Frame
          : displayTicks < startEnd ? MarkerKind.SequenceStart
          : displayTicks < runEnd ? MarkerKind.Frame
          : MarkerKind.SequenceEnd;
        m_presented.Add(new SyntheticPresentedFrame(new MarkerPayload(frameIndex, animationTicks, idle ? 0u : o.RunId, kind), displayTicks));
      }
    }

    private static long SecondsToTicks(double seconds) => (long)Math.Round(seconds * TimeSpan.TicksPerSecond);
  }
}
