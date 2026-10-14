#ifndef MB_FRAMEPACING_DATA_ANALYSIS_FRAMEROW_HPP
#define MB_FRAMEPACING_DATA_ANALYSIS_FRAMEROW_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/data/analysis/OlderFrame.hpp>
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace MB::FramePacing::Data
{
  //! One line of a run's frames CSV: one presented frame. An empty cell (or a column the file lacks) is empty. Every time is in
  //! nanoseconds: points in time are NanosecondTickCounts, on the capture's clock (FirstSeenTime, LastSeenTime,
  //! MainMarkerFirstSeenTime) or the frame pacer's (IntendedDisplayTime, CpuStartTime). A length of time is a NanosecondTimeDuration,
  //! which is never negative: TargetFrameTime, PreferredFrameTime and FrameTime, and the marker's own durations (MarkerTargetFrameTime,
  //! MarkerPreferredFrameTime, CpuBusy: 32 bits in the marker and in the file), as the marker library's Payload returns them. What can
  //! go either way is a NanosecondTimeSpan: AnimationTime, AnimationDelta, AnimationError, Drift, PacingError, PredictionError,
  //! Lateness, CpuWait and ScanoutDelay (the sync marker can be seen before the main marker decodes), and OnScreen and DisplayDelta,
  //! which are the difference of two sightings: a camera can time a frame after the next one's sighting, and the steps then still add
  //! up to the time that passed. FirstSeenTime is the display time, DisplayDelta the display time step, AnimationError the animation
  //! time step minus the display time step. The pacing and CPU fields come from the markers; the last two are EXPERIMENTAL camera
  //! captures' only.
  struct FrameRow
  {
    int32_t Segment{0};
    uint64_t FrameIndex{0};
    NanosecondTimeSpan AnimationTime;
    int64_t FirstCaptureIndex{0};
    NanosecondTickCount FirstSeenTime;
    NanosecondTimeSpan OnScreen;
    int32_t Captures{0};
    uint64_t SkippedBefore{0};
    std::optional<NanosecondTimeSpan> DisplayDelta;
    std::optional<NanosecondTimeSpan> AnimationDelta;
    std::optional<NanosecondTimeSpan> AnimationError;
    NanosecondTimeSpan Drift;
    //! SkippedBefore, UncertainStart, Torn, Late, StaticAfter, StaticBefore, UncertainStep, StaticAssumed, DeltaTimeJitter.
    std::vector<std::string> Flags;
    std::optional<NanosecondTickCount> IntendedDisplayTime;
    //! The marker's target frame time; Marker::Payload::OnDemandFrameTime on demand.
    std::optional<NanosecondTimeDuration> MarkerTargetFrameTime;
    //! In whole refreshes; empty on demand.
    std::optional<NanosecondTimeDuration> TargetFrameTime;
    //! The frame time the application wants to run at; Marker::Payload::OnDemandFrameTime on demand.
    std::optional<NanosecondTimeDuration> MarkerPreferredFrameTime;
    //! The preferred frame time the late share is measured against, in whole refreshes; empty on demand.
    std::optional<NanosecondTimeDuration> PreferredFrameTime;
    std::optional<NanosecondTimeSpan> PacingError;
    std::optional<NanosecondTimeSpan> PredictionError;
    std::optional<NanosecondTimeSpan> Lateness;
    std::optional<NanosecondTickCount> LastSeenTime;
    std::optional<NanosecondTickCount> CpuStartTime;
    std::optional<NanosecondTimeDuration> CpuBusy;
    std::optional<NanosecondTimeDuration> FrameTime;
    std::optional<NanosecondTimeSpan> CpuWait;
    //! The captures that showed an older frame out of order while this frame was the newest, in capture order.
    std::vector<OlderFrame> OlderFrames;
    std::optional<NanosecondTickCount> MainMarkerFirstSeenTime;
    std::optional<NanosecondTimeSpan> ScanoutDelay;
  };
}

#endif
