//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Marker decoding per capture: the locked paths the analysis runs on every capture, and the full detector search used when locating.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using BenchmarkDotNet.Attributes;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Benchmarks
{
  [MemoryDiagnoser]
  public class DecodeBenchmarks
  {
    private readonly MarkerDecoder m_decoder = new MarkerDecoder();
    private readonly MarkerDecoder m_cameraDecoder = new MarkerDecoder(sampleModuleGrid: true);
    private readonly MarkerDecoder m_searchDecoder = new MarkerDecoder(tryHarder: true);
    private BenchmarkData m_data = null!;

    [GlobalSetup]
    public void Setup() => m_data = BenchmarkData.Instance;

    [Benchmark(Description = "Card: locked decode (pure barcode)")]
    public bool CardLocked() => m_decoder.DecodeLocked(m_data.CardFrame, m_data.CardLock).IsDecoded;

    [Benchmark(Description = "Card: full detector search (960x540)")]
    public bool CardSearch() => m_searchDecoder.Decode(m_data.CardFrame).IsDecoded;

    [Benchmark(Description = "Camera: locked decode of one rectified zone")]
    public bool CameraZoneLocked() => m_cameraDecoder.DecodeLocked(m_data.Rectified, m_data.ZoneLock).IsDecoded;

    [Benchmark(Description = "Camera: DecodeEach on a whole camera frame (calibration)")]
    public int CameraFrameSearch() => m_searchDecoder.DecodeEach(m_data.CameraFrame, 3).Count;
  }
}
