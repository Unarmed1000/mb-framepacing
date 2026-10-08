#ifndef MB_FRAMEPACING_MARKER_DETAIL_WIREFORMAT_HPP
#define MB_FRAMEPACING_MARKER_DETAIL_WIREFORMAT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the marker module: the payload's wire format (doc/marker-format.md, the reference; C# must match it byte for byte). Only
// the encoder, the decoder and the tests use it.

#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/marker/payload/SequenceId.hpp>
#include <cstddef>
#include <cstdint>

namespace MB::FramePacing::Marker::WireFormat
{
  //! Every main marker (frame, start and end) is QR version 6 (41x41 modules), ECC level M, byte mode, so it never changes size; the
  //! sync marker is QR version 2 (25x25 modules), ECC level M.
  inline constexpr int32_t QrVersion = 6;
  inline constexpr int32_t SyncQrVersion = 2;
  static_assert((4 * QrVersion) + 17 == ModuleMatrix::MainSize && (4 * SyncQrVersion) + 17 == ModuleMatrix::SyncSize);

  //! The highest kind byte a payload may carry.
  inline constexpr uint8_t MaxMarkerKindValue = static_cast<uint8_t>(MarkerKind::Sync);

  //! QR version 6-M in byte mode holds 106 bytes: a frame or end marker uses PayloadByteCount of them, a start marker
  //! StartPayloadByteCount; the rest is room for future fields.
  inline constexpr std::size_t QrCapacityBytes = 106;
  //! QR version 2-M in byte mode holds 26 bytes: a sync marker uses SyncPayloadByteCount of them.
  inline constexpr std::size_t SyncQrCapacityBytes = 26;

  inline constexpr uint8_t PayloadMagic0 = 'M';
  inline constexpr uint8_t PayloadMagic1 = 'F';
  inline constexpr uint8_t PayloadFormatVersion = 1;

  //! Every payload ends with the CRC-32 (the core's Crc32Util, a u32) of all the bytes before it.
  inline constexpr std::size_t CrcByteCount = 4;

  //! The header every kind starts with (a sync marker has its first SyncFieldsByteCount bytes: which run and frame), little endian,
  //! grouped: the format, which run and frame, what the frame shows, the frame pacing, the CPU's work. Every time is in nanoseconds:
  //! the animation, intended display and CPU start time eight bytes, the two frame times and CPU busy four (unsigned).
  inline constexpr std::size_t OffsetMagic0 = 0;
  inline constexpr std::size_t OffsetMagic1 = 1;
  inline constexpr std::size_t OffsetVersion = 2;
  inline constexpr std::size_t OffsetKind = 3;
  inline constexpr std::size_t OffsetRunId = 4;
  inline constexpr std::size_t OffsetFrameIndex = 8;
  inline constexpr std::size_t OffsetFlags = 16;
  inline constexpr std::size_t OffsetAnimationTime = 17;
  inline constexpr std::size_t OffsetPreferredFrameTime = 25;
  inline constexpr std::size_t OffsetTargetFrameTime = 29;
  inline constexpr std::size_t OffsetIntendedDisplayTime = 33;
  inline constexpr std::size_t OffsetCpuStartTime = 41;
  inline constexpr std::size_t OffsetCpuBusy = 49;
  inline constexpr std::size_t HeaderByteCount = 53;
  inline constexpr std::size_t SyncFieldsByteCount = 16;

  //! A frame or end marker: the header and the CRC. A sync marker: the header's start and the CRC.
  inline constexpr std::size_t PayloadByteCount = HeaderByteCount + CrcByteCount;
  inline constexpr std::size_t SyncPayloadByteCount = SyncFieldsByteCount + CrcByteCount;

  //! A start marker: the header, then its start time (UTC i64) and sequence id, and the CRC.
  inline constexpr std::size_t OffsetStartUtcTicks = HeaderByteCount;
  inline constexpr std::size_t OffsetSequenceId = OffsetStartUtcTicks + 8;
  inline constexpr std::size_t StartPayloadByteCount = OffsetSequenceId + SequenceId::ByteCount + CrcByteCount;

  static_assert(OffsetKind + 1 == OffsetRunId);
  static_assert(OffsetRunId + 4 == OffsetFrameIndex);
  static_assert(OffsetFrameIndex + 8 == SyncFieldsByteCount);
  static_assert(SyncFieldsByteCount == OffsetFlags);
  static_assert(OffsetFlags + 1 == OffsetAnimationTime);
  static_assert(OffsetAnimationTime + 8 == OffsetPreferredFrameTime);
  static_assert(OffsetPreferredFrameTime + 4 == OffsetTargetFrameTime);
  static_assert(OffsetTargetFrameTime + 4 == OffsetIntendedDisplayTime);
  static_assert(OffsetIntendedDisplayTime + 8 == OffsetCpuStartTime);
  static_assert(OffsetCpuStartTime + 8 == OffsetCpuBusy);
  static_assert(OffsetCpuBusy + 4 == HeaderByteCount);
  static_assert(PayloadByteCount == 57u && SyncPayloadByteCount == 20u && StartPayloadByteCount == 81u);
  static_assert(StartPayloadByteCount == Payload::MaxEncodedByteCount);
  static_assert(Payload::MaxEncodedByteCount <= QrCapacityBytes);
  static_assert(SyncPayloadByteCount <= SyncQrCapacityBytes);
}

#endif
