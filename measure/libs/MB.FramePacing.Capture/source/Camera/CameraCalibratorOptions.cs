//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options for calibrating and verifying a camera rig (EXPERIMENTAL camera support).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Capture.Camera
{
  public sealed record CameraCalibratorOptions
  {
    /// <summary>Seconds of camera frames to calibrate from (at the source's nominal rate).</summary>
    public double Seconds { get; init; } = 2;

    /// <summary>
    /// Seconds of camera frames a verification looks at. Long enough to get past a start marker: in the BottomLeft slot it is larger than the
    /// frame marker and usually runs off the screen, so only frame markers show that zone.
    /// </summary>
    public double VerifySeconds { get; init; } = 1;

    /// <summary>Most pixel memory the collected frames may use.</summary>
    public long MemoryBudgetBytes { get; init; } = 1L << 30;

    /// <summary>Frames searched for the markers with the full detector (the slow part).</summary>
    public int GeometryFrames { get; init; } = 24;

    /// <summary>Detections per zone the transform is refined on.</summary>
    public int RefineFrames { get; init; } = 8;

    /// <summary>Give up when the source delivers nothing for this long.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The live device mode and input format, recorded in the rig for live captures.</summary>
    public string? Mode { get; init; }
    public string? InputFormat { get; init; }
  }
}
