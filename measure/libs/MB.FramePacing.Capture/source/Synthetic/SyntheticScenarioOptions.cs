//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options for the synthetic test game: capture and display rates, run length, stalls, skipped frames, run id and name, and the stored frame
//* size.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Capture.Synthetic
{
  public sealed record SyntheticScenarioOptions
  {
    /// <summary>
    /// Capture rate. A capture card captures at the display's native refresh rate, so for <see cref="SyntheticCaptureSource"/> it must equal
    /// <see cref="RefreshHz"/>; only the synthetic camera films faster.
    /// </summary>
    public double CaptureFps { get; init; } = 60;
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

    /// <summary>The start marker's sequence id: a text tag of at most 16 printable ASCII characters.</summary>
    public string SequenceTag { get; init; } = "synthetic";
    public long RunStartUtcTicks { get; init; } = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc).Ticks;

    /// <summary>
    /// Write the pacer's intended display time (the vsync each frame is rendered for), target frame time (one refresh), CPU start time
    /// (one refresh before the intended display time) and CPU busy (60 % of a refresh, longer when stalled) into the markers.
    /// </summary>
    public bool PacingInformation { get; init; } = true;

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
