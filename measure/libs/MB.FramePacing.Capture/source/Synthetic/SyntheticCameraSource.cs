//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Capture source that records a SyntheticCamera: full camera frames with the camera's own (drifting) timestamps as device ticks. It runs as
//* fast as it can render (not live), like importing a high speed camera clip.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Threading;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture.Synthetic
{
  public sealed class SyntheticCameraSource : ICaptureSource
  {
    private readonly SyntheticCamera m_camera;

    public SyntheticCameraSource(SyntheticCamera camera)
    {
      m_camera = camera ?? throw new ArgumentNullException(nameof(camera));
      var o = camera.Options;
      Format = new CaptureFormat(o.CameraWidth, o.CameraHeight, FrameRate.FromFps(camera.Scenario.Options.CaptureFps), o.CameraWidth, o.CameraHeight);
    }

    public string Description =>
      $"synthetic {m_camera.Scenario.Options.RefreshHz:0.##}Hz game filmed by a {m_camera.Scenario.Options.CaptureFps:0.##}fps camera";

    public CaptureFormat Format { get; }

    public IDeviceTimestampSource? DeviceTimestamps => null;

    public long SourceDroppedFrames => 0;

    public bool IsLive => false;

    public SyntheticCamera Camera => m_camera;

    public void Run(IFrameSink sink, CaptureClock clock, CancellationToken cancellationToken)
    {
      var frame = new GrayImage(Format.Width, Format.Height);
      for (long captureIndex = 0; captureIndex < m_camera.CaptureCount && !cancellationToken.IsCancellationRequested; ++captureIndex)
      {
        m_camera.Render(captureIndex, frame);
        var dst = sink.BeginFrame();
        frame.Pixels.AsSpan(0, Format.PixelByteCount).CopyTo(dst);
        sink.EndFrame(clock.NowTicks, m_camera.CameraTicks(captureIndex), CaptureRecordFlags.None);
      }
    }

    public void Dispose() { }
  }
}
