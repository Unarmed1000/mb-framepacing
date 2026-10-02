#ifndef MB_FRAMEPACING_DATA_CAPTURE_CAPTUREDATARECORD_HPP
#define MB_FRAMEPACING_DATA_CAPTURE_CAPTUREDATARECORD_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/data/capture/CaptureDataStatus.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/marker/payload/StartMetadata.hpp>
#include <cstdint>
#include <optional>
#include <span>
#include <vector>

namespace MB::FramePacing::Data
{
  //! One capture of captures.mbcd: the source's frame counter (gaps are captures the recorder dropped), when it arrived on the host's
  //! steady clock and the device's timestamp (both since the capture started), how many frames the source reported dropping since
  //! the previous record, the status, and the main and second markers' bytes as read (empty when not read).
  struct CaptureDataRecord
  {
    int64_t CaptureIndex{0};
    TickCount64 HostTime;
    //! Empty when the device gave none.
    std::optional<TickCount64> DeviceTime;
    //! How many frames the capture source reported dropping since the previous record (0: none).
    uint32_t SourceDrops{0};
    CaptureDataStatus CaptureStatus{CaptureDataStatus::Undecodable};
    std::vector<uint8_t> MainBytes;
    std::vector<uint8_t> SecondBytes;

    //! The main marker's payload (and a start marker's metadata), decoded with the marker library. False when there is none or it is not
    //! a valid payload.
    bool TryDecodeMain(MB::FramePacing::Marker::Payload& rPayload, MB::FramePacing::Marker::StartMetadata* pMetadata = nullptr) const noexcept;

    //! The second marker's payload (a sync marker), decoded with the marker library.
    bool TryDecodeSecond(MB::FramePacing::Marker::Payload& rPayload) const noexcept;

    //! Parse a record (192 bytes). Throws DataFormatError for an invalid one.
    static CaptureDataRecord Parse(std::span<const uint8_t> bytes);

    bool operator==(const CaptureDataRecord&) const = default;
  };
}

#endif
