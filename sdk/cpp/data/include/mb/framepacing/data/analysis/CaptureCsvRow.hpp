#ifndef MB_FRAMEPACING_DATA_ANALYSIS_CAPTURECSVROW_HPP
#define MB_FRAMEPACING_DATA_ANALYSIS_CAPTURECSVROW_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace MB::FramePacing::Data
{
  //! One line of captures.csv: one capture as the analysis read it. CaptureTime (on the capture's clock) is empty for a capture the recorder dropped;
  //! Kind, RunId, FrameIndex and AnimationTime are the main marker's, when one was read; Payload its bytes. SourceDropsBefore: frames the source
  //! reported dropping before it; MissedBefore: refreshes the device clock says were missed since the previous capture (0 on the host
  //! clock); SyncRunId and SyncFrameIndex: the sync marker's, when it was read.
  struct CaptureCsvRow
  {
    int64_t CaptureIndex{0};
    std::optional<TickCount64> CaptureTime;
    //! Decoded, Undecodable, Torn or NotRecorded.
    std::string Status;
    std::optional<std::string> Kind;
    std::optional<uint32_t> RunId;
    std::optional<uint64_t> FrameIndex;
    std::optional<TimeSpan> AnimationTime;
    int64_t SourceDropsBefore{0};
    int64_t MissedBefore{0};
    std::optional<uint32_t> SyncRunId;
    std::optional<uint64_t> SyncFrameIndex;
    std::optional<TickCount64> HostTime;
    std::optional<TickCount64> DeviceTime;
    std::vector<uint8_t> Payload;
  };
}

#endif
