//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The marker format and geometry: constants, sizing and placement, the payload wire format and quad to vertex conversion. The same API as
//* the C++ library (MB::FrameMarker); the specification is doc/marker-format.md. Nothing here allocates.
//*
//* Coordinates are pixels with the origin at the top-left corner, +x to the right and +y down. Every quad edge and every vertex lies on
//* an integer pixel edge.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;

namespace MB.FrameMarker
{
  public static class Marker
  {
    /// <summary>
    /// Every marker (frame, start and end) is QR version 6 (41x41 modules), error correction level M, byte mode, so the marker never changes
    /// size. Version 6-M holds <see cref="QrCapacityBytes"/> bytes: a frame or end marker uses <see cref="PayloadByteCount"/> of them, a start
    /// marker <see cref="StartPayloadByteCount"/>; the rest is room for future fields.
    /// </summary>
    public const int QrVersion = 6;

    public const int QrModuleCount = (4 * QrVersion) + 17;
    public const int QrCapacityBytes = 106;

    /// <summary>The sync marker (<see cref="MarkerKind.Sync"/>) is QR version 2 (25x25 modules): magic | format version | kind | frame index.</summary>
    public const int SyncQrVersion = 2;

    public const int SyncQrModuleCount = (4 * SyncQrVersion) + 17;
    public const int SyncPayloadByteCount = 12;

    /// <summary>Payload header, shared by every marker kind (little endian): magic "MF" | format version | kind | frame index u64 |
    /// animation ticks i64 | run id u32 | intended display ticks i64 | target frame ticks u32 | CPU start ticks i64 | CPU busy ticks u32 |
    /// preferred frame ticks u32 | flags u8. Start and end markers carry the values of the frame that shows them.</summary>
    public const int PayloadByteCount = 53;
    public const byte PayloadMagic0 = (byte)'M';
    public const byte PayloadMagic1 = (byte)'F';
    public const byte PayloadFormatVersion = 1;

    /// <summary>The target and preferred frame time of a renderer that presents only when something changes: there is no interval to aim for.</summary>
    public const uint OnDemandFrameTicks = uint.MaxValue;

    /// <summary>Start marker payload: header | start time UTC i64 | sequence id (16 bytes).</summary>
    public const int StartPayloadByteCount = PayloadByteCount + 8 + SequenceId.ByteCount;

    /// <summary>The longest payload of any kind: the start marker's.</summary>
    public const int MaxEncodedPayloadByteCount = StartPayloadByteCount;

    /// <summary>TimeSpan / DateTime resolution.</summary>
    public const long TicksPerSecond = TimeSpan.TicksPerSecond;

    /// <summary>DateTime ticks (since 0001-01-01) at the Unix epoch.</summary>
    public const long UnixEpochDateTimeTicks = 621_355_968_000_000_000;

    /// <summary>Recommended distance in source pixels between the marker and the edge of the frame.</summary>
    public const int RecommendedInsetPx = 32;

    public const int MinModuleSizePx = 1;
    public const int MaxModuleSizePx = 1024;
    public const int MaxQuietZoneModules = 16;
    public const int RecommendedQuietZoneModules = 4;

    /// <summary>Upper bound on the number of quads for any marker: one background quad plus at most one quad per dark run.</summary>
    public const int MaxQuadCount = 1 + (QrModuleCount * ((QrModuleCount + 1) / 2));

    public const int MaxTriangleVertexCount = MaxQuadCount * 6;
    public const int MaxIndexedVertexCount = MaxQuadCount * 4;
    public const int MaxIndexCount = MaxQuadCount * 6;

    /// <summary>
    /// The packed module matrix (<see cref="ModuleMatrix.Bits"/>) of the largest symbol: 1 bit per module, 211 bytes for 41x41. A buffer of this
    /// size fits every marker.
    /// </summary>
    public const int MaxPackedModuleByteCount = ((QrModuleCount * QrModuleCount) + 7) / 8;

    /// <summary>Vertices of the main marker's static grid (<see cref="GridVertices"/>): 1768; the sync marker's is 680. Both fit 16-bit indices.</summary>
    public const int MaxGridVertexCount = 4 + ((QrModuleCount + 1) * (QrModuleCount + 1));

    private const int OffsetKind = 3;
    private const int OffsetFrameIndex = 4;
    private const int OffsetAnimationTicks = 12;
    private const int OffsetRunId = 20;
    private const int OffsetIntendedDisplayTicks = 24;
    private const int OffsetTargetFrameTicks = 32;
    private const int OffsetCpuStartTicks = 36;
    private const int OffsetCpuBusyTicks = 44;
    private const int OffsetPreferredFrameTicks = 48;
    private const int OffsetFlags = 52;
    private const int OffsetStartUtcTicks = PayloadByteCount;
    private const int OffsetSequenceId = OffsetStartUtcTicks + 8;

