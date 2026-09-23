//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A deterministic model of "a game presenting frames on vsync, captured by a capture card". It produces the ground truth the analyzer must
//* recover: which application frame is on screen at every capture instant.
//*
//* The game uses a fixed animation step of one refresh per frame. Every StallEvery-th frame misses StallSlots vsyncs (its presentation is late,
//* but its animation time still advances by one step), which is exactly the kind of animation error the tool measures. Every SkipEvery-th
//* frame is rendered but never presented (the frame index jumps).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture.Synthetic
{
  public sealed record SyntheticScenarioOptions
  {
    public double CaptureFps { get; init; } = 240;
    public double RefreshHz { get; init; } = 60;

    /// <summary>Stored frame size.</summary>
    public int Width { get; init; } = 320;
    public int Height { get; init; } = 180;

    /// <summary>Marker module size in stored pixels.</summary>
    public int ModuleSizePx { get; init; } = 3;
    public int OriginX { get; init; } = 12;
    public int OriginY { get; init; } = 12;

    /// <summary>Length of the measured part (frame markers).</summary>
    public double RunSeconds { get; init; } = 2;
    public double StartMarkerSeconds { get; init; } = 0.3;
    public double EndMarkerSeconds { get; init; } = 0.3;

    public uint RunId { get; init; } = 1;
    public string RunName { get; init; } = "synthetic";
    public long RunStartUtcTicks { get; init; } = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc).Ticks;

    /// <summary>Every n-th frame misses <see cref="StallSlots"/> extra vsyncs (0 = never).</summary>
    public int StallEvery { get; init; }
    public int StallSlots { get; init; } = 1;

    /// <summary>Every n-th frame is rendered but never presented (0 = never).</summary>
    public int SkipEvery { get; init; }

    /// <summary>Offset of the capture clock against vsync, as a fraction of a capture period.</summary>
    public double CapturePhase { get; init; } = 0.37;

    /// <summary>First application frame index (tests a frame index that does not start at zero).</summary>
    public ulong FirstFrameIndex { get; init; } = 1000;

    public double TotalSeconds => StartMarkerSeconds + RunSeconds + EndMarkerSeconds;
  }

  /// <summary>One application frame that reached the screen.</summary>
  public readonly record struct SyntheticPresentedFrame(MarkerPayload Payload, long DisplayTicks);

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
      long startEnd = SecondsToTicks(o.StartMarkerSeconds);
      long runEnd = startEnd + SecondsToTicks(o.RunSeconds);
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

        var kind =
          displayTicks < startEnd ? MarkerKind.SequenceStart
          : displayTicks < runEnd ? MarkerKind.Frame
          : MarkerKind.SequenceEnd;
        m_presented.Add(new SyntheticPresentedFrame(new MarkerPayload(frameIndex, animationTicks, o.RunId, kind), displayTicks));
      }
    }

    private static long SecondsToTicks(double seconds) => (long)Math.Round(seconds * TimeSpan.TicksPerSecond);
  }
}
