//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Capture source that renders a SyntheticScenario: frames carry real markers, device ticks are the exact simulated capture instants. Runs
//* either paced in real time (to exercise the recorder like real hardware) or as fast as possible.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Threading;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture.Synthetic
{
  public sealed class SyntheticCaptureSource : ICaptureSource
  {
    private const byte BackgroundLuma = 96;

    private readonly SyntheticScenario m_scenario;
    private readonly bool m_paced;

    public SyntheticCaptureSource(SyntheticScenario scenario, bool paced)
    {
      m_scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
      m_paced = paced;
      var o = scenario.Options;
      int maxMarker = MarkerRenderer.MaxMarkerSizePx(o.ModuleSizePx);
      if (o.OriginX + maxMarker > o.Width || o.OriginY + maxMarker > o.Height)
        throw new ArgumentException($"A {o.Width}x{o.Height} frame is too small for a start marker of {maxMarker}px at ({o.OriginX},{o.OriginY})");
      Format = new CaptureFormat(o.Width, o.Height, FrameRate.FromFps(o.CaptureFps), o.Width, o.Height);
    }

    public string Description => $"synthetic {m_scenario.Options.RefreshHz:0.##}Hz game captured at {m_scenario.Options.CaptureFps:0.##}fps";

    public CaptureFormat Format { get; }

    public IDeviceTimestampSource? DeviceTimestamps => null;

    public long SourceDroppedFrames => 0;

    public bool IsLive => m_paced;

    public SyntheticScenario Scenario => m_scenario;

    public void Run(IFrameSink sink, CaptureClock clock, CancellationToken cancellationToken)
    {
      var o = m_scenario.Options;
      var frame = new GrayImage(o.Width, o.Height, BackgroundLuma);
      int shownIndex = int.MinValue;
      long runStartTicks = clock.NowTicks;

      for (long captureIndex = 0; captureIndex < m_scenario.CaptureCount && !cancellationToken.IsCancellationRequested; ++captureIndex)
      {
        long deviceTicks = m_scenario.CaptureTicks(captureIndex);
        if (m_paced)
          WaitUntil(clock, runStartTicks + deviceTicks, cancellationToken);

        int presentedIndex = m_scenario.PresentedIndexAt(captureIndex);
        if (presentedIndex != shownIndex)
        {
          RenderFrame(frame, presentedIndex);
          shownIndex = presentedIndex;
        }

        var dst = sink.BeginFrame();
        frame.Pixels.AsSpan(0, Format.PixelByteCount).CopyTo(dst);
        sink.EndFrame(clock.NowTicks, deviceTicks, CaptureRecordFlags.None);
      }
    }

    public void Dispose() { }

    private void RenderFrame(GrayImage frame, int presentedIndex)
    {
      var o = m_scenario.Options;
      Array.Fill(frame.Pixels, BackgroundLuma);
      if (presentedIndex < 0)
        return;
      var payload = m_scenario.PresentedFrames[presentedIndex].Payload;
      var metadata = payload.Kind == MarkerKind.SequenceStart ? m_scenario.StartMetadata : null;
      MarkerRenderer.Render(frame, payload, o.OriginX, o.OriginY, o.ModuleSizePx, MarkerRenderer.RecommendedQuietZoneModules, metadata);
    }

    private static void WaitUntil(CaptureClock clock, long targetTicks, CancellationToken cancellationToken)
    {
      while (!cancellationToken.IsCancellationRequested)
      {
        long remaining = targetTicks - clock.NowTicks;
        if (remaining <= 0)
          return;
        if (remaining > TimeSpan.TicksPerMillisecond * 2)
          Thread.Sleep(1);
        else
          Thread.SpinWait(64);
      }
    }
  }
}
