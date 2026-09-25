//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The inputs the benchmarks share: a capture card frame with its marker lock, and a synthetic high speed camera frame with a calibrated rig
//* and its rectified zones (EXPERIMENTAL camera support).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Threading;
using MB.FramePacing.Capture.Camera;
using MB.FramePacing.Capture.Synthetic;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Benchmarks
{
  internal sealed class BenchmarkData
  {
    private const int CardModulePx = 3;

    private static readonly Lazy<BenchmarkData> g_instance = new Lazy<BenchmarkData>(() => new BenchmarkData());

    private BenchmarkData()
    {
      // Capture card: a 1080p frame downscaled 2x (960x540), marker with 3 stored pixels per module at the recommended origin
      CardFrame = new GrayImage(960, 540, 96);
      MarkerRenderer.Render(CardFrame, new MarkerPayload(12345, 67890, 1), 32, 32, CardModulePx);
      int size = MarkerRenderer.MarkerSizePx(CardModulePx);
      CardLock = new MarkerLock(new PixelRect(32, 32, size, size), CardModulePx);

      // Camera: 1000 fps filming a 60 Hz screen; calibrate from half a second, then take one mid-refresh frame
      Camera = new SyntheticCamera(
        new SyntheticScenario(
          new SyntheticScenarioOptions
          {
            CaptureFps = 1000,
            RefreshHz = 60,
            StartMarkerSeconds = 0.05,
            RunSeconds = 1,
            EndMarkerSeconds = 0.05,
          }
        ),
        new SyntheticCameraOptions()
      );
      CalibrationFrames = CameraFrameSet.Collect(new SyntheticCameraSource(Camera), 0.5, 1L << 30, TimeSpan.FromMinutes(5), CancellationToken.None);
      Rig = CameraCalibrator.Calibrate(CalibrationFrames, new CameraCalibratorOptions());
      if (Rig.HasFailures)
        throw new InvalidOperationException("The synthetic camera rig did not calibrate: " + string.Join("; ", Rig.Checks));
      CameraFrame = CalibrationFrames.Frames[410];
      Rectifier = new CameraRectifier(Rig);
      Rectified = new GrayImage(Rectifier.Width, Rectifier.Height);
      Rectifier.Rectify(CameraFrame, Rectified);
      int stored = MarkerRenderer.MarkerSizePx(CameraZone.StoredPxPerModule);
      ZoneLock = new MarkerLock(
        new PixelRect(CameraZone.StoredMarkerOriginPx, CameraZone.StoredMarkerOriginPx, stored, stored),
        CameraZone.StoredPxPerModule
      );
      ZonePayload = new MarkerDecoder(sampleModuleGrid: true).DecodeLocked(Rectified, ZoneLock).Payload;
    }

    public static BenchmarkData Instance => g_instance.Value;

    public GrayImage CardFrame { get; }
    public MarkerLock CardLock { get; }

    public SyntheticCamera Camera { get; }
    public CameraFrameSet CalibrationFrames { get; }
    public CameraRig Rig { get; }
    public GrayImage CameraFrame { get; }
    public CameraRectifier Rectifier { get; }
    public GrayImage Rectified { get; }
    public MarkerLock ZoneLock { get; }
    public MarkerPayload ZonePayload { get; }
  }
}
