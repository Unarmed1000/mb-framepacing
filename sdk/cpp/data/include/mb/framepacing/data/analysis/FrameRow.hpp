#ifndef MB_FRAMEPACING_DATA_ANALYSIS_FRAMEROW_HPP
#define MB_FRAMEPACING_DATA_ANALYSIS_FRAMEROW_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/data/analysis/OlderFrame.hpp>
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace MB::FramePacing::Data
{
  //! One line of a run's frames CSV: one presented frame. An empty cell (or a column the file lacks) is empty. Points in time are
  //! TickCount64s, on the capture's clock (FirstSeenTime, LastSeenTime, MainMarkerFirstSeenTime) or the frame pacer's
  //! (IntendedDisplayTime, CpuStartTime); spans are TimeSpans; the marker's own 32-bit values (MarkerTargetFrameTime,
  //! MarkerPreferredFrameTime, CpuBusy) are TimeSpan32s. FirstSeenTime is the display time, DisplayDelta the display time step,
  //! AnimationError the animation time step minus the display time step. The pacing and CPU fields come from the markers; the last two
  //! are EXPERIMENTAL camera captures' only.
  struct FrameRow
  {
    int32_t Segment{0};
    uint64_t FrameIndex{0};
    TimeSpan AnimationTime;
    int64_t FirstCaptureIndex{0};
    TickCount64 FirstSeenTime;
    TimeSpan OnScreen;
    int32_t Captures{0};
    uint64_t SkippedBefore{0};
    std::optional<TimeSpan> DisplayDelta;
    std::optional<TimeSpan> AnimationDelta;
    std::optional<TimeSpan> AnimationError;
    TimeSpan Drift;
    //! SkippedBefore, UncertainStart, Torn, Late, StaticAfter, StaticBefore, UncertainStep, StaticAssumed.
    std::vector<std::string> Flags;
    std::optional<TickCount64> IntendedDisplayTime;
    //! The marker's target frame time; Marker::Payload::OnDemandFrameTime on demand.
    std::optional<TimeSpan32> MarkerTargetFrameTime;
    //! In whole refreshes; empty on demand.
    std::optional<TimeSpan> TargetFrameTime;
    //! The frame time the application wants to run at; Marker::Payload::OnDemandFrameTime on demand.
    std::optional<TimeSpan32> MarkerPreferredFrameTime;
    //! The preferred frame time the late share is measured against, in whole refreshes; empty on demand.
    std::optional<TimeSpan> PreferredFrameTime;
    std::optional<TimeSpan> PacingError;
    std::optional<TimeSpan> PredictionError;
    std::optional<TimeSpan> Lateness;
    std::optional<TickCount64> LastSeenTime;
    std::optional<TickCount64> CpuStartTime;
    std::optional<TimeSpan32> CpuBusy;
    std::optional<TimeSpan> FrameTime;
    std::optional<TimeSpan> CpuWait;
    //! The captures that showed an older frame out of order while this frame was the newest, in capture order.
    std::vector<OlderFrame> OlderFrames;
    std::optional<TickCount64> MainMarkerFirstSeenTime;
    std::optional<TimeSpan> ScanoutDelay;
  };
}

#endif
