//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Generates the marker for a frame as triangles, indexed triangles or quads, written straight into the caller's spans. Create one
//* generator and reuse it every frame: every buffer is allocated in the constructor (the payload bytes live on the stack), so the Generate
//* methods never allocate. Not thread-safe; use one generator per thread.
//*
//* Every output walks the marker in the same order: the light background (symbol + quiet zone) first, then one dark quad per horizontal
//* run of dark modules. Draw it in that order, last in the frame (after post effects and UI), without blending, in pure black and white.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FrameMarker
{
  public sealed class MarkerGenerator
  {
    private readonly QrEncoder m_encoder = new QrEncoder();
    private readonly ModuleMatrix m_matrix = new ModuleMatrix();

    /// <summary>
    /// Build the QR module matrix for the payload into <paramref name="matrix"/>. The metadata is only used by start markers. Returns false if
    /// the payload does not fit the QR symbol (which no valid payload does).
    /// </summary>
    public bool GenerateModules(in Payload payload, in StartMetadata metadata, ModuleMatrix matrix)
    {
      Span<byte> payloadBytes = stackalloc byte[Marker.MaxEncodedPayloadByteCount];
      int byteCount = Marker.EncodePayload(payload, metadata, payloadBytes);
      if (byteCount == 0)
        return false;
      // Every kind is pinned to one version, so the symbol never changes size between frames
      int version = payload.Kind == MarkerKind.Sync ? Marker.SyncQrVersion : Marker.QrVersion;
      if (!m_encoder.Encode(payloadBytes.Slice(0, byteCount), version, version))
        return false;
      matrix.CopyFrom(m_encoder);
      return true;
    }

    /// <summary>Build the QR module matrix of a frame or end marker.</summary>
    public bool GenerateModules(in Payload payload, ModuleMatrix matrix) => GenerateModules(payload, default, matrix);

    /// <summary>
    /// Generate the marker as a triangle list: 6 vertices per quad, (TL, TR, BL) (BL, TR, BR), clockwise on screen, every vertex on a pixel
    /// corner. Every marker needs at most <see cref="Marker.MaxTriangleVertexCount"/> vertices. Returns the number of vertices
    /// written, or 0 if the options are invalid or <paramref name="destination"/> is too small.
    /// </summary>
    public int GenerateTriangles(in Payload payload, in Options options, Point origin, Span<Vertex> destination) =>
      BuildMatrix(payload, default, false, options) ? WriteTriangles(options, origin, destination) : 0;

    /// <summary>
    /// <see cref="GenerateTriangles"/> for a start marker carrying metadata (the payload's kind is forced to SequenceStart). At most
    /// <see cref="Marker.MaxTriangleVertexCount"/> vertices.
    /// </summary>
    public int GenerateStartTriangles(in Payload payload, in StartMetadata metadata, in Options options, Point origin, Span<Vertex> destination) =>
      BuildMatrix(payload, metadata, true, options) ? WriteTriangles(options, origin, destination) : 0;

    /// <summary>
    /// Generate the marker as an indexed triangle list: 4 vertices (TL, TR, BR, BL) and 6 indices (0,1,3)(3,1,2) per quad, clockwise on
    /// screen. <paramref name="baseVertex"/> is added to every index. Every marker needs at most
    /// <see cref="Marker.MaxIndexedVertexCount"/> vertices and <see cref="Marker.MaxIndexCount"/> indices. Returns an empty count
    /// if the options are invalid or a destination is too small.
    /// </summary>
    public IndexedCount GenerateIndexed(
      in Payload payload,
      in Options options,
      Point origin,
      Span<Vertex> vertices,
      Span<int> indices,
      int baseVertex = 0
    ) => BuildMatrix(payload, default, false, options) ? WriteIndexed(options, origin, vertices, indices, baseVertex) : default;

    /// <summary><see cref="GenerateIndexed"/> for a start marker carrying metadata (the payload's kind is forced to SequenceStart).</summary>
    public IndexedCount GenerateStartIndexed(
      in Payload payload,
      in StartMetadata metadata,
      in Options options,
      Point origin,
      Span<Vertex> vertices,
      Span<int> indices,
      int baseVertex = 0
    ) => BuildMatrix(payload, metadata, true, options) ? WriteIndexed(options, origin, vertices, indices, baseVertex) : default;

    /// <summary>
    /// Generate the marker as quads, for renderers that fill rectangles. Every marker produces at most
    /// <see cref="Marker.MaxQuadCount"/> quads. Returns the number of quads written, or 0 if the options are invalid or
    /// <paramref name="destination"/> is too small.
    /// </summary>
    public int GenerateQuads(in Payload payload, in Options options, Point origin, Span<Quad> destination) =>
      BuildMatrix(payload, default, false, options) ? WriteQuads(options, origin, destination) : 0;

    /// <summary><see cref="GenerateQuads"/> for a start marker carrying metadata (the payload's kind is forced to SequenceStart).</summary>
    public int GenerateStartQuads(in Payload payload, in StartMetadata metadata, in Options options, Point origin, Span<Quad> destination) =>
      BuildMatrix(payload, metadata, true, options) ? WriteQuads(options, origin, destination) : 0;

    private bool BuildMatrix(in Payload payload, in StartMetadata metadata, bool forceStart, in Options options)
    {
      if (!Marker.IsValid(options))
        return false;
      if (!forceStart)
        return GenerateModules(payload, default, m_matrix);
      return GenerateModules(payload.WithKind(MarkerKind.SequenceStart), metadata, m_matrix);
    }

    private int WriteQuads(in Options options, Point origin, Span<Quad> destination)
    {
      var walker = new QuadWalker(m_matrix, options, origin);
      int count = 0;
      while (walker.TryNext(out var quad))
      {
        if (count >= destination.Length)
          return 0;
        destination[count++] = quad;
      }
      return count;
    }

    private int WriteTriangles(in Options options, Point origin, Span<Vertex> destination)
    {
      var walker = new QuadWalker(m_matrix, options, origin);
      int count = 0;
      while (walker.TryNext(out var quad))
      {
        if (destination.Length - count < 6)
          return 0;
        Marker.WriteTriangles(quad, destination.Slice(count, 6));
        count += 6;
      }
      return count;
    }

    private IndexedCount WriteIndexed(in Options options, Point origin, Span<Vertex> vertices, Span<int> indices, int baseVertex)
    {
      var walker = new QuadWalker(m_matrix, options, origin);
      int vertexCount = 0;
      int indexCount = 0;
      while (walker.TryNext(out var quad))
      {
        if (vertices.Length - vertexCount < 4 || indices.Length - indexCount < 6)
          return default;
        Marker.WriteIndexed(quad, vertices.Slice(vertexCount, 4), indices.Slice(indexCount, 6), baseVertex + vertexCount);
        vertexCount += 4;
        indexCount += 6;
      }
      return new IndexedCount(vertexCount, indexCount);
    }

    /// <summary>
    /// The marker's quads in draw order: the light background, then one dark quad per horizontal run of dark modules. A plain struct over the
    /// module matrix, so every output walks the same code without allocating (C# 9 cannot pass spans through an interface).
    /// </summary>
    private struct QuadWalker
    {
      private readonly ModuleMatrix m_matrix;
      private readonly int m_moduleSize;
      private readonly int m_symbolLeft;
      private readonly int m_symbolTop;
      private readonly Quad m_background;
      private bool m_backgroundDone;
      private int m_x;
      private int m_y;

      public QuadWalker(ModuleMatrix matrix, in Options options, Point origin)
      {
        m_matrix = matrix;
        m_moduleSize = options.ModuleSizePx;
        int markerSize = (matrix.Size + (2 * options.QuietZoneModules)) * options.ModuleSizePx;
        m_symbolLeft = origin.X + (options.QuietZoneModules * m_moduleSize);
        m_symbolTop = origin.Y + (options.QuietZoneModules * m_moduleSize);
        m_background = new Quad(origin.X, origin.Y, origin.X + markerSize, origin.Y + markerSize, false);
        m_backgroundDone = false;
        m_x = 0;
        m_y = 0;
      }

      public bool TryNext(out Quad quad)
      {
        if (!m_backgroundDone)
        {
          m_backgroundDone = true;
          quad = m_background;
          return true;
        }
        while (m_y < m_matrix.Size)
        {
          while (m_x < m_matrix.Size)
          {
            if (!m_matrix.IsDark(m_x, m_y))
            {
              ++m_x;
              continue;
            }
            int runStart = m_x;
            while (m_x < m_matrix.Size && m_matrix.IsDark(m_x, m_y))
              ++m_x;
            int top = m_symbolTop + (m_y * m_moduleSize);
            quad = new Quad(m_symbolLeft + (runStart * m_moduleSize), top, m_symbolLeft + (m_x * m_moduleSize), top + m_moduleSize, true);
            return true;
          }
          m_x = 0;
          ++m_y;
        }
        quad = default;
        return false;
      }
    }
  }
}
