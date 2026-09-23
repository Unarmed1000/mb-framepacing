//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Generates the marker for a frame as triangles, indexed triangles or quads, written straight into the caller's arrays. Create one
//* generator and reuse it every frame: every buffer is allocated in the constructor, so the Generate methods never allocate.
//* Not thread-safe; use one generator per thread.
//*
//* Every output walks the marker in the same order: the light background (symbol + quiet zone) first, then one dark quad per horizontal
//* run of dark modules. Draw it in that order, last in the frame (after post effects and UI), without blending, in pure black and white.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FrameMarker
{
  public sealed class MarkerGenerator
  {
    private readonly QrEncoder m_encoder = new QrEncoder();
    private readonly byte[] m_payloadBytes = new byte[Marker.MaxEncodedPayloadByteCount];
    private readonly ModuleMatrix m_matrix = new ModuleMatrix();

    /// <summary>
    /// Build the QR module matrix for the payload into <paramref name="matrix"/>. The metadata is only used by start markers. Returns false if
    /// the start name is too long.
    /// </summary>
    public bool GenerateModules(in Payload payload, in StartMetadata metadata, ModuleMatrix matrix)
    {
      int byteCount = Marker.EncodePayload(payload, metadata, m_payloadBytes);
      if (byteCount == 0)
        return false;
      // Frame and end markers are pinned to one version so the symbol never changes size between frames
      int maxVersion = payload.Kind == MarkerKind.SequenceStart ? Marker.MaxQrVersion : Marker.FrameQrVersion;
      if (!m_encoder.Encode(m_payloadBytes, byteCount, Marker.FrameQrVersion, maxVersion))
        return false;
      matrix.CopyFrom(m_encoder);
      return true;
    }

    /// <summary>Build the QR module matrix of a frame or end marker.</summary>
    public bool GenerateModules(in Payload payload, ModuleMatrix matrix) => GenerateModules(payload, default, matrix);

    /// <summary>
    /// Generate the marker as a triangle list: 6 vertices per quad, (TL, TR, BL) (BL, TR, BR), clockwise on screen, every vertex on a pixel
    /// corner. Frame and end markers need at most <see cref="Marker.MaxFrameTriangleVertexCount"/> vertices. Returns the number of vertices
    /// written, or 0 if the options are invalid or <paramref name="destination"/> is too small.
    /// </summary>
    public int GenerateTriangles(in Payload payload, in Options options, Point origin, Vertex[] destination)
    {
      if (destination == null || !BuildMatrix(payload, default, false, options))
        return 0;
      var emitter = new TriangleEmitter(destination);
      return Walk(options, origin, ref emitter) ? emitter.Count : 0;
    }

    /// <summary>
    /// <see cref="GenerateTriangles"/> for a start marker carrying metadata (the payload's kind is forced to SequenceStart). At most
    /// <see cref="Marker.MaxTriangleVertexCount"/> vertices, and the marker is at most <see cref="Marker.MaxMarkerSizePx"/> wide and high.
    /// </summary>
    public int GenerateStartTriangles(in Payload payload, in StartMetadata metadata, in Options options, Point origin, Vertex[] destination)
    {
      if (destination == null || !BuildMatrix(payload, metadata, true, options))
        return 0;
      var emitter = new TriangleEmitter(destination);
      return Walk(options, origin, ref emitter) ? emitter.Count : 0;
    }

    /// <summary>
    /// Generate the marker as an indexed triangle list: 4 vertices (TL, TR, BR, BL) and 6 indices (0,1,3)(3,1,2) per quad, clockwise on
    /// screen. <paramref name="baseVertex"/> is added to every index. Frame and end markers need at most
    /// <see cref="Marker.MaxFrameIndexedVertexCount"/> vertices and <see cref="Marker.MaxFrameIndexCount"/> indices. Returns an empty count
    /// if the options are invalid or a destination is too small.
    /// </summary>
    public IndexedCount GenerateIndexed(in Payload payload, in Options options, Point origin, Vertex[] vertices, int[] indices, int baseVertex = 0)
    {
      if (vertices == null || indices == null || !BuildMatrix(payload, default, false, options))
        return default;
      var emitter = new IndexedEmitter(vertices, indices, baseVertex);
      return Walk(options, origin, ref emitter) ? new IndexedCount(emitter.VertexCount, emitter.IndexCount) : default;
    }

    /// <summary><see cref="GenerateIndexed"/> for a start marker carrying metadata (the payload's kind is forced to SequenceStart).</summary>
    public IndexedCount GenerateStartIndexed(
      in Payload payload,
      in StartMetadata metadata,
      in Options options,
      Point origin,
      Vertex[] vertices,
      int[] indices,
      int baseVertex = 0
    )
    {
      if (vertices == null || indices == null || !BuildMatrix(payload, metadata, true, options))
        return default;
      var emitter = new IndexedEmitter(vertices, indices, baseVertex);
      return Walk(options, origin, ref emitter) ? new IndexedCount(emitter.VertexCount, emitter.IndexCount) : default;
    }

    /// <summary>
    /// Generate the marker as quads, for renderers that fill rectangles. Frame and end markers produce at most
    /// <see cref="Marker.MaxFrameQuadCount"/> quads. Returns the number of quads written, or 0 if the options are invalid or
    /// <paramref name="destination"/> is too small.
    /// </summary>
    public int GenerateQuads(in Payload payload, in Options options, Point origin, Quad[] destination)
    {
      if (destination == null || !BuildMatrix(payload, default, false, options))
        return 0;
      var emitter = new QuadEmitter(destination);
      return Walk(options, origin, ref emitter) ? emitter.Count : 0;
    }

    /// <summary><see cref="GenerateQuads"/> for a start marker carrying metadata (the payload's kind is forced to SequenceStart).</summary>
    public int GenerateStartQuads(in Payload payload, in StartMetadata metadata, in Options options, Point origin, Quad[] destination)
    {
      if (destination == null || !BuildMatrix(payload, metadata, true, options))
        return 0;
      var emitter = new QuadEmitter(destination);
      return Walk(options, origin, ref emitter) ? emitter.Count : 0;
    }

    private bool BuildMatrix(in Payload payload, in StartMetadata metadata, bool forceStart, in Options options)
    {
      if (!Marker.IsValid(options))
        return false;
      if (!forceStart)
        return GenerateModules(payload, default, m_matrix);
      return GenerateModules(payload.WithKind(MarkerKind.SequenceStart), metadata, m_matrix);
    }

    /// <summary>Walk the marker in draw order and hand every quad to the emitter. False when the emitter's output is full.</summary>
    private bool Walk<TEmitter>(in Options options, Point origin, ref TEmitter emitter)
      where TEmitter : struct, IQuadEmitter
    {
      int moduleSize = options.ModuleSizePx;
      int markerSize = Marker.MarkerSizePx(options, m_matrix.Size);
      int symbolLeft = origin.X + (options.QuietZoneModules * moduleSize);
      int symbolTop = origin.Y + (options.QuietZoneModules * moduleSize);

      if (!emitter.Emit(new Quad(origin.X, origin.Y, origin.X + markerSize, origin.Y + markerSize, false)))
        return false;
      for (int y = 0; y < m_matrix.Size; ++y)
      {
        int top = symbolTop + (y * moduleSize);
        int x = 0;
        while (x < m_matrix.Size)
        {
          if (!m_matrix.IsDark(x, y))
          {
            ++x;
            continue;
          }
          int runStart = x;
          while (x < m_matrix.Size && m_matrix.IsDark(x, y))
            ++x;
          if (!emitter.Emit(new Quad(symbolLeft + (runStart * moduleSize), top, symbolLeft + (x * moduleSize), top + moduleSize, true)))
            return false;
        }
      }
      return true;
    }

    // The emitters are structs used through a generic constraint, so every call is specialised: no delegates, no boxing, no allocations.
    private interface IQuadEmitter
    {
      bool Emit(in Quad quad);
    }

    private struct QuadEmitter : IQuadEmitter
    {
      private readonly Quad[] m_destination;

      public QuadEmitter(Quad[] destination)
      {
        m_destination = destination;
        Count = 0;
      }

      public int Count { get; private set; }

      public bool Emit(in Quad quad)
      {
        if (Count >= m_destination.Length)
          return false;
        m_destination[Count++] = quad;
        return true;
      }
    }

    private struct TriangleEmitter : IQuadEmitter
    {
      private readonly Vertex[] m_destination;

      public TriangleEmitter(Vertex[] destination)
      {
        m_destination = destination;
        Count = 0;
      }

      public int Count { get; private set; }

      public bool Emit(in Quad quad)
      {
        if (m_destination.Length - Count < 6)
          return false;
        Marker.WriteTriangles(quad, m_destination, Count);
        Count += 6;
        return true;
      }
    }

    private struct IndexedEmitter : IQuadEmitter
    {
      private readonly Vertex[] m_vertices;
      private readonly int[] m_indices;
      private readonly int m_baseVertex;

      public IndexedEmitter(Vertex[] vertices, int[] indices, int baseVertex)
      {
        m_vertices = vertices;
        m_indices = indices;
        m_baseVertex = baseVertex;
        VertexCount = 0;
        IndexCount = 0;
      }

      public int VertexCount { get; private set; }

      public int IndexCount { get; private set; }

      public bool Emit(in Quad quad)
      {
        if (m_vertices.Length - VertexCount < 4 || m_indices.Length - IndexCount < 6)
          return false;
        Marker.WriteIndexed(quad, m_vertices, VertexCount, m_indices, IndexCount, m_baseVertex + VertexCount);
        VertexCount += 4;
        IndexCount += 6;
        return true;
      }
    }
  }
}
