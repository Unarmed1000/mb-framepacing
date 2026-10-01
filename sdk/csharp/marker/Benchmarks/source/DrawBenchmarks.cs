//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Drawing from an encoded matrix, every output the marker library offers: quads, triangles, indexed triangles, and the static grid with
//* per-frame indices, for the main and the sync marker.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using BenchmarkDotNet.Attributes;

namespace MB.FramePacing.Marker.Benchmarks
{
  [MemoryDiagnoser]
  public class DrawBenchmarks
  {
    private static readonly Point g_origin = new Point(32, 32);

    private readonly byte[] m_bits = new byte[ModuleMatrix.MaxPackedModuleByteCount];
    private readonly MarkerQuad[] m_quads = new MarkerQuad[FrameMarker.MaxQuadCount];
    private readonly Vertex[] m_triangles = new Vertex[FrameMarker.MaxTriangleVertexCount];
    private readonly Vertex[] m_indexedVertices = new Vertex[FrameMarker.MaxIndexedVertexCount];
    private readonly Vertex[] m_grid = new Vertex[FrameMarker.MaxGridVertexCount];
    private readonly int[] m_indices = new int[FrameMarker.MaxIndexCount];
    private readonly Options m_options = Options.Default;
    private int m_size;

    [Params(MarkerKind.Frame, MarkerKind.Sync)]
    public MarkerKind Kind { get; set; }

    [GlobalSetup]
    public void Setup() => m_size = BenchmarkMarkers.Encode(Kind, m_bits);

    [Benchmark]
    public int ModulesToQuads() => FrameMarker.ModulesToQuads(Matrix(), m_options, g_origin, m_quads);

    [Benchmark]
    public int ModulesToTriangles() => FrameMarker.ModulesToTriangles(Matrix(), m_options, g_origin, m_triangles);

    [Benchmark]
    public int ModulesToIndexed() => FrameMarker.ModulesToIndexed(Matrix(), m_options, g_origin, m_indexedVertices, m_indices).IndexCount;

    /// <summary>The static grid: built once per symbol size, options and origin, not per frame; measured for completeness.</summary>
    [Benchmark]
    public int GridVertices() => FrameMarker.GridVertices(Kind, m_options, g_origin, m_grid);

    [Benchmark]
    public int ModulesToGridIndices() => FrameMarker.ModulesToGridIndices(Matrix(), m_indices);

    // A ModuleMatrix is a view (a ref struct): it is made from the kept bytes for every call, as an application does
    private ModuleMatrix Matrix()
    {
      ModuleMatrix.TryFromBits(m_size, m_bits, out var matrix);
      return matrix;
    }
  }
}
