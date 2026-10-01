//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options for FrameRecorder: ring size, pre-roll while waiting for the start marker, and whether a full ring makes the source wait (files) or
//* drops frames (live sources).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Capture
{
  public sealed record FrameRecorderOptions
  {
    /// <summary>Number of frames the ring can hold. Size it for the longest stall of the disk (default: one second of frames).</summary>
    public int RingFrames { get; init; } = 256;

    /// <summary>
    /// Start in armed mode: frames are held back until the <see cref="Inspector"/> reports a start marker (or until
    /// <see cref="FrameRecorder.StartWriting"/>).
    /// </summary>
    public bool StartArmed { get; init; }

    /// <summary>
    /// Looks at every frame, in order, before it is written or discarded; its start/end triggers drive <see cref="StartArmed"/> and
    /// <see cref="StopAtEnd"/>. Null = no inspection.
    /// </summary>
    public IFrameInspector? Inspector { get; init; }

    /// <summary>
    /// Reads every frame's markers, in order, before the frame is written or discarded: the capture data (captures.mbcd), and what the
    /// <see cref="Inspector"/> gets. Needed to record the capture data. Null = no decoding.
    /// </summary>
    public LiveFrameDecoder? Decoder { get; init; }

    /// <summary>Stop writing <see cref="EndTailFrames"/> frames after the frame in which the inspector found the end marker.</summary>
    public bool StopAtEnd { get; init; }

    /// <summary>Frames written after the end marker frame (so the capture shows how the run ended).</summary>
    public int EndTailFrames { get; init; } = 32;

    /// <summary>Frames before the trigger that are kept when armed.</summary>
    public int PreRollFrames { get; init; } = 64;

    /// <summary>
    /// How long the writer waits for a late device timestamp before writing the record without one. A live source must not stall; a
    /// source that is not live (<see cref="WaitWhenFull"/>) is given <see cref="NotLiveDeviceTimeWait"/>, since one record without a
    /// device timestamp makes the analysis fall back to the host clock for the whole capture.
    /// </summary>
    public TimeSpan DeviceTimeWait { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The <see cref="DeviceTimeWait"/> for sources that are not live.</summary>
    public static readonly TimeSpan NotLiveDeviceTimeWait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Make the source wait for free ring space instead of dropping the frame (for sources that are not live, e.g. video files, where
    /// waiting costs nothing and every frame should be kept).
    /// </summary>
    public bool WaitWhenFull { get; init; }

    /// <summary>Upper bound on records per write call.</summary>
    public int MaxBatchRecords { get; init; } = 64;

    /// <summary>How often a copy of the newest frame is kept for the live preview (display only; the triggers see every frame), zero to disable.</summary>
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
