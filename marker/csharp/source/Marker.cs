//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The marker format and geometry: constants, sizing and placement, the payload wire format and quad to vertex conversion. The same API as
//* the C++ library (MB::FrameMarker); the specification is doc/marker-format.md. Nothing here allocates, except TryDecodePayload (it
//* returns the start marker's name), which is not meant for the per-frame path.
//*
//* Coordinates are pixels with the origin at the top-left corner, +x to the right and +y down. Every quad edge and every vertex lies on
//* an integer pixel edge.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Text;

namespace MB.FrameMarker
{
  public static class Marker
  {
    /// <summary>Frame and end markers are fixed to QR version 2 (25x25 modules), error correction level M, byte mode.</summary>
    public const int FrameQrVersion = 2;

    /// <summary>Start markers carry metadata and use the smallest version in [FrameQrVersion, MaxQrVersion] that fits.</summary>
    public const int MaxQrVersion = 6;

    public const int FrameQrModuleCount = (4 * FrameQrVersion) + 17;
    public const int MaxQrModuleCount = (4 * MaxQrVersion) + 17;

    /// <summary>Payload header, shared by every marker kind (little endian): magic "MF" | format version | kind | frame index u64 |
    /// animation ticks i64 | run id u32.</summary>
    public const int PayloadByteCount = 24;
    public const byte PayloadMagic0 = (byte)'M';
    public const byte PayloadMagic1 = (byte)'F';
    public const byte PayloadFormatVersion = 1;

    /// <summary>Start marker payload: header | start time UTC i64 | name length u8 | name UTF-8 (0..MaxStartNameBytes).</summary>
    public const int MaxStartNameBytes = 64;
    public const int StartPayloadFixedByteCount = PayloadByteCount + 8 + 1;
    public const int MaxEncodedPayloadByteCount = StartPayloadFixedByteCount + MaxStartNameBytes;

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
    public const int MaxQuadCount = 1 + (MaxQrModuleCount * ((MaxQrModuleCount + 1) / 2));

    /// <summary>Upper bound on the number of quads for a frame or end marker.</summary>
    public const int MaxFrameQuadCount = 1 + (FrameQrModuleCount * ((FrameQrModuleCount + 1) / 2));

    public const int MaxTriangleVertexCount = MaxQuadCount * 6;
    public const int MaxIndexedVertexCount = MaxQuadCount * 4;
    public const int MaxIndexCount = MaxQuadCount * 6;
    public const int MaxFrameTriangleVertexCount = MaxFrameQuadCount * 6;
    public const int MaxFrameIndexedVertexCount = MaxFrameQuadCount * 4;
    public const int MaxFrameIndexCount = MaxFrameQuadCount * 6;

    private const int OffsetKind = 3;
    private const int OffsetFrameIndex = 4;
    private const int OffsetAnimationTicks = 12;
    private const int OffsetRunId = 20;
    private const int OffsetStartUtcTicks = PayloadByteCount;
    private const int OffsetStartNameLength = OffsetStartUtcTicks + 8;
    private const int OffsetStartName = OffsetStartNameLength + 1;

    // Decoding rejects names that are not valid UTF-8, like the analysis tools
    private static readonly UTF8Encoding g_strictUtf8 = new UTF8Encoding(false, true);

    public static int QrModuleCountForVersion(int version) => (4 * version) + 17;

    public static bool IsValid(in Options options) =>
      options.ModuleSizePx >= MinModuleSizePx
      && options.ModuleSizePx <= MaxModuleSizePx
      && options.QuietZoneModules >= 0
      && options.QuietZoneModules <= MaxQuietZoneModules;

    /// <summary>Width and height in source pixels of a marker (symbol + quiet zone) with the given symbol size.</summary>
    public static int MarkerSizePx(in Options options, int moduleCount) => (moduleCount + (2 * options.QuietZoneModules)) * options.ModuleSizePx;

    /// <summary>Width and height in source pixels of a frame or end marker (symbol + quiet zone).</summary>
    public static int MarkerSizePx(in Options options) => MarkerSizePx(options, FrameQrModuleCount);

    /// <summary>Largest possible start marker (a 64 byte name). Keep this area free around the marker origin while the start marker shows.</summary>
    public static int MaxMarkerSizePx(in Options options) => MarkerSizePx(options, MaxQrModuleCount);

    /// <summary>Hard minimum module size: 2 stored pixels per module after all scaling (source -> capture -> stored).</summary>
    public static int MinimumModuleSizePx(int sourceHeight, int storedHeight) => ModuleSizeForStoredPx(2, sourceHeight, storedHeight);

    /// <summary>Recommended module size: 3 stored pixels per module, or 4 when the capture card delivers MJPEG.</summary>
    public static int RecommendModuleSizePx(int sourceHeight, int storedHeight, bool mjpeg = false) =>
      ModuleSizeForStoredPx(mjpeg ? 4 : 3, sourceHeight, storedHeight);

