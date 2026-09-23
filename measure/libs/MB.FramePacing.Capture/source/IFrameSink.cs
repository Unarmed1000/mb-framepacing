//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Receives frames from a source, in capture order, on the source's thread.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Capture
{
  /// <summary>Receives frames from a source, in capture order, on the source's thread.</summary>
  public interface IFrameSink
  {
    /// <summary>
    /// Get the buffer for the next frame (<see cref="CaptureFormat.PixelByteCount"/> bytes, rows packed). Always succeeds: when the recorder
    /// cannot keep up it hands out a scratch buffer and the frame is counted as dropped.
    /// </summary>
    Span<byte> BeginFrame();

    /// <summary>Complete the frame started with <see cref="BeginFrame"/>. Every call advances the capture index by one.</summary>
    /// <param name="hostTicks">Capture clock ticks when the frame arrived.</param>
    /// <param name="deviceTicks">Device timestamp in TimeSpan ticks, <see cref="CaptureRecordHeader.UnknownTicks"/> or
    /// <see cref="DeviceTimestamps.PendingTicks"/> when the source resolves it later through <see cref="IDeviceTimestampSource"/>.</param>
    void EndFrame(long hostTicks, long deviceTicks, CaptureRecordFlags flags);
  }
}
