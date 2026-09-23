//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Contracts between capture sources (ffmpeg, synthetic, future vendor SDKs) and the recorder.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Diagnostics;
using System.Threading;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture
{
  /// <summary>The stored frame format a source delivers.</summary>
  /// <param name="Width">Stored frame width (after crop and scale).</param>
  /// <param name="Height">Stored frame height (after crop and scale).</param>
  /// <param name="SourceWidth">Device mode width before crop/scale, 0 if unknown.</param>
  /// <param name="SourceHeight">Device mode height before crop/scale, 0 if unknown.</param>
  /// <param name="Roi">Crop in source pixels applied before scaling, empty if none.</param>
  public sealed record CaptureFormat(int Width, int Height, FrameRate FrameRate, int SourceWidth = 0, int SourceHeight = 0, PixelRect Roi = default)
  {
    public int PixelByteCount => checked(Width * Height);

    public CaptureFileHeader ToFileHeader() => new CaptureFileHeader(Width, Height, FrameRate, SourceWidth, SourceHeight, Roi);
  }

  /// <summary>Monotonic capture clock in TimeSpan ticks, shared by a source and the recorder.</summary>
  public sealed class CaptureClock
  {
    private readonly long m_startTimestamp = Stopwatch.GetTimestamp();

    public long NowTicks => Stopwatch.GetElapsedTime(m_startTimestamp).Ticks;
  }

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

  /// <summary>Resolves device timestamps that arrive after the pixels (for example ffmpeg's showinfo lines on stderr).</summary>
  public interface IDeviceTimestampSource
  {
    bool TryGetDeviceTicks(long captureIndex, out long deviceTicks);
  }

  public static class DeviceTimestamps
  {
    /// <summary>Passed to <see cref="IFrameSink.EndFrame"/> when the device timestamp will be supplied later.</summary>
    public const long PendingTicks = long.MinValue + 1;
  }

  public interface ICaptureSource : IDisposable
  {
    /// <summary>A short human readable description, for logs and capture.json.</summary>
    string Description { get; }

    CaptureFormat Format { get; }

    /// <summary>Late device timestamps, or null if the source always passes them to EndFrame directly.</summary>
    IDeviceTimestampSource? DeviceTimestamps { get; }

    /// <summary>Frames the source itself reported as dropped (driver/ffmpeg buffers), as far as it can tell.</summary>
    long SourceDroppedFrames { get; }

    /// <summary>
    /// True for sources that deliver frames in real time and can not wait (capture cards, network streams): when the recorder falls behind,
    /// frames are dropped and counted. False for files and image sequences: the recorder makes the source wait instead, so nothing is lost.
    /// </summary>
    bool IsLive => true;

    /// <summary>Deliver frames to the sink on the calling thread until cancelled or the source ends.</summary>
    void Run(IFrameSink sink, CaptureClock clock, CancellationToken cancellationToken);
  }
}
