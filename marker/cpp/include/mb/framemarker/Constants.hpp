#ifndef MB_FRAMEMARKER_CONSTANTS_HPP
#define MB_FRAMEMARKER_CONSTANTS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Constants of the frame marker format and geometry. See doc/marker-format.md for the full specification.

#include <cstddef>
#include <cstdint>

namespace MB::FrameMarker
{
  //! Every marker (frame, start and end) is QR version 6 (41x41 modules), ECC level M, byte mode, so the marker never changes size.
  //! Version 6-M holds 106 bytes: a frame or end marker uses PayloadByteCount of them, a start marker StartPayloadByteCount; the rest is
  //! room for future fields.
  inline constexpr int32_t QrVersion = 6;
  inline constexpr int32_t QrModuleCount = (4 * QrVersion) + 17;
  inline constexpr std::size_t QrCapacityBytes = 106;

  //! The sync marker (MarkerKind::Sync) is QR version 2 (25x25 modules), ECC level M: magic | format version | kind | frame index u64.
  inline constexpr int32_t SyncQrVersion = 2;
  inline constexpr int32_t SyncQrModuleCount = (4 * SyncQrVersion) + 17;
  inline constexpr std::size_t SyncPayloadByteCount = 12;

  //! Payload header, shared by every marker kind (little endian):
  //! magic "MF" (2) | format version (1) | kind (1) | frame index u64 (8) | animation ticks i64 (8) | run id u32 (4) |
  //! intended display ticks i64 (8) | target frame ticks u32 (4) | cpu start ticks i64 (8) | cpu busy ticks u32 (4)
  //! Start and end markers carry the values of the frame that shows them.
  inline constexpr std::size_t PayloadByteCount = 48;
  inline constexpr uint8_t PayloadMagic0 = 'M';
  inline constexpr uint8_t PayloadMagic1 = 'F';
  inline constexpr uint8_t PayloadFormatVersion = 1;

  //! The start marker's sequence id: 16 opaque bytes (see SequenceId).
  inline constexpr std::size_t SequenceIdByteCount = 16;

  //! Start marker payload: header (48) | start time UTC i64 (8) | sequence id (16)
  inline constexpr std::size_t StartPayloadByteCount = PayloadByteCount + 8u + SequenceIdByteCount;

  //! The longest payload of any kind: the start marker's.
  inline constexpr std::size_t MaxEncodedPayloadByteCount = StartPayloadByteCount;
  static_assert(MaxEncodedPayloadByteCount <= QrCapacityBytes);

  //! C# TimeSpan / DateTime resolution
  inline constexpr int64_t TicksPerSecond = 10'000'000;
  //! C# DateTime ticks (since 0001-01-01) at the Unix epoch
  inline constexpr int64_t UnixEpochDateTimeTicks = 621'355'968'000'000'000;

  //! Recommended distance in source pixels between the marker and the edge of the frame.
  inline constexpr int32_t RecommendedInsetPx = 32;

  inline constexpr int32_t MinModuleSizePx = 1;
  inline constexpr int32_t MaxModuleSizePx = 1024;
  inline constexpr int32_t MaxQuietZoneModules = 16;
  inline constexpr int32_t RecommendedQuietZoneModules = 4;

  //! The packed module matrix (ModuleMatrix::Bits): 1 bit per module, row-major, most significant bit first, continuous across rows, the
  //! last byte zero padded. 211 bytes for a main marker (41x41), 79 for the sync marker (25x25).
  constexpr std::size_t PackedModuleByteCount(const int32_t size) noexcept
  {
    return size <= 0 ? 0u : ((static_cast<std::size_t>(size) * static_cast<std::size_t>(size)) + 7u) / 8u;
  }

  inline constexpr std::size_t MaxPackedModuleByteCount = PackedModuleByteCount(QrModuleCount);
  static_assert(MaxPackedModuleByteCount == 211u);
}

#endif
