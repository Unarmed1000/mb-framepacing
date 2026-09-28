#ifndef MB_FRAMEPACINGDATA_CAPTURECSVROW_HPP
#define MB_FRAMEPACINGDATA_CAPTURECSVROW_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace MB::FramePacingData
{
  //! One line of captures.csv: one capture as the analysis read it. CaptureTicks is empty for a capture the recorder dropped; Kind, RunId,
  //! FrameIndex and AnimationTicks are the main marker's, when one was read; Payload its bytes.
  struct CaptureCsvRow
  {
    int64_t CaptureIndex{0};
    std::optional<int64_t> CaptureTicks;
    //! Decoded, Undecodable, Torn or NotRecorded.
    std::string Status;
    std::optional<std::string> Kind;
    std::optional<uint32_t> RunId;
    std::optional<uint64_t> FrameIndex;
    std::optional<int64_t> AnimationTicks;
    bool SourceDropBefore{false};
    std::optional<int64_t> HostTicks;
    std::optional<int64_t> DeviceTicks;
    std::vector<uint8_t> Payload;
    std::optional<uint64_t> SecondZoneFrameIndex;
  };
}

#endif
