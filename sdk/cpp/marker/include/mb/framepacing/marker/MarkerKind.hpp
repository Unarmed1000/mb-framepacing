#ifndef MB_FRAMEPACING_MARKER_MARKERKIND_HPP
#define MB_FRAMEPACING_MARKER_MARKERKIND_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Marker
{
  //! What a marker means. Frame markers are drawn every frame of a test run; the sequence markers bracket the run so the analyzer can cut
  //! the capture to exactly the measured window. See doc/marker-format.md "Test sequences". A sync marker is the small second marker for
  //! tearing checks and camera timing: it only carries the frame index.
  enum class MarkerKind : uint8_t
  {
    Frame = 0,
    SequenceStart = 1,
    SequenceEnd = 2,
    Sync = 3,
  };

  inline constexpr uint8_t MaxMarkerKindValue = static_cast<uint8_t>(MarkerKind::Sync);

  //! Every main marker (frame, start and end) is QR version 6 (41x41 modules), ECC level M, byte mode, so it never changes size.
  inline constexpr int32_t QrVersion = 6;
  inline constexpr int32_t QrModuleCount = (4 * QrVersion) + 17;

  //! The sync marker (MarkerKind::Sync) is QR version 2 (25x25 modules), ECC level M.
  inline constexpr int32_t SyncQrVersion = 2;
  inline constexpr int32_t SyncQrModuleCount = (4 * SyncQrVersion) + 17;

  //! Modules per side of a marker's symbol: the main marker (frame, start and end) or the smaller sync marker.
  constexpr int32_t QrModuleCountFor(const MarkerKind kind) noexcept
  {
    return kind == MarkerKind::Sync ? SyncQrModuleCount : QrModuleCount;
  }
}

#endif
