//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Drawing into a pixel buffer exactly the marker's size, in every pixel format: module size 1 (a module-resolution texture to scale up)
//* and 6 (the default).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using BenchmarkDotNet.Attributes;

namespace MB.FramePacing.Marker.Benchmarks
{
  [MemoryDiagnoser]
  public class BitmapBenchmarks
  {
    private readonly byte[] m_bits = new byte[ModuleMatrix.MaxPackedModuleByteCount];
    private byte[] m_pixels = System.Array.Empty<byte>();
    private Options m_options;
    private int m_size;
    private int m_markerSizePx;

    [Params(PixelFormat.R8, PixelFormat.R8G8B8, PixelFormat.R8G8B8A8)]
    public PixelFormat Format { get; set; }

    [Params(1, Options.DefaultModuleSizePx)]
    public int ModuleSizePx { get; set; }

    [GlobalSetup]
    public void Setup()
    {
      m_size = BenchmarkMarkers.Encode(MarkerKind.Frame, m_bits);
      m_options = new Options(ModuleSizePx);
      m_markerSizePx = m_options.MarkerSizePx();
      m_pixels = new byte[m_markerSizePx * m_markerSizePx * PixelFormatUtil.BytesPerPixel(Format)];
    }

    [Benchmark]
    public bool ModulesToBitmap()
    {
      ModuleMatrix.TryFromBits(m_size, m_bits, out var matrix);
      return FrameMarker.ModulesToBitmap(matrix, m_options, default, m_pixels, m_markerSizePx, m_markerSizePx, Format);
    }
  }
}
