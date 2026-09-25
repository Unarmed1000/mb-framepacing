//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* MarkerProbe: finds the marker of a synthetic source, reports a source without a marker, and refuses a marker that moves.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Threading;
using MB.FramePacing.Capture.Synthetic;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class MarkerProbeTests
  {
    private static readonly TimeSpan g_timeout = TimeSpan.FromSeconds(30);

    [Test]
    public void Locate_FindsTheSyntheticMarker()
    {
      var scenario = new SyntheticScenario(
        new SyntheticScenarioOptions
        {
          Width = 400,
          Height = 240,
          ModuleSizePx = 4,
          OriginX = 20,
          OriginY = 16,
          RunSeconds = 0.5,
        }
      );
      using var source = new SyntheticCaptureSource(scenario, paced: false);

      var markerLock = MarkerProbe.Locate(source, g_timeout, TimeSpan.Zero, CancellationToken.None);

      Assert.That(markerLock.Bounds.X, Is.EqualTo(20).Within(1));
      Assert.That(markerLock.Bounds.Y, Is.EqualTo(16).Within(1));
      Assert.That(markerLock.ModuleSizePx, Is.EqualTo(4).Within(0.2));
      Assert.That(markerLock.Bounds.Width, Is.EqualTo(MarkerRenderer.MarkerSizePx(4)).Within(2), "a frame marker lock, also for a start marker");
    }

    [Test]
    public void Locate_NoMarker_Throws()
    {
      using var source = new ScriptedSource(frames: 20, _ => null);
      var ex = Assert.Throws<TimeoutException>(() => MarkerProbe.Locate(source, g_timeout, TimeSpan.Zero, CancellationToken.None));
      Assert.That(ex!.Message, Does.Contain("No marker was found in 20 frames"));
    }

    [Test]
    public void Locate_MovingMarker_Throws()
    {
      using var source = new ScriptedSource(frames: 20, frame => frame % 2 == 0 ? (12, 12) : (140, 12));
      var ex = Assert.Throws<InvalidOperationException>(() => MarkerProbe.Locate(source, g_timeout, TimeSpan.Zero, CancellationToken.None));
      Assert.That(ex!.Message, Does.Contain("moved"));
    }

    /// <summary>Frame markers at a scripted position per frame (null: no marker).</summary>
    private sealed class ScriptedSource(int frames, Func<int, (int X, int Y)?> position) : ICaptureSource
    {
      private const int ModulePx = 3;

      public string Description => "scripted";

      public CaptureFormat Format { get; } = new CaptureFormat(320, 180, FrameRate.FromFps(240));

      public IDeviceTimestampSource? DeviceTimestamps => null;

      public long SourceDroppedFrames => 0;

      public void Run(IFrameSink sink, CaptureClock clock, CancellationToken cancellationToken)
      {
        var image = new GrayImage(Format.Width, Format.Height);
        for (int i = 0; i < frames && !cancellationToken.IsCancellationRequested; ++i)
        {
          Array.Fill(image.Pixels, (byte)96);
          if (position(i) is { } origin)
            MarkerRenderer.Render(image, new MarkerPayload((ulong)i, i * 41_667L, 1, MarkerKind.Frame), origin.X, origin.Y, ModulePx);
          image.Pixels.CopyTo(sink.BeginFrame());
          sink.EndFrame(clock.NowTicks, i * 41_667L, CaptureRecordFlags.None);
        }
      }

      public void Dispose() { }
    }
  }
}