    public static bool IsValid(in Options options) =>
      options.ModuleSizePx >= MinModuleSizePx
      && options.ModuleSizePx <= MaxModuleSizePx
      && options.QuietZoneModules >= 0
      && options.QuietZoneModules <= MaxQuietZoneModules;

    /// <summary>Modules per side of a marker's symbol: the main marker (frame, start and end) or the smaller sync marker.</summary>
    public static int QrModuleCountFor(MarkerKind kind) => kind == MarkerKind.Sync ? SyncQrModuleCount : QrModuleCount;

    /// <summary>
    /// Width and height in source pixels of a marker (symbol + quiet zone). Frame, start and end markers have one size, the sync marker is
    /// smaller.
    /// </summary>
    public static int MarkerSizePx(in Options options, MarkerKind kind = MarkerKind.Frame) =>
      (QrModuleCountFor(kind) + (2 * options.QuietZoneModules)) * options.ModuleSizePx;

    /// <summary>Hard minimum module size: 2 stored pixels per module after all scaling (source -> capture -> stored).</summary>
    public static int MinimumModuleSizePx(int sourceHeight, int storedHeight) => ModuleSizeForStoredPx(2, sourceHeight, storedHeight);

    /// <summary>Recommended module size: 3 stored pixels per module, or 4 when the capture card delivers MJPEG.</summary>
    public static int RecommendModuleSizePx(int sourceHeight, int storedHeight, bool mjpeg = false) =>
      ModuleSizeForStoredPx(mjpeg ? 4 : 3, sourceHeight, storedHeight);

    /// <summary>
    /// Recommended origin of a marker: the main marker (frame, start and end) top-left, the sync marker bottom-left.
    /// <paramref name="alignPx"/> should be the integer downscale ratio (1 if none) so module edges land on stored pixel edges.
    /// </summary>
    public static Point RecommendedOrigin(MarkerKind kind, int sourceWidth, int sourceHeight, in Options options, int alignPx = 1)
    {
      int inset = AlignUp(RecommendedInsetPx, alignPx);
      if (kind == MarkerKind.Sync)
        return new Point(inset, AlignDown(sourceHeight - inset - MarkerSizePx(options, kind), alignPx));
      return new Point(inset, inset);
    }

    /// <summary>Convert a wall clock time to DateTime UTC ticks (the <see cref="StartMetadata.UtcTicks"/> format).</summary>
    public static long ToDateTimeTicks(DateTime time) => time.ToUniversalTime().Ticks;

    /// <summary>Convert seconds (for example an animation clock) to TimeSpan ticks, rounded to the nearest tick.</summary>
    public static long SecondsToTicks(double seconds) => (long)Math.Round(seconds * TicksPerSecond);

