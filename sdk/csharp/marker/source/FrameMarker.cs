//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The marker's functions: the payload's wire format, the buffer sizes and the drawing outputs. The same API as the C++ library's
//* FrameMarker.hpp (MB::FramePacing::Marker); the specification is doc/marker-format.md. Nothing here allocates.
//*
//* Coordinates are pixels with the origin at the top-left corner, +x to the right and +y down. Every quad edge and every vertex lies on
//* an integer pixel edge.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;

namespace MB.FramePacing.Marker
{
  public static class FrameMarker
  {
    /// <summary>Upper bound on the number of quads for any marker: one background quad plus at most one quad per dark run.</summary>
    public const int MaxQuadCount = 1 + (ModuleMatrix.MainSize * ((ModuleMatrix.MainSize + 1) / 2));

    public const int MaxTriangleVertexCount = MaxQuadCount * 6;
    public const int MaxIndexedVertexCount = MaxQuadCount * 4;
    public const int MaxIndexCount = MaxQuadCount * 6;

    /// <summary>Vertices of the main marker's static grid (<see cref="GridVertices"/>): 1768; the sync marker's is 680. Both fit 16-bit indices.</summary>
    public const int MaxGridVertexCount = 4 + ((ModuleMatrix.MainSize + 1) * (ModuleMatrix.MainSize + 1));

