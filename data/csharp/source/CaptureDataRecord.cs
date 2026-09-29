//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One record of captures.mbcd (doc/capture-data-format.md): 192 bytes per capture, little endian. The capture part (index, host and device
//* ticks, flags) is laid out like a frames.mbfc record header; then the status and the markers' encoded bytes as they were read.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;
using System.IO;
using MB.FrameMarker;

namespace MB.FramePacing.Data
{
  /// <param name="CaptureIndex">The capture source's frame counter; gaps are captures the recorder dropped.</param>
  /// <param name="HostTicks">When the frame arrived, on the host's steady clock (TimeSpan ticks since the capture started).</param>
  /// <param name="DeviceTicks">The capture device's timestamp (TimeSpan ticks), <see cref="UnknownTicks"/> if none.</param>
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

    /// <summary>A device timestamp that the capture source did not give.</summary>
    public const long UnknownTicks = long.MinValue;

    public const int StatusOffset = 28;
    public const int MainLengthOffset = 29;
    public const int SecondLengthOffset = 30;
    public const int MainOffset = 32;

    // Two equal slots: either can hold any marker payload (the longest, a start marker, is 77 bytes)
    public const int MainCapacity = 80;
    public const int SecondOffset = MainOffset + MainCapacity;
    public const int SecondCapacity = Size - SecondOffset;

    public bool HasDeviceTicks => DeviceTicks != UnknownTicks;

    /// <summary>The main marker's payload, decoded with the marker library. False when there is none or it is not a valid payload.</summary>
    public bool TryDecodeMain(out Payload payload, out StartMetadata metadata) => TryDecode(MainBytes, out payload, out metadata);

    /// <summary>The second marker's payload (a sync marker), decoded with the marker library.</summary>
    public bool TryDecodeSecond(out Payload payload) => TryDecode(SecondBytes, out payload, out _);

    public void Write(Span<byte> destination)
    {
      if (destination.Length < Size)
        throw new ArgumentException("Record buffer too small", nameof(destination));
      destination.Slice(0, Size).Clear();
      WriteCapture(destination, CaptureIndex, HostTicks, DeviceTicks, Flags);
      WriteDecoded(destination, Status, MainBytes, SecondBytes);
    }

    /// <summary>Write the decoded part (status and marker bytes) of a record; the capture part (bytes 0 to 27) is left as it is.</summary>
    public static void WriteDecoded(Span<byte> destination, CaptureDataStatus status, ReadOnlySpan<byte> main, ReadOnlySpan<byte> second)
    {
      if (main.Length > MainCapacity || second.Length > SecondCapacity)
        throw new ArgumentException($"A marker of {Math.Max(main.Length, second.Length)} bytes does not fit a capture data record");
      destination.Slice(StatusOffset, Size - StatusOffset).Clear();
      destination[StatusOffset] = (byte)status;
      destination[MainLengthOffset] = (byte)main.Length;
      destination[SecondLengthOffset] = (byte)second.Length;
      main.CopyTo(destination.Slice(MainOffset));
      second.CopyTo(destination.Slice(SecondOffset));
    }

    /// <summary>Write the capture part (index, host and device ticks, flags: bytes 0 to 27) of a record.</summary>
    public static void WriteCapture(Span<byte> destination, long captureIndex, long hostTicks, long deviceTicks, CaptureRecordFlags flags)
    {
      BinaryPrimitives.WriteInt64LittleEndian(destination, captureIndex);
      BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(8), hostTicks);
      BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(16), deviceTicks);
      BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(24), (uint)flags);
    }

    public static CaptureDataRecord Read(ReadOnlySpan<byte> source)
    {
      if (source.Length < Size)
        throw new ArgumentException("Record buffer too small", nameof(source));
      byte status = source[StatusOffset];
      int mainLength = source[MainLengthOffset];
      int secondLength = source[SecondLengthOffset];
      if (status > (byte)CaptureDataStatus.Torn || mainLength > MainCapacity || secondLength > SecondCapacity)
        throw new InvalidDataException("Invalid capture data record");
      return new CaptureDataRecord(
        BinaryPrimitives.ReadInt64LittleEndian(source),
        BinaryPrimitives.ReadInt64LittleEndian(source.Slice(8)),
        BinaryPrimitives.ReadInt64LittleEndian(source.Slice(16)),
        (CaptureRecordFlags)BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(24)),
        (CaptureDataStatus)status,
        mainLength > 0 ? source.Slice(MainOffset, mainLength).ToArray() : null,
        secondLength > 0 ? source.Slice(SecondOffset, secondLength).ToArray() : null
      );
    }

    private static bool TryDecode(byte[]? bytes, out Payload payload, out StartMetadata metadata)
    {
      if (bytes != null && Marker.TryDecodePayload(bytes, out payload, out metadata))
        return true;
      payload = default;
      metadata = default;
      return false;
    }
  }
}
