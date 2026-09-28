//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One record of captures.mbcd: what one captured frame was (capture index, the host and the capture device's clock, flags) and what its
//* markers said (status and the encoded QR bytes as read). Fixed size and little endian, see doc/capture-data-format.md. The first 28 bytes
//* have the layout of the .mbfc record header (CaptureRecordHeader).
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;
using System.IO;

namespace MB.FramePacing.Capture
{
  /// <param name="CaptureIndex">The capture source's frame counter; gaps are captures the recorder dropped.</param>
  /// <param name="HostTicks">When the frame arrived, on the host's steady clock (TimeSpan ticks since the capture started).</param>
  /// <param name="DeviceTicks">The capture device's timestamp (TimeSpan ticks), <see cref="CaptureRecordHeader.UnknownTicks"/> if none.</param>
  /// <param name="Flags">What the source reported (a drop before this frame).</param>
  /// <param name="Status">Decoded, undecodable or torn.</param>
  /// <param name="MainBytes">The main marker's encoded bytes as read (frame, start or end marker), when it was read.</param>
  /// <param name="SecondBytes">The second marker's encoded bytes (sync marker, a camera's second zone), when it was read.</param>
  public readonly record struct CaptureDataRecord(
    long CaptureIndex,
    long HostTicks,
    long DeviceTicks,
    CaptureRecordFlags Flags,
    CaptureDataStatus Status,
    byte[]? MainBytes,
    byte[]? SecondBytes
  )
  {
    public const int Size = 192;
    public const int StatusOffset = 28;
    public const int MainLengthOffset = 29;
    public const int SecondLengthOffset = 30;
    public const int MainOffset = 32;
    public const int MainCapacity = 112;
    public const int SecondOffset = MainOffset + MainCapacity;
    public const int SecondCapacity = Size - SecondOffset;

    public bool HasDeviceTicks => DeviceTicks != CaptureRecordHeader.UnknownTicks;

    public void Write(Span<byte> dst)
    {
      if (dst.Length < Size)
        throw new ArgumentException("Record buffer too small", nameof(dst));
      dst.Slice(0, Size).Clear();
      BinaryPrimitives.WriteInt64LittleEndian(dst, CaptureIndex);
      BinaryPrimitives.WriteInt64LittleEndian(dst.Slice(8), HostTicks);
      BinaryPrimitives.WriteInt64LittleEndian(dst.Slice(16), DeviceTicks);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(24), (uint)Flags);
      WriteDecoded(dst, Status, MainBytes, SecondBytes);
    }

    /// <summary>Write the decoded part (status and marker bytes) of a record; the capture part (bytes 0 to 27) is left as it is.</summary>
    public static void WriteDecoded(Span<byte> dst, CaptureDataStatus status, ReadOnlySpan<byte> main, ReadOnlySpan<byte> second)
    {
      if (main.Length > MainCapacity || second.Length > SecondCapacity)
        throw new ArgumentException($"A marker of {Math.Max(main.Length, second.Length)} bytes does not fit a capture data record");
      dst.Slice(StatusOffset, Size - StatusOffset).Clear();
      dst[StatusOffset] = (byte)status;
      dst[MainLengthOffset] = (byte)main.Length;
      dst[SecondLengthOffset] = (byte)second.Length;
      main.CopyTo(dst.Slice(MainOffset));
      second.CopyTo(dst.Slice(SecondOffset));
    }

    /// <summary>Copy the capture part (index, host and device ticks, flags) from a .mbfc record header.</summary>
    public static void WriteCapture(Span<byte> dst, CaptureRecordHeader header)
    {
      BinaryPrimitives.WriteInt64LittleEndian(dst, header.CaptureIndex);
      BinaryPrimitives.WriteInt64LittleEndian(dst.Slice(8), header.HostTicks);
      BinaryPrimitives.WriteInt64LittleEndian(dst.Slice(16), header.DeviceTicks);
      BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(24), (uint)header.Flags);
    }

    public static CaptureDataRecord Read(ReadOnlySpan<byte> src)
    {
      if (src.Length < Size)
        throw new ArgumentException("Record buffer too small", nameof(src));
      byte status = src[StatusOffset];
      int mainLength = src[MainLengthOffset];
      int secondLength = src[SecondLengthOffset];
      if (status > (byte)CaptureDataStatus.Torn || mainLength > MainCapacity || secondLength > SecondCapacity)
        throw new InvalidDataException("Invalid capture data record");
      return new CaptureDataRecord(
        BinaryPrimitives.ReadInt64LittleEndian(src),
        BinaryPrimitives.ReadInt64LittleEndian(src.Slice(8)),
        BinaryPrimitives.ReadInt64LittleEndian(src.Slice(16)),
        (CaptureRecordFlags)BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(24)),
        (CaptureDataStatus)status,
        mainLength > 0 ? src.Slice(MainOffset, mainLength).ToArray() : null,
        secondLength > 0 ? src.Slice(SecondOffset, secondLength).ToArray() : null
      );
    }
  }
}
