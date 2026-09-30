#ifndef MB_FRAMEPACING_MARKER_DETAIL_WIREFORMAT_HPP
#define MB_FRAMEPACING_MARKER_DETAIL_WIREFORMAT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the marker module: the payload's wire format (doc/marker-format.md, the reference; C# must match it byte for byte). Only
// the encoder, the decoder and the tests use it.

#include <mb/framepacing/marker/Payload.hpp>
#include <mb/framepacing/marker/SequenceId.hpp>
#include <cstddef>
#include <cstdint>

namespace MB::FramePacing::Marker::Detail
{
  //! QR version 6-M in byte mode holds 106 bytes: a frame or end marker uses PayloadByteCount of them, a start marker
  //! StartPayloadByteCount; the rest is room for future fields.
  inline constexpr std::size_t QrCapacityBytes = 106;

  inline constexpr uint8_t PayloadMagic0 = 'M';
  inline constexpr uint8_t PayloadMagic1 = 'F';
  inline constexpr uint8_t PayloadFormatVersion = 1;

  //! The header every kind starts with (a sync marker is its first SyncPayloadByteCount bytes: which run and frame), little endian,
  //! grouped: the format, which run and frame, what the frame shows, the frame pacing, the CPU's work.
  inline constexpr std::size_t OffsetMagic0 = 0;
  inline constexpr std::size_t OffsetMagic1 = 1;
  inline constexpr std::size_t OffsetVersion = 2;
  inline constexpr std::size_t OffsetKind = 3;
  inline constexpr std::size_t OffsetRunId = 4;
  inline constexpr std::size_t OffsetFrameIndex = 8;
  inline constexpr std::size_t OffsetFlags = 16;
  inline constexpr std::size_t OffsetAnimationTicks = 17;
  inline constexpr std::size_t OffsetPreferredFrameTicks = 25;
  inline constexpr std::size_t OffsetTargetFrameTicks = 29;
  inline constexpr std::size_t OffsetIntendedDisplayTicks = 33;
  inline constexpr std::size_t OffsetCpuStartTicks = 41;
  inline constexpr std::size_t OffsetCpuBusyTicks = 49;
  inline constexpr std::size_t PayloadByteCount = 53;
  inline constexpr std::size_t SyncPayloadByteCount = 16;

  //! A start marker: the header, then its start time (UTC i64) and sequence id.
  inline constexpr std::size_t OffsetStartUtcTicks = PayloadByteCount;
  inline constexpr std::size_t OffsetSequenceId = OffsetStartUtcTicks + 8;
  inline constexpr std::size_t StartPayloadByteCount = OffsetSequenceId + SequenceId::ByteCount;

  static_assert(OffsetKind + 1 == OffsetRunId);
  static_assert(OffsetRunId + 4 == OffsetFrameIndex);
  static_assert(OffsetFrameIndex + 8 == SyncPayloadByteCount);
  static_assert(SyncPayloadByteCount == OffsetFlags);
  static_assert(OffsetFlags + 1 == OffsetAnimationTicks);
  static_assert(OffsetAnimationTicks + 8 == OffsetPreferredFrameTicks);
  static_assert(OffsetPreferredFrameTicks + 4 == OffsetTargetFrameTicks);
  static_assert(OffsetTargetFrameTicks + 4 == OffsetIntendedDisplayTicks);
  static_assert(OffsetIntendedDisplayTicks + 8 == OffsetCpuStartTicks);
  static_assert(OffsetCpuStartTicks + 8 == OffsetCpuBusyTicks);
  static_assert(OffsetCpuBusyTicks + 4 == PayloadByteCount);
  static_assert(StartPayloadByteCount == 77u);
  static_assert(StartPayloadByteCount == Payload::MaxEncodedByteCount);
  static_assert(Payload::MaxEncodedByteCount <= QrCapacityBytes);
}

#endif
