//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Wraps a capture source that delivers whole camera frames and passes on only the rig's rectified zones (EXPERIMENTAL camera support), like
//* the ffmpeg camera filter does for ffmpeg sources.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Threading;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture.Camera
{
  public sealed class RectifyingCaptureSource : ICaptureSource
  {
    private readonly ICaptureSource m_inner;
    private readonly CameraRectifier m_rectifier;

    public RectifyingCaptureSource(ICaptureSource inner, CameraRig rig)
    {
      m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
      m_rectifier = new CameraRectifier(rig);
      var format = inner.Format;
      Format = new CaptureFormat(m_rectifier.Width, m_rectifier.Height, format.FrameRate, format.Width, format.Height);
    }

    public string Description => m_inner.Description + " (camera zones rectified)";

    public CaptureFormat Format { get; }

    public IDeviceTimestampSource? DeviceTimestamps => m_inner.DeviceTimestamps;

    public long SourceDroppedFrames => m_inner.SourceDroppedFrames;

    public bool IsLive => m_inner.IsLive;

    public void Run(IFrameSink sink, CaptureClock clock, CancellationToken cancellationToken) =>
      m_inner.Run(new Sink(sink, m_rectifier, m_inner.Format.Width, m_inner.Format.Height), clock, cancellationToken);

    public void Dispose() => m_inner.Dispose();

    private sealed class Sink : IFrameSink
    {
      private readonly IFrameSink m_target;
      private readonly CameraRectifier m_rectifier;
      private readonly GrayImage m_camera;
      private readonly GrayImage m_stored;

      public Sink(IFrameSink target, CameraRectifier rectifier, int width, int height)
      {
        m_target = target;
        m_rectifier = rectifier;
        m_camera = new GrayImage(width, height);
        m_stored = new GrayImage(rectifier.Width, rectifier.Height);
      }

      public Span<byte> BeginFrame() => m_camera.Pixels.AsSpan(0, m_camera.Width * m_camera.Height);

      public void EndFrame(long hostTicks, long deviceTicks, CaptureRecordFlags flags)
      {
        m_rectifier.Rectify(m_camera, m_stored);
        m_stored.Pixels.AsSpan(0, m_stored.Width * m_stored.Height).CopyTo(m_target.BeginFrame());
        m_target.EndFrame(hostTicks, deviceTicks, flags);
      }
    }
  }
}
