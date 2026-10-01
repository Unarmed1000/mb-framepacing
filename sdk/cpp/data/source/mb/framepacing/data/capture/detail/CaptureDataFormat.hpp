#ifndef MB_FRAMEPACING_DATA_CAPTURE_DETAIL_CAPTUREDATAFORMAT_HPP
#define MB_FRAMEPACING_DATA_CAPTURE_DETAIL_CAPTUREDATAFORMAT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the data module: the captures.mbcd file format (doc/capture-data-format.md), its version and the header and record sizes.

#include <cstddef>
#include <cstdint>

namespace MB::FramePacing::Data::CaptureDataFormat
{
  inline constexpr uint32_t Magic = 0x4443424Du;    // "MBCD" little endian
  inline constexpr uint16_t Version = 1;
  inline constexpr std::size_t HeaderSize = 256;
  inline constexpr std::size_t RecordSize = 192;
  inline constexpr std::size_t MaxMarkerLocations = 4;
  //! Two equal slots: either can hold any marker payload (the longest, a start marker, is 77 bytes).
  inline constexpr std::size_t MainMarkerCapacity = 80;
  inline constexpr std::size_t SecondMarkerCapacity = 80;
}

#endif
