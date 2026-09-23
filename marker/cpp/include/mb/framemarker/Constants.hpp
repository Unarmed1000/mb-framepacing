#ifndef MB_FRAMEMARKER_CONSTANTS_HPP
#define MB_FRAMEMARKER_CONSTANTS_HPP
// SPDX-License-Identifier: BSD-3-Clause
//
// Constants of the frame marker format and geometry. See doc/marker-format.md for the full specification.

#include <cstddef>
#include <cstdint>

namespace MB::FrameMarker
{
  //! Frame and end markers are fixed to QR version 2 (25x25 modules), ECC level M, byte mode, so they never change size.
  inline constexpr int32_t FrameQrVersion = 2;
  //! Start markers carry metadata and use the smallest version in [FrameQrVersion, MaxQrVersion] that fits.
  inline constexpr int32_t MaxQrVersion = 6;

  constexpr int32_t QrModuleCountForVersion(const int32_t version) noexcept
  {
    return (4 * version) + 17;
  }

  inline constexpr int32_t FrameQrModuleCount = QrModuleCountForVersion(FrameQrVersion);
  inline constexpr int32_t MaxQrModuleCount = QrModuleCountForVersion(MaxQrVersion);

  //! Payload header, shared by every marker kind (little endian):
  //! magic "MF" (2) | format version (1) | kind (1) | frame index u64 (8) | animation ticks i64 (8) | run id u32 (4)
  inline constexpr std::size_t PayloadByteCount = 24;
  inline constexpr uint8_t PayloadMagic0 = 'M';
  inline constexpr uint8_t PayloadMagic1 = 'F';
  inline constexpr uint8_t PayloadFormatVersion = 1;

  //! Start marker payload: header (24) | start time UTC i64 (8) | name length u8 (1) | name UTF-8 (0..MaxStartNameBytes)
  inline constexpr std::size_t MaxStartNameBytes = 64;
  inline constexpr std::size_t StartPayloadFixedByteCount = PayloadByteCount + 8u + 1u;
  inline constexpr std::size_t MaxEncodedPayloadByteCount = StartPayloadFixedByteCount + MaxStartNameBytes;

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
}

#endif
