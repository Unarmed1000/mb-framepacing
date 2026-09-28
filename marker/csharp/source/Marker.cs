//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The marker format and geometry: constants, sizing and placement, the payload wire format and quad to vertex conversion. The same API as
//* the C++ library (MB::FrameMarker); the specification is doc/marker-format.md. Nothing here allocates.
//*
//* Coordinates are pixels with the origin at the top-left corner, +x to the right and +y down. Every quad edge and every vertex lies on
//* an integer pixel edge.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

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
    /// animation ticks i64 | run id u32 | intended display ticks i64 | target frame ticks u32 | CPU start ticks i64 | CPU busy ticks u32.
    /// Start and end markers carry the values of the frame that shows them.</summary>
    public const int PayloadByteCount = 48;
    public const byte PayloadMagic0 = (byte)'M';
    public const byte PayloadMagic1 = (byte)'F';
    public const byte PayloadFormatVersion = 1;

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

    private const int OffsetKind = 3;
    private const int OffsetFrameIndex = 4;
    private const int OffsetAnimationTicks = 12;
    private const int OffsetRunId = 20;
    private const int OffsetIntendedDisplayTicks = 24;
    private const int OffsetTargetFrameTicks = 32;
    private const int OffsetCpuStartTicks = 36;
    private const int OffsetCpuBusyTicks = 44;
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
    /// Serialize the payload into <paramref name="destination"/> at <paramref name="offset"/>. Start markers append the metadata, other kinds
    /// ignore it. <see cref="MaxEncodedPayloadByteCount"/> bytes are always enough. Returns the number of bytes written, or 0 if the
    /// destination is too small.
    /// </summary>
    public static int EncodePayload(in Payload payload, in StartMetadata metadata, byte[] destination, int offset = 0)
    {
      bool isStart = payload.Kind == MarkerKind.SequenceStart;
      int byteCount =
        isStart ? StartPayloadByteCount
        : payload.Kind == MarkerKind.Sync ? SyncPayloadByteCount
        : PayloadByteCount;
      if (destination == null || offset < 0 || destination.Length - offset < byteCount)
        return 0;

      destination[offset] = PayloadMagic0;
      destination[offset + 1] = PayloadMagic1;
      destination[offset + 2] = PayloadFormatVersion;
      destination[offset + OffsetKind] = (byte)payload.Kind;
      WriteLittleEndian(destination, offset + OffsetFrameIndex, payload.FrameIndex, 8);
      // A sync marker is the start of the header: magic, format version, kind and frame index
      if (payload.Kind == MarkerKind.Sync)
        return byteCount;
      WriteLittleEndian(destination, offset + OffsetAnimationTicks, unchecked((ulong)payload.AnimationTicks), 8);
      WriteLittleEndian(destination, offset + OffsetRunId, payload.RunId, 4);
      WriteLittleEndian(destination, offset + OffsetIntendedDisplayTicks, unchecked((ulong)payload.IntendedDisplayTicks), 8);
      WriteLittleEndian(destination, offset + OffsetTargetFrameTicks, payload.TargetFrameTicks, 4);
      WriteLittleEndian(destination, offset + OffsetCpuStartTicks, unchecked((ulong)payload.CpuStartTicks), 8);
      WriteLittleEndian(destination, offset + OffsetCpuBusyTicks, payload.CpuBusyTicks, 4);
      if (isStart)
      {
        WriteLittleEndian(destination, offset + OffsetStartUtcTicks, unchecked((ulong)metadata.UtcTicks), 8);
        metadata.SequenceId.TryCopyTo(destination, offset + OffsetSequenceId);
      }
      return byteCount;
    }

    /// <summary>Serialize a frame or end marker payload (start markers need <see cref="EncodePayload(in Payload, in StartMetadata, byte[], int)"/>).</summary>
    public static int EncodePayload(in Payload payload, byte[] destination, int offset = 0) => EncodePayload(payload, default, destination, offset);

    /// <summary>Parse the wire format. Returns false on a wrong length, magic, format version or an unknown kind.</summary>
    public static bool TryDecodePayload(byte[] source, int offset, int count, out Payload payload, out StartMetadata metadata)
    {
      payload = default;
      metadata = default;
      if (
        source == null
        || offset < 0
        || count < SyncPayloadByteCount
        || source.Length - offset < count
        || source[offset] != PayloadMagic0
        || source[offset + 1] != PayloadMagic1
        || source[offset + 2] != PayloadFormatVersion
        || source[offset + OffsetKind] > (byte)MarkerKind.Sync
      )
        return false;

      var kind = (MarkerKind)source[offset + OffsetKind];
      if (kind == MarkerKind.Sync)
      {
        if (count != SyncPayloadByteCount)
          return false;
        payload = new Payload(ReadLittleEndian(source, offset + OffsetFrameIndex, 8), 0, 0, kind);
        return true;
      }
      if (count < PayloadByteCount)
        return false;
      if (kind == MarkerKind.SequenceStart)
      {
        if (count != StartPayloadByteCount)
          return false;
        metadata = new StartMetadata(
          unchecked((long)ReadLittleEndian(source, offset + OffsetStartUtcTicks, 8)),
          SequenceId.FromBytes(source, offset + OffsetSequenceId)
        );
      }
      else if (count != PayloadByteCount)
      {
        return false;
      }

      payload = new Payload(
        ReadLittleEndian(source, offset + OffsetFrameIndex, 8),
        unchecked((long)ReadLittleEndian(source, offset + OffsetAnimationTicks, 8)),
        (uint)ReadLittleEndian(source, offset + OffsetRunId, 4),
        kind,
        unchecked((long)ReadLittleEndian(source, offset + OffsetIntendedDisplayTicks, 8)),
        (uint)ReadLittleEndian(source, offset + OffsetTargetFrameTicks, 4),
        unchecked((long)ReadLittleEndian(source, offset + OffsetCpuStartTicks, 8)),
        (uint)ReadLittleEndian(source, offset + OffsetCpuBusyTicks, 4)
      );
      return true;
    }

    /// <summary>
    /// Convert quads to a triangle list: 6 vertices per quad, (TL, TR, BL) (BL, TR, BR), clockwise on screen (+y down). Returns the number
    /// of vertices written, or 0 if the destination is too small.
    /// </summary>
    public static int QuadsToTriangles(Quad[] quads, int quadCount, Vertex[] destination)
    {
      if (quads == null || destination == null || quadCount < 0 || quadCount > quads.Length || destination.Length < quadCount * 6)
        return 0;
      for (int i = 0; i < quadCount; ++i)
        WriteTriangles(quads[i], destination, i * 6);
      return quadCount * 6;
    }

    /// <summary>
    /// Convert quads to an indexed triangle list: 4 vertices (TL, TR, BR, BL) and 6 indices (0,1,3)(3,1,2) per quad, clockwise on screen.
    /// <paramref name="baseVertex"/> is added to every index. Returns an empty count if a destination is too small.
    /// </summary>
    public static IndexedCount QuadsToIndexed(Quad[] quads, int quadCount, Vertex[] vertices, int[] indices, int baseVertex = 0)
    {
      if (
        quads == null
        || vertices == null
        || indices == null
        || quadCount < 0
        || quadCount > quads.Length
        || vertices.Length < quadCount * 4
        || indices.Length < quadCount * 6
      )
        return default;
      for (int i = 0; i < quadCount; ++i)
        WriteIndexed(quads[i], vertices, i * 4, indices, i * 6, baseVertex + (i * 4));
      return new IndexedCount(quadCount * 4, quadCount * 6);
    }

    internal static void WriteTriangles(in Quad quad, Vertex[] destination, int offset)
    {
      byte luma = quad.Dark ? (byte)0 : (byte)255;
      var topLeft = new Vertex(quad.Left, quad.Top, luma);
      var topRight = new Vertex(quad.Right, quad.Top, luma);
      var bottomRight = new Vertex(quad.Right, quad.Bottom, luma);
      var bottomLeft = new Vertex(quad.Left, quad.Bottom, luma);
      destination[offset] = topLeft;
      destination[offset + 1] = topRight;
      destination[offset + 2] = bottomLeft;
      destination[offset + 3] = bottomLeft;
      destination[offset + 4] = topRight;
      destination[offset + 5] = bottomRight;
    }

    internal static void WriteIndexed(in Quad quad, Vertex[] vertices, int vertexOffset, int[] indices, int indexOffset, int firstIndex)
    {
      byte luma = quad.Dark ? (byte)0 : (byte)255;
      vertices[vertexOffset] = new Vertex(quad.Left, quad.Top, luma);
      vertices[vertexOffset + 1] = new Vertex(quad.Right, quad.Top, luma);
      vertices[vertexOffset + 2] = new Vertex(quad.Right, quad.Bottom, luma);
      vertices[vertexOffset + 3] = new Vertex(quad.Left, quad.Bottom, luma);
      indices[indexOffset] = firstIndex;
      indices[indexOffset + 1] = firstIndex + 1;
      indices[indexOffset + 2] = firstIndex + 3;
      indices[indexOffset + 3] = firstIndex + 3;
      indices[indexOffset + 4] = firstIndex + 1;
      indices[indexOffset + 5] = firstIndex + 2;
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

    private static void WriteLittleEndian(byte[] destination, int offset, ulong value, int byteCount)
    {
      for (int i = 0; i < byteCount; ++i)
        destination[offset + i] = (byte)(value >> (8 * i));
    }

    private static ulong ReadLittleEndian(byte[] source, int offset, int byteCount)
    {
      ulong value = 0;
      for (int i = 0; i < byteCount; ++i)
        value |= (ulong)source[offset + i] << (8 * i);
      return value;
    }
  }
}