    /// <summary>
    /// Serialize the payload into <paramref name="destination"/>. Start markers append the metadata, other kinds ignore it. A frame or end
    /// marker is 53 bytes, a start marker 77, a sync marker 16; <see cref="Payload.MaxEncodedByteCount"/> bytes are always enough. Returns
    /// the number of bytes written, or 0 if the destination is too small.
    /// </summary>
    public static int EncodePayload(in Payload payload, in StartMetadata metadata, Span<byte> destination)
    {
      bool isStart = payload.Kind == MarkerKind.SequenceStart;
      int byteCount =
        isStart ? WireFormat.StartPayloadByteCount
        : payload.Kind == MarkerKind.Sync ? WireFormat.SyncPayloadByteCount
        : WireFormat.PayloadByteCount;
      if (destination.Length < byteCount)
        return 0;

      destination[WireFormat.OffsetMagic0] = WireFormat.PayloadMagic0;
      destination[WireFormat.OffsetMagic1] = WireFormat.PayloadMagic1;
      destination[WireFormat.OffsetVersion] = WireFormat.PayloadFormatVersion;
      destination[WireFormat.OffsetKind] = (byte)payload.Kind;
      BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(WireFormat.OffsetRunId), payload.RunId);
      BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(WireFormat.OffsetFrameIndex), payload.FrameIndex);
      // A sync marker is the start of the header: magic, format version, kind, run id and frame index
      if (payload.Kind == MarkerKind.Sync)
        return byteCount;
      destination[WireFormat.OffsetFlags] = (byte)payload.Flags;
      BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(WireFormat.OffsetAnimationTicks), payload.AnimationTime.Ticks);
      BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(WireFormat.OffsetPreferredFrameTicks), payload.PreferredFrameTime.Ticks);
      BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(WireFormat.OffsetTargetFrameTicks), payload.TargetFrameTime.Ticks);
      BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(WireFormat.OffsetIntendedDisplayTicks), payload.IntendedDisplayTime.UnsignedTicks);
      BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(WireFormat.OffsetCpuStartTicks), payload.CpuStartTime.UnsignedTicks);
      BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(WireFormat.OffsetCpuBusyTicks), payload.CpuBusy.Ticks);
      if (isStart)
      {
        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(WireFormat.OffsetStartUtcTicks), metadata.UtcTicks);
        metadata.SequenceId.TryCopyTo(destination.Slice(WireFormat.OffsetSequenceId));
      }
      return byteCount;
    }

    /// <summary>
    /// Parse the wire format: <paramref name="source"/> is exactly one payload. Returns false on a wrong length, magic, format version or an
    /// unknown kind.
    /// </summary>
    public static bool TryDecodePayload(ReadOnlySpan<byte> source, out Payload payload, out StartMetadata metadata)
    {
      payload = default;
      metadata = default;
      if (
        source.Length < WireFormat.SyncPayloadByteCount
        || source[WireFormat.OffsetMagic0] != WireFormat.PayloadMagic0
        || source[WireFormat.OffsetMagic1] != WireFormat.PayloadMagic1
        || source[WireFormat.OffsetVersion] != WireFormat.PayloadFormatVersion
        || source[WireFormat.OffsetKind] > WireFormat.MaxMarkerKindValue
      )
        return false;

      var kind = (MarkerKind)source[WireFormat.OffsetKind];
      if (kind == MarkerKind.Sync)
      {
        if (source.Length != WireFormat.SyncPayloadByteCount)
          return false;
        payload = new Payload(
          kind,
          BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(WireFormat.OffsetRunId)),
          BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(WireFormat.OffsetFrameIndex)),
          MarkerFlags.None,
          TimeSpan.Zero
        );
        return true;
      }
      if (source.Length < WireFormat.PayloadByteCount)
        return false;
      if (kind == MarkerKind.SequenceStart)
      {
        if (source.Length != WireFormat.StartPayloadByteCount)
          return false;
        metadata = new StartMetadata(
          BinaryPrimitives.ReadInt64LittleEndian(source.Slice(WireFormat.OffsetStartUtcTicks)),
          SequenceId.FromBytes(source.Slice(WireFormat.OffsetSequenceId, SequenceId.ByteCount))
        );
      }
      else if (source.Length != WireFormat.PayloadByteCount)
      {
        return false;
      }

      payload = new Payload(
        kind,
        BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(WireFormat.OffsetRunId)),
        BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(WireFormat.OffsetFrameIndex)),
        // Every value is accepted: bits without a name are reserved and kept
        (MarkerFlags)source[WireFormat.OffsetFlags],
        new TimeSpan(BinaryPrimitives.ReadInt64LittleEndian(source.Slice(WireFormat.OffsetAnimationTicks))),
        new TimeSpan32(BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(WireFormat.OffsetPreferredFrameTicks))),
        new TimeSpan32(BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(WireFormat.OffsetTargetFrameTicks))),
        TickCount64.FromUnsignedTicks(BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(WireFormat.OffsetIntendedDisplayTicks))),
        TickCount64.FromUnsignedTicks(BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(WireFormat.OffsetCpuStartTicks))),
        new TimeSpan32(BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(WireFormat.OffsetCpuBusyTicks)))
      );
      return true;
    }

    /// <summary>Vertices of a marker kind's static grid: 4 for the light background, then every module corner, (N + 1)².</summary>
    public static int GridVertexCount(MarkerKind kind)
    {
      int corners = ModuleMatrix.SizeFor(kind) + 1;
      return 4 + (corners * corners);
    }

    /// <summary>
    /// The marker's static grid, for drawing it with per-frame indices only (<see cref="ModulesToGridIndices"/>): the vertices stay the same
    /// while the kind's symbol size, the options and the origin do. Vertices 0..3 are the light background (TL, TR, BR, BL, luma 255); then
    /// the corners of the modules, dark (luma 0), row-major: corner (column, row) is vertex 4 + row x (N + 1) + column, N the kind's modules
    /// per side. Returns the number of vertices written (<see cref="GridVertexCount"/>), or 0 if <paramref name="destination"/> is too
    /// small.
    /// </summary>
    public static int GridVertices(MarkerKind kind, in Options options, Point origin, Span<Vertex> destination)
    {
      int count = GridVertexCount(kind);
      if (destination.Length < count)
        return 0;
      int modules = ModuleMatrix.SizeFor(kind);
      int moduleSize = options.ModuleSizePx;
      int markerSize = options.MarkerSizePx(kind);
      destination[0] = new Vertex(origin.X, origin.Y, 255);
      destination[1] = new Vertex(origin.X + markerSize, origin.Y, 255);
      destination[2] = new Vertex(origin.X + markerSize, origin.Y + markerSize, 255);
      destination[3] = new Vertex(origin.X, origin.Y + markerSize, 255);
      int symbolLeft = origin.X + options.QuietZonePx;
      int symbolTop = origin.Y + options.QuietZonePx;
      int index = 4;
      for (int row = 0; row <= modules; ++row)
      {
        for (int column = 0; column <= modules; ++column)
          destination[index++] = new Vertex(symbolLeft + (column * moduleSize), symbolTop + (row * moduleSize), 0);
      }
      return count;
    }

    /// <summary>
    /// The per-frame part of the grid drawing: the indices of the background, (0,1,3)(3,1,2), then 6 per horizontal run of dark modules,
    /// (TL, TR, BL) (BL, TR, BR) of the run's grid corners, clockwise on screen. Use the grid of the matrix's kind.
    /// <paramref name="baseVertex"/> is added to every index. Every marker needs at most <see cref="MaxIndexCount"/> indices. Returns the
    /// number of indices written, or 0 if the matrix is empty or <paramref name="destination"/> is too small.
    /// </summary>
    public static int ModulesToGridIndices(ModuleMatrix matrix, Span<int> destination, int baseVertex = 0)
    {
      if (matrix.IsEmpty || destination.Length < 6)
        return 0;
      destination[0] = baseVertex;
      destination[1] = baseVertex + 1;
      destination[2] = baseVertex + 3;
      destination[3] = baseVertex + 3;
      destination[4] = baseVertex + 1;
      destination[5] = baseVertex + 2;
      int count = 6;
      int corners = matrix.Size + 1;
      var walker = new QuadWalker(matrix, new Options(1, 0), default);
      walker.TryNext(out _); // the background
      while (walker.TryNext(out var run))
      {
        if (destination.Length - count < 6)
          return 0;
        // With 1 px modules at the origin, a run's quad is its columns and row
        int topLeft = baseVertex + 4 + (run.Rect.Top * corners) + run.Rect.Left;
        int topRight = baseVertex + 4 + (run.Rect.Top * corners) + run.Rect.Right;
        destination[count++] = topLeft;
        destination[count++] = topRight;
        destination[count++] = topLeft + corners;
        destination[count++] = topLeft + corners;
        destination[count++] = topRight;
        destination[count++] = topRight + corners;
      }
      return count;
    }

    /// <summary>
    /// The marker as quads: the light background (symbol + quiet zone) first, then one dark quad per horizontal run of dark modules. Draw them
    /// in order. Every marker produces at most <see cref="MaxQuadCount"/> quads. Returns the number of quads written, or 0 if the matrix is
    /// empty or <paramref name="destination"/> is too small.
    /// </summary>
    public static int ModulesToQuads(ModuleMatrix matrix, in Options options, Point origin, Span<MarkerQuad> destination)
    {
      if (matrix.IsEmpty)
        return 0;
      var walker = new QuadWalker(matrix, options, origin);
      int count = 0;
      while (walker.TryNext(out var quad))
      {
        if (count >= destination.Length)
          return 0;
        destination[count++] = quad;
      }
      return count;
    }

    /// <summary>
    /// The marker as a triangle list: 6 vertices per quad (see <see cref="ModulesToQuads"/> for the order), (TL, TR, BL) (BL, TR, BR), clockwise
    /// on screen, every vertex on a pixel corner. Every marker needs at most <see cref="MaxTriangleVertexCount"/> vertices. Returns the number of
    /// vertices written, or 0 if the matrix is empty or <paramref name="destination"/> is too small.
    /// </summary>
    public static int ModulesToTriangles(ModuleMatrix matrix, in Options options, Point origin, Span<Vertex> destination)
    {
      if (matrix.IsEmpty)
        return 0;
      var walker = new QuadWalker(matrix, options, origin);
      int count = 0;
      while (walker.TryNext(out var quad))
      {
        if (destination.Length - count < 6)
          return 0;
        WriteTriangles(quad, destination.Slice(count, 6));
        count += 6;
      }
      return count;
    }

    /// <summary>
    /// The marker as an indexed triangle list: 4 vertices (TL, TR, BR, BL) and 6 indices (0,1,3)(3,1,2) per quad, clockwise on screen.
    /// <paramref name="baseVertex"/> is added to every index. Every marker needs at most <see cref="MaxIndexedVertexCount"/> vertices and
    /// <see cref="MaxIndexCount"/> indices. Returns an empty count if the matrix is empty or a destination is too small.
    /// </summary>
    public static IndexedCount ModulesToIndexed(
      ModuleMatrix matrix,
      in Options options,
      Point origin,
      Span<Vertex> vertices,
      Span<int> indices,
      int baseVertex = 0
    )
    {
      if (matrix.IsEmpty)
        return default;
      var walker = new QuadWalker(matrix, options, origin);
      int vertexCount = 0;
      int indexCount = 0;
      while (walker.TryNext(out var quad))
      {
        if (vertices.Length - vertexCount < 4 || indices.Length - indexCount < 6)
          return default;
        WriteIndexed(quad, vertices.Slice(vertexCount, 4), indices.Slice(indexCount, 6), baseVertex + vertexCount);
        vertexCount += 4;
        indexCount += 6;
      }
      return new IndexedCount(vertexCount, indexCount);
    }

    /// <summary>
    /// Draw the marker into a <paramref name="width"/> x <paramref name="height"/> pixel buffer, rows <paramref name="stride"/> bytes apart
    /// (0 = width x <see cref="PixelFormatUtil.BytesPerPixel"/>): the light background (symbol + quiet zone), then the dark modules, 0 (dark) or 255 (light) in
    /// every colour channel and alpha 255. The marker is clipped to the buffer; other pixels are left as they are. With a module size of 1 and
    /// origin (0,0) this is a module-resolution image (a texture to scale up with point filtering). Returns false, writing nothing, if the
    /// matrix is empty, the stride is shorter than a row or <paramref name="destination"/> is too small.
    /// </summary>
    public static bool ModulesToBitmap(
      ModuleMatrix matrix,
      in Options options,
      Point origin,
      Span<byte> destination,
      int width,
      int height,
      PixelFormat format,
      int stride = 0
    )
    {
      if (matrix.IsEmpty || width < 0 || height < 0)
        return false;
      int bytesPerPixel = PixelFormatUtil.BytesPerPixel(format);
      long rowBytes = (long)width * bytesPerPixel;
      long rowStride = stride == 0 ? rowBytes : stride;
      long required = height == 0 ? 0 : (rowStride * (height - 1)) + rowBytes;
      if (rowStride < rowBytes || destination.Length < required)
        return false;
      var walker = new QuadWalker(matrix, options, origin);
      while (walker.TryNext(out var quad))
        FillQuad(quad, destination, width, height, bytesPerPixel, (int)rowStride);
      return true;
    }

    /// <summary>The quad's two triangles into <paramref name="destination"/> (6 vertices).</summary>
    internal static void WriteTriangles(in MarkerQuad quad, Span<Vertex> destination)
    {
      byte luma = quad.Dark ? (byte)0 : (byte)255;
      var topLeft = new Vertex(quad.Rect.Left, quad.Rect.Top, luma);
      var topRight = new Vertex(quad.Rect.Right, quad.Rect.Top, luma);
      var bottomRight = new Vertex(quad.Rect.Right, quad.Rect.Bottom, luma);
      var bottomLeft = new Vertex(quad.Rect.Left, quad.Rect.Bottom, luma);
      destination[0] = topLeft;
      destination[1] = topRight;
      destination[2] = bottomLeft;
      destination[3] = bottomLeft;
      destination[4] = topRight;
      destination[5] = bottomRight;
    }

    /// <summary>The quad's 4 vertices and 6 indices (starting at <paramref name="firstIndex"/>).</summary>
    internal static void WriteIndexed(in MarkerQuad quad, Span<Vertex> vertices, Span<int> indices, int firstIndex)
    {
      byte luma = quad.Dark ? (byte)0 : (byte)255;
      vertices[0] = new Vertex(quad.Rect.Left, quad.Rect.Top, luma);
      vertices[1] = new Vertex(quad.Rect.Right, quad.Rect.Top, luma);
      vertices[2] = new Vertex(quad.Rect.Right, quad.Rect.Bottom, luma);
      vertices[3] = new Vertex(quad.Rect.Left, quad.Rect.Bottom, luma);
      indices[0] = firstIndex;
      indices[1] = firstIndex + 1;
      indices[2] = firstIndex + 3;
      indices[3] = firstIndex + 3;
      indices[4] = firstIndex + 1;
      indices[5] = firstIndex + 2;
    }

    /// <summary>Fill a quad, clipped to the buffer, with its luma in every colour channel (alpha 255).</summary>
    private static void FillQuad(in MarkerQuad quad, Span<byte> destination, int width, int height, int bytesPerPixel, int stride)
    {
      int left = Math.Max(quad.Rect.Left, 0);
      int right = Math.Min(quad.Rect.Right, width);
      int top = Math.Max(quad.Rect.Top, 0);
      int bottom = Math.Min(quad.Rect.Bottom, height);
      if (left >= right || top >= bottom)
        return;
      byte luma = quad.Dark ? (byte)0 : (byte)255;
      for (int y = top; y < bottom; ++y)
      {
        var row = destination.Slice((y * stride) + (left * bytesPerPixel), (right - left) * bytesPerPixel);
        if (bytesPerPixel == 4)
        {
          for (int x = 0; x < row.Length; x += 4)
          {
            row[x] = luma;
            row[x + 1] = luma;
            row[x + 2] = luma;
            row[x + 3] = 255;
          }
        }
        else
        {
          row.Fill(luma);
        }
      }
    }

    /// <summary>
    /// The marker's quads in draw order: the light background, then one dark quad per horizontal run of dark modules. A ref struct over the
    /// packed module matrix, so every output walks the same code without allocating (C# 9 cannot pass spans through an interface). It reads
    /// whole bytes: a byte without the module it looks for is skipped at once.
    /// </summary>
    private ref struct QuadWalker
    {
      private readonly ReadOnlySpan<byte> m_bits;
      private readonly int m_size;
      private readonly int m_moduleSize;
      private readonly int m_symbolLeft;
      private readonly int m_symbolTop;
      private readonly MarkerQuad m_background;
      private bool m_backgroundDone;
      private int m_x;
      private int m_y;

      public QuadWalker(ModuleMatrix matrix, in Options options, Point origin)
      {
        m_bits = matrix.Bits;
        m_size = matrix.Size;
        m_moduleSize = options.ModuleSizePx;
        int markerSize = (matrix.Size + (2 * options.QuietZoneModules)) * m_moduleSize;
        m_symbolLeft = origin.X + options.QuietZonePx;
        m_symbolTop = origin.Y + options.QuietZonePx;
        m_background = new MarkerQuad(new Rectangle(origin.X, origin.Y, markerSize, markerSize), false);
        m_backgroundDone = false;
        m_x = 0;
        m_y = 0;
      }

      public bool TryNext(out MarkerQuad quad)
      {
        if (!m_backgroundDone)
        {
          m_backgroundDone = true;
          quad = m_background;
          return true;
        }
        while (m_y < m_size)
        {
          int rowStart = m_y * m_size;
          int runStart = FindModule(rowStart, m_x, true);
          if (runStart < m_size)
          {
            m_x = FindModule(rowStart, runStart, false);
            int top = m_symbolTop + (m_y * m_moduleSize);
            quad = new MarkerQuad(new Rectangle(m_symbolLeft + (runStart * m_moduleSize), top, (m_x - runStart) * m_moduleSize, m_moduleSize), true);
            return true;
          }
          m_x = 0;
          ++m_y;
        }
        quad = default;
        return false;
      }

      /// <summary>The first column at or after <paramref name="from"/> whose module is dark (or light), or the size when there is none.</summary>
      private int FindModule(int rowStart, int from, bool dark)
      {
        int x = from;
        while (x < m_size)
        {
          int index = rowStart + x;
          int offset = index & 7;
          int value = dark ? m_bits[index >> 3] : ~m_bits[index >> 3] & 0xFF;
          int remaining = value & (0xFF >> offset);
          if (remaining == 0)
          {
            x += 8 - offset;
            continue;
          }
          // The most significant bit is the first module
          int first = 0;
          while ((remaining & (0x80 >> first)) == 0)
            ++first;
          return Math.Min(x + (first - offset), m_size);
        }
        return m_size;
      }
    }
  }
}