    /// <summary>
    /// Serialize the payload into <paramref name="destination"/>. Start markers append the metadata, other kinds ignore it.
    /// <see cref="MaxEncodedPayloadByteCount"/> bytes are always enough. Returns the number of bytes written, or 0 if the destination is too
    /// small.
    /// </summary>
    public static int EncodePayload(in Payload payload, in StartMetadata metadata, Span<byte> destination)
    {
      bool isStart = payload.Kind == MarkerKind.SequenceStart;
      int byteCount =
        isStart ? StartPayloadByteCount
        : payload.Kind == MarkerKind.Sync ? SyncPayloadByteCount
        : PayloadByteCount;
      if (destination.Length < byteCount)
        return 0;

      destination[0] = PayloadMagic0;
      destination[1] = PayloadMagic1;
      destination[2] = PayloadFormatVersion;
      destination[OffsetKind] = (byte)payload.Kind;
      BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(OffsetFrameIndex), payload.FrameIndex);
      // A sync marker is the start of the header: magic, format version, kind and frame index
      if (payload.Kind == MarkerKind.Sync)
        return byteCount;
      BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(OffsetAnimationTicks), payload.AnimationTicks);
      BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(OffsetRunId), payload.RunId);
      BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(OffsetIntendedDisplayTicks), payload.IntendedDisplayTicks);
      BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(OffsetTargetFrameTicks), payload.TargetFrameTicks);
      BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(OffsetCpuStartTicks), payload.CpuStartTicks);
      BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(OffsetCpuBusyTicks), payload.CpuBusyTicks);
      BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(OffsetPreferredFrameTicks), payload.PreferredFrameTicks);
      destination[OffsetFlags] = (byte)payload.Flags;
      if (isStart)
      {
        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(OffsetStartUtcTicks), metadata.UtcTicks);
        metadata.SequenceId.TryCopyTo(destination.Slice(OffsetSequenceId));
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
        source.Length < SyncPayloadByteCount
        || source[0] != PayloadMagic0
        || source[1] != PayloadMagic1
        || source[2] != PayloadFormatVersion
        || source[OffsetKind] > (byte)MarkerKind.Sync
      )
        return false;

      var kind = (MarkerKind)source[OffsetKind];
      if (kind == MarkerKind.Sync)
      {
        if (source.Length != SyncPayloadByteCount)
          return false;
        payload = new Payload(BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(OffsetFrameIndex)), 0, 0, kind);
        return true;
      }
      if (source.Length < PayloadByteCount)
        return false;
      if (kind == MarkerKind.SequenceStart)
      {
        if (source.Length != StartPayloadByteCount)
          return false;
        metadata = new StartMetadata(
          BinaryPrimitives.ReadInt64LittleEndian(source.Slice(OffsetStartUtcTicks)),
          SequenceId.FromBytes(source.Slice(OffsetSequenceId, SequenceId.ByteCount))
        );
      }
      else if (source.Length != PayloadByteCount)
      {
        return false;
      }

      payload = new Payload(
        BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(OffsetFrameIndex)),
        BinaryPrimitives.ReadInt64LittleEndian(source.Slice(OffsetAnimationTicks)),
        BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(OffsetRunId)),
        kind,
        BinaryPrimitives.ReadInt64LittleEndian(source.Slice(OffsetIntendedDisplayTicks)),
        BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(OffsetTargetFrameTicks)),
        BinaryPrimitives.ReadInt64LittleEndian(source.Slice(OffsetCpuStartTicks)),
        BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(OffsetCpuBusyTicks)),
        BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(OffsetPreferredFrameTicks)),
        // Every value is accepted: bits without a name are reserved and kept
        (MarkerFlags)source[OffsetFlags]
      );
      return true;
    }

    /// <summary>The bytes of a packed module matrix of <paramref name="size"/> x <paramref name="size"/> modules (79 for the sync marker's 25).</summary>
    public static int PackedModuleByteCount(int size) => size <= 0 ? 0 : ((size * size) + 7) / 8;

    /// <summary>Vertices of a marker kind's static grid: 4 for the light background, then every module corner, (N + 1)².</summary>
    public static int GridVertexCount(MarkerKind kind)
    {
      int corners = QrModuleCountFor(kind) + 1;
      return 4 + (corners * corners);
    }

    /// <summary>
    /// The marker's static grid, for drawing it with per-frame indices only (<see cref="ModulesToGridIndices"/>): the vertices stay the same
    /// while the kind's symbol size, the options and the origin do. Vertices 0..3 are the light background (TL, TR, BR, BL, luma 255); then
    /// the corners of the modules, dark (luma 0), row-major: corner (column, row) is vertex 4 + row x (N + 1) + column, N the kind's modules
    /// per side. Returns the number of vertices written (<see cref="GridVertexCount"/>), or 0 if the options are invalid or
    /// <paramref name="destination"/> is too small.
    /// </summary>
    public static int GridVertices(MarkerKind kind, in Options options, Point origin, Span<Vertex> destination)
    {
      int count = GridVertexCount(kind);
      if (!IsValid(options) || destination.Length < count)
        return 0;
      int modules = QrModuleCountFor(kind);
      int moduleSize = options.ModuleSizePx;
      int markerSize = MarkerSizePx(options, kind);
      destination[0] = new Vertex(origin.X, origin.Y, 255);
      destination[1] = new Vertex(origin.X + markerSize, origin.Y, 255);
      destination[2] = new Vertex(origin.X + markerSize, origin.Y + markerSize, 255);
      destination[3] = new Vertex(origin.X, origin.Y + markerSize, 255);
      int symbolLeft = origin.X + (options.QuietZoneModules * moduleSize);
      int symbolTop = origin.Y + (options.QuietZoneModules * moduleSize);
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
        int topLeft = baseVertex + 4 + (run.Top * corners) + run.Left;
        int topRight = baseVertex + 4 + (run.Top * corners) + run.Right;
        destination[count++] = topLeft;
        destination[count++] = topRight;
        destination[count++] = topLeft + corners;
        destination[count++] = topLeft + corners;
        destination[count++] = topRight;
        destination[count++] = topRight + corners;
      }
      return count;
    }

    /// <summary>Bytes per pixel of a <see cref="PixelFormat"/>.</summary>
    public static int BytesPerPixel(PixelFormat format) =>
      format switch
      {
        PixelFormat.Rgb24 => 3,
        PixelFormat.Rgba32 => 4,
        _ => 1,
      };

    /// <summary>
    /// The marker as quads: the light background (symbol + quiet zone) first, then one dark quad per horizontal run of dark modules. Draw them
    /// in order. Every marker produces at most <see cref="MaxQuadCount"/> quads. Returns the number of quads written, or 0 if the options are
    /// invalid, the matrix is empty or <paramref name="destination"/> is too small.
    /// </summary>
    public static int ModulesToQuads(ModuleMatrix matrix, in Options options, Point origin, Span<Quad> destination)
    {
      if (!IsValid(options) || matrix.IsEmpty)
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
    /// vertices written, or 0 if the options are invalid, the matrix is empty or <paramref name="destination"/> is too small.
    /// </summary>
    public static int ModulesToTriangles(ModuleMatrix matrix, in Options options, Point origin, Span<Vertex> destination)
    {
      if (!IsValid(options) || matrix.IsEmpty)
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
    /// <see cref="MaxIndexCount"/> indices. Returns an empty count if the options are invalid, the matrix is empty or a destination is too small.
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
      if (!IsValid(options) || matrix.IsEmpty)
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
    /// (0 = width x <see cref="BytesPerPixel"/>): the light background (symbol + quiet zone), then the dark modules, 0 (dark) or 255 (light) in
    /// every colour channel and alpha 255. The marker is clipped to the buffer; other pixels are left as they are. With a module size of 1 and
    /// origin (0,0) this is a module-resolution image (a texture to scale up with point filtering). Returns false, writing nothing, if the
    /// options are invalid, the matrix is empty, the stride is shorter than a row or <paramref name="destination"/> is too small.
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
      if (!IsValid(options) || matrix.IsEmpty || width < 0 || height < 0)
        return false;
      int bytesPerPixel = BytesPerPixel(format);
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
    internal static void WriteTriangles(in Quad quad, Span<Vertex> destination)
    {
      byte luma = quad.Dark ? (byte)0 : (byte)255;
      var topLeft = new Vertex(quad.Left, quad.Top, luma);
      var topRight = new Vertex(quad.Right, quad.Top, luma);
      var bottomRight = new Vertex(quad.Right, quad.Bottom, luma);
      var bottomLeft = new Vertex(quad.Left, quad.Bottom, luma);
      destination[0] = topLeft;
      destination[1] = topRight;
      destination[2] = bottomLeft;
      destination[3] = bottomLeft;
      destination[4] = topRight;
      destination[5] = bottomRight;
    }

    /// <summary>The quad's 4 vertices and 6 indices (starting at <paramref name="firstIndex"/>).</summary>
    internal static void WriteIndexed(in Quad quad, Span<Vertex> vertices, Span<int> indices, int firstIndex)
    {
      byte luma = quad.Dark ? (byte)0 : (byte)255;
      vertices[0] = new Vertex(quad.Left, quad.Top, luma);
      vertices[1] = new Vertex(quad.Right, quad.Top, luma);
      vertices[2] = new Vertex(quad.Right, quad.Bottom, luma);
      vertices[3] = new Vertex(quad.Left, quad.Bottom, luma);
      indices[0] = firstIndex;
      indices[1] = firstIndex + 1;
      indices[2] = firstIndex + 3;
      indices[3] = firstIndex + 3;
      indices[4] = firstIndex + 1;
      indices[5] = firstIndex + 2;
    }

    /// <summary>Fill a quad, clipped to the buffer, with its luma in every colour channel (alpha 255).</summary>
    private static void FillQuad(in Quad quad, Span<byte> destination, int width, int height, int bytesPerPixel, int stride)
    {
      int left = Math.Max(quad.Left, 0);
      int right = Math.Min(quad.Right, width);
      int top = Math.Max(quad.Top, 0);
      int bottom = Math.Min(quad.Bottom, height);
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

    private static int CeilDiv(long numerator, long denominator) => (int)((numerator + denominator - 1) / denominator);

    private static int AlignDown(int value, int alignment) => alignment <= 1 ? value : (value / alignment) * alignment;

    private static int AlignUp(int value, int alignment) => alignment <= 1 ? value : CeilDiv(value, alignment) * alignment;

    private static int ModuleSizeForStoredPx(int storedPxPerModule, int sourceHeight, int storedHeight)
    {
      if (sourceHeight <= 0 || storedHeight <= 0)
        return storedPxPerModule;
      // ModuleSizePx = ceil(storedPxPerModule / s) where s = storedHeight / sourceHeight
      int size = CeilDiv((long)storedPxPerModule * sourceHeight, storedHeight);
      return size < storedPxPerModule ? storedPxPerModule : size;
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
      private readonly Quad m_background;
      private bool m_backgroundDone;
      private int m_x;
      private int m_y;

      public QuadWalker(ModuleMatrix matrix, in Options options, Point origin)
      {
        m_bits = matrix.Bits;
        m_size = matrix.Size;
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
        while (m_y < m_size)
        {
          int rowStart = m_y * m_size;
          int runStart = FindModule(rowStart, m_x, true);
          if (runStart < m_size)
          {
            m_x = FindModule(rowStart, runStart, false);
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
