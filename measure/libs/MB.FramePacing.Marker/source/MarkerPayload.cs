//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The frame marker payload. Wire format is defined in doc/marker-format.md and must match marker/cpp/src/Payload.cpp byte for byte.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;
using System.Text;

namespace MB.FramePacing.Marker
{
  /// <summary>What a marker means. Frame markers are drawn every frame of a test run; the sequence markers bracket the run.</summary>
  public enum MarkerKind : byte
  {
    Frame = 0,
    SequenceStart = 1,
    SequenceEnd = 2,
  }

  /// <summary>Extra data carried by a <see cref="MarkerKind.SequenceStart"/> marker.</summary>
  /// <param name="UtcTicks">Wall clock start time as <see cref="DateTime"/> UTC ticks, 0 = unknown.</param>
  /// <param name="Name">Test name, at most <see cref="MarkerPayload.MaxStartNameBytes"/> bytes as UTF-8.</param>
  public sealed record StartMetadata(long UtcTicks, string Name)
  {
    public static readonly StartMetadata Empty = new StartMetadata(0, string.Empty);

    public DateTime? StartTimeUtc => UtcTicks > 0 && UtcTicks <= DateTime.MaxValue.Ticks ? new DateTime(UtcTicks, DateTimeKind.Utc) : null;

    public static StartMetadata Create(DateTime startTimeUtc, string name) => new StartMetadata(startTimeUtc.ToUniversalTime().Ticks, name);
  }

  /// <summary>The header carried by every marker.</summary>
  /// <param name="FrameIndex">The application's own rendered-frame counter. Unrelated to the capture card's frame counter.</param>
  /// <param name="AnimationTicks">The animation time the frame was rendered for, in <see cref="TimeSpan"/> ticks (100ns).</param>
  /// <param name="RunId">Identifies one test run: the start marker, every frame marker and the end marker of a run share it.</param>
  public readonly record struct MarkerPayload(ulong FrameIndex, long AnimationTicks, uint RunId = 0, MarkerKind Kind = MarkerKind.Frame)
  {
    /// <summary>Size of the header, which is the complete payload of frame and end markers.</summary>
    public const int ByteCount = 24;
    public const int MaxStartNameBytes = 64;
    public const int StartFixedByteCount = ByteCount + 8 + 1;
    public const int MaxEncodedByteCount = StartFixedByteCount + MaxStartNameBytes;
    public const byte Magic0 = (byte)'M';
    public const byte Magic1 = (byte)'F';
    public const byte FormatVersion = 1;

    private const int OffsetMagic0 = 0;
    private const int OffsetMagic1 = 1;
    private const int OffsetVersion = 2;
    private const int OffsetKind = 3;
    private const int OffsetFrameIndex = 4;
    private const int OffsetAnimationTicks = 12;
    private const int OffsetRunId = 20;
    private const int OffsetStartUtcTicks = ByteCount;
    private const int OffsetStartNameLength = OffsetStartUtcTicks + 8;
    private const int OffsetStartName = OffsetStartNameLength + 1;

    private static readonly UTF8Encoding g_utf8 = new UTF8Encoding(false, true);

    public TimeSpan AnimationTime => TimeSpan.FromTicks(AnimationTicks);

    /// <summary>Serialize the payload. Start markers append the metadata (empty if null), other kinds ignore it.</summary>
    public byte[] Encode(StartMetadata? metadata = null)
    {
      if (Kind != MarkerKind.SequenceStart)
      {
        var header = new byte[ByteCount];
        WriteHeader(header);
        return header;
      }

      metadata ??= StartMetadata.Empty;
      var name = g_utf8.GetBytes(metadata.Name);
      if (name.Length > MaxStartNameBytes)
        throw new ArgumentException($"The start name is {name.Length} bytes as UTF-8, the limit is {MaxStartNameBytes}", nameof(metadata));

      var bytes = new byte[StartFixedByteCount + name.Length];
      WriteHeader(bytes);
      BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(OffsetStartUtcTicks, 8), metadata.UtcTicks);
      bytes[OffsetStartNameLength] = (byte)name.Length;
      name.CopyTo(bytes, OffsetStartName);
      return bytes;
    }

    /// <summary>Parse the wire format. Fails on a wrong length, magic, format version, unknown kind or invalid UTF-8 name.</summary>
    /// <param name="metadata">The start metadata for a start marker, otherwise null.</param>
    public static bool TryDecode(ReadOnlySpan<byte> src, out MarkerPayload payload, out StartMetadata? metadata)
    {
      payload = default;
      metadata = null;
      if (
        src.Length < ByteCount
        || src[OffsetMagic0] != Magic0
        || src[OffsetMagic1] != Magic1
        || src[OffsetVersion] != FormatVersion
        || src[OffsetKind] > (byte)MarkerKind.SequenceEnd
      )
        return false;

      var kind = (MarkerKind)src[OffsetKind];
      if (kind == MarkerKind.SequenceStart)
      {
        if (src.Length < StartFixedByteCount)
          return false;
        int nameLength = src[OffsetStartNameLength];
        if (nameLength > MaxStartNameBytes || src.Length != StartFixedByteCount + nameLength)
          return false;
        string name;
        try
        {
          name = g_utf8.GetString(src.Slice(OffsetStartName, nameLength));
        }
        catch (DecoderFallbackException)
        {
          return false;
        }
        metadata = new StartMetadata(BinaryPrimitives.ReadInt64LittleEndian(src.Slice(OffsetStartUtcTicks, 8)), name);
      }
      else if (src.Length != ByteCount)
        return false;

      payload = new MarkerPayload(
        BinaryPrimitives.ReadUInt64LittleEndian(src.Slice(OffsetFrameIndex, 8)),
        BinaryPrimitives.ReadInt64LittleEndian(src.Slice(OffsetAnimationTicks, 8)),
        BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(OffsetRunId, 4)),
        kind
      );
      return true;
    }

    public static bool TryDecode(ReadOnlySpan<byte> src, out MarkerPayload payload) => TryDecode(src, out payload, out _);

    private void WriteHeader(Span<byte> dst)
    {
      dst[OffsetMagic0] = Magic0;
      dst[OffsetMagic1] = Magic1;
      dst[OffsetVersion] = FormatVersion;
      dst[OffsetKind] = (byte)Kind;
      BinaryPrimitives.WriteUInt64LittleEndian(dst.Slice(OffsetFrameIndex, 8), FrameIndex);
      BinaryPrimitives.WriteInt64LittleEndian(dst.Slice(OffsetAnimationTicks, 8), AnimationTicks);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(OffsetRunId, 4), RunId);
    }
  }
}
