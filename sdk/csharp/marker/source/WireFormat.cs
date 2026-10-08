//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Internal to the marker module: the payload's wire format (doc/marker-format.md, the reference; the C++ library's WireFormat must match
//* it byte for byte). Only the encoder, the decoder and the tests use it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Marker
{
  internal static class WireFormat
  {
    /// <summary>
    /// Every main marker (frame, start and end) is QR version 6 (41x41 modules), ECC level M, byte mode, so it never changes size; the sync
    /// marker is QR version 2 (25x25 modules), ECC level M.
    /// </summary>
    public const int QrVersion = 6;

    public const int SyncQrVersion = 2;

    /// <summary>The highest kind byte a payload may carry.</summary>
    public const byte MaxMarkerKindValue = (byte)MarkerKind.Sync;

    /// <summary>
    /// QR version 6-M in byte mode holds 106 bytes: a frame or end marker uses PayloadByteCount of them, a start marker
    /// StartPayloadByteCount; the rest is room for future fields.
    /// </summary>
    public const int QrCapacityBytes = 106;

    /// <summary>QR version 2-M in byte mode holds 26 bytes: a sync marker uses SyncPayloadByteCount of them.</summary>
    public const int SyncQrCapacityBytes = 26;

    public const byte PayloadMagic0 = (byte)'M';
    public const byte PayloadMagic1 = (byte)'F';
    public const byte PayloadFormatVersion = 1;

    /// <summary>Every payload ends with the CRC-32 (<see cref="Crc32"/>, a u32) of all the bytes before it.</summary>
    public const int CrcByteCount = 4;

    // The header every kind starts with (a sync marker has its first SyncFieldsByteCount bytes: which run and frame), little endian,
    // grouped: the format, which run and frame, what the frame shows, the frame pacing, the CPU's work. Every time is in nanoseconds: the
    // animation, intended display and CPU start time eight bytes, the two frame times and CPU busy four (unsigned).
    public const int OffsetMagic0 = 0;
    public const int OffsetMagic1 = 1;
    public const int OffsetVersion = 2;
    public const int OffsetKind = 3;
    public const int OffsetRunId = 4;
    public const int OffsetFrameIndex = 8;
    public const int OffsetFlags = 16;
    public const int OffsetAnimationTime = 17;
    public const int OffsetPreferredFrameTime = 25;
    public const int OffsetTargetFrameTime = 29;
    public const int OffsetIntendedDisplayTime = 33;
    public const int OffsetCpuStartTime = 41;
    public const int OffsetCpuBusy = 49;
    public const int HeaderByteCount = 53;
    public const int SyncFieldsByteCount = 16;

    // A frame or end marker: the header and the CRC. A sync marker: the header's start and the CRC.
    public const int PayloadByteCount = HeaderByteCount + CrcByteCount;
    public const int SyncPayloadByteCount = SyncFieldsByteCount + CrcByteCount;

    // A start marker: the header, then its start time (UTC i64) and sequence id, and the CRC.
    public const int OffsetStartUtcTicks = HeaderByteCount;
    public const int OffsetSequenceId = OffsetStartUtcTicks + 8;
    public const int StartPayloadByteCount = OffsetSequenceId + SequenceId.ByteCount + CrcByteCount;
  }
}
