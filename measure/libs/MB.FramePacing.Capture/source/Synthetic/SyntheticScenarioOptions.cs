//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options for the synthetic test game: capture and display rates, run length, stalls, skipped frames, run id and name, and the stored frame
//* size.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

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

    /// <summary>Idle frame markers (run id 0) before the start marker, like an application in its menus.</summary>
    public double LeadInSeconds { get; init; }

    /// <summary>Length of the measured part (frame markers).</summary>
    public double RunSeconds { get; init; } = 2;
    public double StartMarkerSeconds { get; init; } = 0.3;
    public double EndMarkerSeconds { get; init; } = 0.3;

    /// <summary>Idle frame markers (run id 0) after the end marker.</summary>
    public double TailSeconds { get; init; }

    public uint RunId { get; init; } = 1;
    public string RunName { get; init; } = "synthetic";
    public long RunStartUtcTicks { get; init; } = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc).Ticks;

    /// <summary>Every n-th frame misses <see cref="StallSlots"/> extra vsyncs (0 = never).</summary>
    public int StallEvery { get; init; }
    public int StallSlots { get; init; } = 1;

    /// <summary>Every n-th frame is rendered but never presented (0 = never).</summary>
    public int SkipEvery { get; init; }

    /// <summary>
    /// Every n-th frame is presented <see cref="TearFraction"/> of a refresh after its vsync, as with vsync off (0 = never). A capture card
    /// sees it from the next scanout on; a camera sees a tear.
    /// </summary>
    public int TearEvery { get; init; }
    public double TearFraction { get; init; } = 0.5;

    /// <summary>Offset of the capture clock against vsync, as a fraction of a capture period.</summary>
    public double CapturePhase { get; init; } = 0.37;

    /// <summary>First application frame index (tests a frame index that does not start at zero).</summary>
    public ulong FirstFrameIndex { get; init; } = 1000;

    public double TotalSeconds => LeadInSeconds + StartMarkerSeconds + RunSeconds + EndMarkerSeconds + TailSeconds;
  }
}
