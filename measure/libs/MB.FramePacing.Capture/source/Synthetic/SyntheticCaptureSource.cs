//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Capture source that renders a SyntheticScenario like a capture card, at the display's native refresh rate: frames carry real markers,
//* device ticks are the exact simulated capture instants. Runs either paced in real time (to exercise the recorder like real hardware) or as
//* fast as possible.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Threading;
using MB.FramePacing.Data;
using MB.FramePacing.MarkerDecoding;

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
      if (Math.Abs(o.CaptureFps - o.RefreshHz) > 1e-9 * o.RefreshHz)
        throw new ArgumentException(
          $"A capture card captures at the display's native refresh rate: capture rate {o.CaptureFps:0.###} fps differs from the {o.RefreshHz:0.###} Hz refresh"
        );
      int maxMarker = MarkerRenderer.MarkerSizePx(o.ModuleSizePx);
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
      var runStart = clock.Now;

      for (long captureIndex = 0; captureIndex < m_scenario.CaptureCount && !cancellationToken.IsCancellationRequested; ++captureIndex)
      {
        var captureTime = m_scenario.CaptureTime(captureIndex);
        if (m_paced)
          WaitUntil(clock, runStart + captureTime.ToTimeSpan(), cancellationToken);

        int presentedIndex = m_scenario.PresentedIndexAt(captureIndex);
        if (presentedIndex != shownIndex)
        {
          RenderFrame(frame, presentedIndex);
          shownIndex = presentedIndex;
        }

        var dst = sink.BeginFrame();
        frame.Pixels.AsSpan(0, Format.PixelByteCount).CopyTo(dst);
        sink.EndFrame(clock.Now, new DeviceTimestamp(captureTime), 0);
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

    private static void WaitUntil(CaptureClock clock, TickCount64 target, CancellationToken cancellationToken)
    {
      while (!cancellationToken.IsCancellationRequested)
      {
        var remaining = target - clock.Now;
        if (remaining <= TimeSpan.Zero)
          return;
        if (remaining > TimeSpan.FromMilliseconds(2))
          Thread.Sleep(1);
        else
          Thread.SpinWait(64);
      }
    }
  }
}
