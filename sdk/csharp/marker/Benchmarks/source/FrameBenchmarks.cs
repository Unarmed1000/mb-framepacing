//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A whole frame as an application draws it: encode the main and the sync marker of a new frame and draw both, as triangles or as the
//* static grid's per-frame indices (the grid itself is built once, outside the measurement).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using BenchmarkDotNet.Attributes;

namespace MB.FramePacing.Marker.Benchmarks
{
  [MemoryDiagnoser]
  public class FrameBenchmarks
  {
    private static readonly Options g_options = Options.Default;
    private static readonly Point g_mainOrigin = g_options.RecommendedOrigin(MarkerKind.Frame, 1080);
    private static readonly Point g_syncOrigin = g_options.RecommendedOrigin(MarkerKind.Sync, 1080);

    private readonly MarkerGenerator m_generator = new MarkerGenerator();
    private readonly byte[] m_mainBits = new byte[ModuleMatrix.MaxPackedModuleByteCount];
    private readonly byte[] m_syncBits = new byte[ModuleMatrix.MaxPackedModuleByteCount];
    private readonly Vertex[] m_mainVertices = new Vertex[FrameMarker.MaxTriangleVertexCount];
    private readonly Vertex[] m_syncVertices = new Vertex[FrameMarker.MaxTriangleVertexCount];
    private readonly int[] m_mainIndices = new int[FrameMarker.MaxIndexCount];
    private readonly int[] m_syncIndices = new int[FrameMarker.MaxIndexCount];
    private ulong m_frameIndex;

    [Benchmark]
    public int FrameAsTriangles()
    {
      Payload payload = BenchmarkMarkers.FramePayload(m_frameIndex++);
      m_generator.TryGenerateModules(payload, m_mainBits, out var main);
      int count = FrameMarker.ModulesToTriangles(main, g_options, g_mainOrigin, m_mainVertices);
      m_generator.TryGenerateModules(payload.WithKind(MarkerKind.Sync), m_syncBits, out var sync);
      return count + FrameMarker.ModulesToTriangles(sync, g_options, g_syncOrigin, m_syncVertices);
    }

    [Benchmark]
    public int FrameAsGridIndices()
    {
      Payload payload = BenchmarkMarkers.FramePayload(m_frameIndex++);
      m_generator.TryGenerateModules(payload, m_mainBits, out var main);
      int count = FrameMarker.ModulesToGridIndices(main, m_mainIndices);
      m_generator.TryGenerateModules(payload.WithKind(MarkerKind.Sync), m_syncBits, out var sync);
      return count + FrameMarker.ModulesToGridIndices(sync, m_syncIndices);
    }
  }
}
