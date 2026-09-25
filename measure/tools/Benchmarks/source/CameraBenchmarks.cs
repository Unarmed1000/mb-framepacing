//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL camera support: rectifying a camera frame (per capture, the C# path for sources that do not use ffmpeg), refining a zone's
//* transform and calibrating a rig (one-off), and rendering the synthetic camera (tests only).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using BenchmarkDotNet.Attributes;
using MB.FramePacing.Capture.Camera;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Benchmarks
{
  [MemoryDiagnoser]
  public class CameraBenchmarks
  {
    private BenchmarkData m_data = null!;
    private GrayImage m_rectified = null!;
    private GrayImage m_render = null!;
    private ModuleMatrix m_modules = null!;

    [GlobalSetup]
    public void Setup()
    {
      m_data = BenchmarkData.Instance;
      m_rectified = new GrayImage(m_data.Rectifier.Width, m_data.Rectifier.Height);
      m_render = new GrayImage(m_data.CameraFrame.Width, m_data.CameraFrame.Height);
      m_modules = MarkerRenderer.GenerateModules(m_data.ZonePayload);
    }

    [Benchmark(Description = "Rectify both zones of a camera frame (per capture)")]
    public void Rectify() => m_data.Rectifier.Rectify(m_data.CameraFrame, m_rectified);

    [Benchmark(Description = "Refine one zone transform (calibration)")]
    public double Refine() => HomographyRefiner.Refine(m_data.CameraFrame, m_data.Rig.Zones[0].ModuleToCamera, m_modules).RmsResidual;

    [Benchmark(Description = "Calibrate a rig from 500 camera frames (one-off)")]
    public int Calibrate() => CameraCalibrator.Calibrate(m_data.CalibrationFrames, new CameraCalibratorOptions()).Zones.Count;

    [Benchmark(Description = "Render one synthetic camera frame (tests only)")]
    public void RenderSynthetic() => m_data.Camera.Render(410, m_render);
  }
}
