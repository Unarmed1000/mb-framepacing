//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Passes a source's frames through unchanged and shows each one to a callback first, so the camera wizard can show what the camera sees while
//* it calibrates or checks (VERY EXPERIMENTAL camera support). The callback runs on the source's thread and must not keep the image.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Threading;
using MB.FramePacing.Capture;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Gui
{
  public sealed class PreviewCaptureSource : ICaptureSource
  {
    private readonly ICaptureSource m_inner;
    private readonly Action<GrayImage> m_onFrame;
    private readonly GrayImage m_frame;

    public PreviewCaptureSource(ICaptureSource inner, Action<GrayImage> onFrame)
    {
      m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
      m_onFrame = onFrame ?? throw new ArgumentNullException(nameof(onFrame));
      m_frame = new GrayImage(inner.Format.Width, inner.Format.Height);
    }

    public string Description => m_inner.Description;

    public CaptureFormat Format => m_inner.Format;

    public IDeviceTimestampSource? DeviceTimestamps => m_inner.DeviceTimestamps;

    public long SourceDroppedFrames => m_inner.SourceDroppedFrames;

    public bool IsLive => m_inner.IsLive;

    /// <summary>True once a frame went through; <see cref="LastFrame"/> then holds the most recent one.</summary>
    public bool HasFrame { get; private set; }

    /// <summary>The most recent frame (the buffer is reused, so read it only after <see cref="Run"/> returned).</summary>
    public GrayImage LastFrame => m_frame;

    public void Run(IFrameSink sink, CaptureClock clock, CancellationToken cancellationToken) =>
      m_inner.Run(new Sink(this, sink), clock, cancellationToken);

    public void Dispose() => m_inner.Dispose();

    private sealed class Sink : IFrameSink
    {
      private readonly PreviewCaptureSource m_owner;
      private readonly IFrameSink m_target;

      public Sink(PreviewCaptureSource owner, IFrameSink target)
      {
        m_owner = owner;
        m_target = target;
      }

      public Span<byte> BeginFrame() => m_owner.m_frame.Pixels.AsSpan(0, m_owner.m_frame.Width * m_owner.m_frame.Height);

      public void EndFrame(long hostTicks, long deviceTicks, CaptureRecordFlags flags)
      {
        var frame = m_owner.m_frame;
        frame.Pixels.AsSpan(0, frame.Width * frame.Height).CopyTo(m_target.BeginFrame());
        m_target.EndFrame(hostTicks, deviceTicks, flags);
        m_owner.HasFrame = true;
        m_owner.m_onFrame(frame);
      }
    }
  }
}