    /// <summary>
    /// Recommended marker origin for the given slot. <paramref name="alignPx"/> should be the integer downscale ratio (1 if none) so module
    /// edges land on stored pixel edges.
    /// </summary>
    public static Point RecommendedOrigin(MarkerSlot slot, int sourceWidth, int sourceHeight, in Options options, int alignPx = 1)
    {
      int size = MarkerSizePx(options);
      int inset = AlignUp(RecommendedInsetPx, alignPx);
      switch (slot)
      {
        case MarkerSlot.MiddleLeft:
          return new Point(inset, AlignDown((sourceHeight - size) / 2, alignPx));
        case MarkerSlot.BottomLeft:
          return new Point(inset, AlignDown(sourceHeight - inset - size, alignPx));
        default:
          return new Point(inset, inset);
      }
    }

    /// <summary>Convert a wall clock time to DateTime UTC ticks (the <see cref="StartMetadata.UtcTicks"/> format).</summary>
    public static long ToDateTimeTicks(DateTime time) => time.ToUniversalTime().Ticks;

    /// <summary>Convert seconds (for example an animation clock) to TimeSpan ticks, rounded to the nearest tick.</summary>
    public static long SecondsToTicks(double seconds) => (long)Math.Round(seconds * TicksPerSecond);

    /// <summary>
    /// Serialize the payload into <paramref name="destination"/> at <paramref name="offset"/>. Start markers append the metadata, other kinds
    /// ignore it. <see cref="MaxEncodedPayloadByteCount"/> bytes are always enough. Returns the number of bytes written, or 0 if the
    /// destination is too small or the name is longer than <see cref="MaxStartNameBytes"/> bytes as UTF-8.
    /// </summary>
    public static int EncodePayload(in Payload payload, in StartMetadata metadata, byte[] destination, int offset = 0)
    {
      bool isStart = payload.Kind == MarkerKind.SequenceStart;
      string name = metadata.Name ?? string.Empty;
      int nameBytes = isStart ? Encoding.UTF8.GetByteCount(name) : 0;
      if (nameBytes > MaxStartNameBytes)
        return 0;
      int byteCount = isStart ? StartPayloadFixedByteCount + nameBytes : PayloadByteCount;
      if (destination == null || offset < 0 || destination.Length - offset < byteCount)
        return 0;

      destination[offset] = PayloadMagic0;
      destination[offset + 1] = PayloadMagic1;
      destination[offset + 2] = PayloadFormatVersion;
      destination[offset + OffsetKind] = (byte)payload.Kind;
      WriteLittleEndian(destination, offset + OffsetFrameIndex, payload.FrameIndex, 8);
      WriteLittleEndian(destination, offset + OffsetAnimationTicks, unchecked((ulong)payload.AnimationTicks), 8);
      WriteLittleEndian(destination, offset + OffsetRunId, payload.RunId, 4);
      if (isStart)
      {
        WriteLittleEndian(destination, offset + OffsetStartUtcTicks, unchecked((ulong)metadata.UtcTicks), 8);
        destination[offset + OffsetStartNameLength] = (byte)nameBytes;
        Encoding.UTF8.GetBytes(name, 0, name.Length, destination, offset + OffsetStartName);
      }
      return byteCount;
    }

    /// <summary>Serialize a frame or end marker payload (start markers need <see cref="EncodePayload(in Payload, in StartMetadata, byte[], int)"/>).</summary>
    public static int EncodePayload(in Payload payload, byte[] destination, int offset = 0) => EncodePayload(payload, default, destination, offset);

    /// <summary>
    /// Parse the wire format. Returns false on a wrong length, magic, format version, an unknown kind or (start markers) a name that is not
    /// valid UTF-8. Allocates the name string, so it is not meant for the per-frame path.
    /// </summary>
    public static bool TryDecodePayload(byte[] source, int offset, int count, out Payload payload, out StartMetadata metadata)
    {
      payload = default;
      metadata = default;
      if (
        source == null
        || offset < 0
        || count < PayloadByteCount
        || source.Length - offset < count
        || source[offset] != PayloadMagic0
        || source[offset + 1] != PayloadMagic1
        || source[offset + 2] != PayloadFormatVersion
        || source[offset + OffsetKind] > (byte)MarkerKind.SequenceEnd
      )
        return false;

      var kind = (MarkerKind)source[offset + OffsetKind];
      if (kind == MarkerKind.SequenceStart)
      {
        if (count < StartPayloadFixedByteCount)
          return false;
        int nameLength = source[offset + OffsetStartNameLength];
        if (nameLength > MaxStartNameBytes || count != StartPayloadFixedByteCount + nameLength)
          return false;
        string name;
        try
        {
          name = g_strictUtf8.GetString(source, offset + OffsetStartName, nameLength);
        }
        catch (DecoderFallbackException)
        {
          return false;
        }
        metadata = new StartMetadata(unchecked((long)ReadLittleEndian(source, offset + OffsetStartUtcTicks, 8)), name);
      }
      else if (count != PayloadByteCount)
      {
        return false;
      }

      payload = new Payload(
        ReadLittleEndian(source, offset + OffsetFrameIndex, 8),
        unchecked((long)ReadLittleEndian(source, offset + OffsetAnimationTicks, 8)),
        (uint)ReadLittleEndian(source, offset + OffsetRunId, 4),
        kind
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
