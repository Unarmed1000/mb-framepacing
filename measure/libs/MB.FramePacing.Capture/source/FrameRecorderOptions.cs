//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options for FrameRecorder: ring size, pre-roll while waiting for the start marker, and whether a full ring makes the source wait (files) or
//* drops frames (live sources).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Capture
{
  public sealed record FrameRecorderOptions
  {
    /// <summary>Number of frames the ring can hold. Size it for the longest stall of the disk (default: one second of frames).</summary>
    public int RingFrames { get; init; } = 256;

    /// <summary>Start in armed mode: frames are held back until <see cref="FrameRecorder.StartWriting"/>.</summary>
    public bool StartArmed { get; init; }

    /// <summary>Frames before the trigger that are kept when armed.</summary>
    public int PreRollFrames { get; init; } = 64;

    /// <summary>How long the writer waits for a late device timestamp before writing the record without one.</summary>
    public TimeSpan DeviceTicksWait { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Make the source wait for free ring space instead of dropping the frame (for sources that are not live, e.g. video files, where
    /// waiting costs nothing and every frame should be kept).
    /// </summary>
    public bool WaitWhenFull { get; init; }

    /// <summary>Upper bound on records per write call.</summary>
    public int MaxBatchRecords { get; init; } = 64;

    /// <summary>How often a copy of the newest frame is kept for live inspection (sequence trigger), zero to disable.</summary>
    public TimeSpan PreviewInterval { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>Ring size for a frame rate and a memory budget, clamped to [16, fps * seconds].</summary>
    public static int RingFramesFor(CaptureFormat format, double seconds = 1.0, long memoryBudgetBytes = 512L * 1024 * 1024)
    {
      double fps = format.FrameRate.IsKnown ? format.FrameRate.FramesPerSecond : 240;
      long byTime = (long)Math.Ceiling(fps * seconds);
      long byMemory = memoryBudgetBytes / format.ToFileHeader().RecordSize;
      return (int)Math.Clamp(Math.Min(byTime, byMemory), 16, int.MaxValue / 2);
    }
  }
}
