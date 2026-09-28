#ifndef MB_FRAMEPACINGDATA_CAPTUREDATARECORD_HPP
#define MB_FRAMEPACINGDATA_CAPTUREDATARECORD_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framemarker/Payload.hpp>
#include <mb/framemarker/StartMetadata.hpp>
#include <mb/framepacingdata/CaptureDataStatus.hpp>
#include <mb/framepacingdata/Constants.hpp>
#include <cstdint>
#include <span>
#include <vector>

namespace MB::FramePacingData
{
  //! One capture of captures.mbcd: the source's frame counter (gaps are captures the recorder dropped), when it arrived on the host's
  //! steady clock and the device's timestamp (100 ns ticks since the capture started), what the source reported, the status, and the
  //! main and second markers' bytes as read (empty when not read).
  struct CaptureDataRecord
  {
    int64_t CaptureIndex{0};
    int64_t HostTicks{0};
    //! UnknownTicks when the device gave none.
    int64_t DeviceTicks{UnknownTicks};
    //! CaptureRecordFlags bits.
    uint32_t Flags{0};
    CaptureDataStatus Status{CaptureDataStatus::Undecodable};
    std::vector<uint8_t> MainBytes;
    std::vector<uint8_t> SecondBytes;

    bool HasDeviceTicks() const noexcept
    {
      return DeviceTicks != UnknownTicks;
    }

    //! The main marker's payload (and a start marker's metadata), decoded with the marker library. False when there is none or it is not
    //! a valid payload.
    bool TryDecodeMain(MB::FrameMarker::Payload& rPayload, MB::FrameMarker::StartMetadata* pMetadata = nullptr) const noexcept;

    //! The second marker's payload (a sync marker), decoded with the marker library.
    bool TryDecodeSecond(MB::FrameMarker::Payload& rPayload) const noexcept;

    //! Parse a record (CaptureDataRecordSize bytes). Throws DataFormatError for an invalid one.
    static CaptureDataRecord Parse(std::span<const uint8_t> bytes);

    bool operator==(const CaptureDataRecord&) const = default;
  };
}

#endif
