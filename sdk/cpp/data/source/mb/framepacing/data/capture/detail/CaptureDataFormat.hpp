#ifndef MB_FRAMEPACING_DATA_CAPTURE_DETAIL_CAPTUREDATAFORMAT_HPP
#define MB_FRAMEPACING_DATA_CAPTURE_DETAIL_CAPTUREDATAFORMAT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the data module: the captures.mbcd file format (doc/capture-data-format.md), its version and the header and record sizes.

#include <cstddef>
#include <cstdint>
#include <limits>

namespace MB::FramePacing::Data::CaptureDataFormat
{
  inline constexpr uint32_t Magic = 0x4443424Du;    // "MBCD" little endian
  inline constexpr uint16_t Version = 1;
  inline constexpr std::size_t HeaderSize = 256;
  inline constexpr std::size_t RecordSize = 256;
  inline constexpr std::size_t MaxMarkerLocations = 4;
  //! A record's device timestamp when the capture source gave none.
  inline constexpr int64_t UnknownNanoseconds = std::numeric_limits<int64_t>::min();
  //! Two equal slots: either can hold any payload a main marker's QR code can carry (106 bytes; the longest today, a start marker, is
  //! 81), so a field added to the markers does not change the records.
  inline constexpr std::size_t MainMarkerCapacity = 112;
  inline constexpr std::size_t SecondMarkerCapacity = 112;
}

#endif
