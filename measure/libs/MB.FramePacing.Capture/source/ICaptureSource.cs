//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Contracts between capture sources (ffmpeg, synthetic, future vendor SDKs) and the recorder.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Threading;

namespace MB.FramePacing.Capture
{
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
