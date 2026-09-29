#ifndef MB_FRAMEPACINGDATA_FRAMEROW_HPP
#define MB_FRAMEPACINGDATA_FRAMEROW_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacingdata/OlderFrame.hpp>
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace MB::FramePacingData
{
  //! One line of a run's frames CSV: one presented frame. Times are 100 ns ticks; an empty cell (or a column the file lacks) is empty.
  //! FirstSeenTicks is the display time, DisplayDeltaTicks the display time step, AnimationErrorTicks the animation time step minus the
  //! display time step. The pacing and CPU fields come from the markers; the last two are EXPERIMENTAL camera captures' only.
  struct FrameRow
  {
    int32_t Segment{0};
    uint64_t FrameIndex{0};
    int64_t AnimationTicks{0};
    int64_t FirstCaptureIndex{0};
    int64_t FirstSeenTicks{0};
    int64_t OnScreenTicks{0};
    int32_t Captures{0};
    uint64_t SkippedBefore{0};
    std::optional<int64_t> DisplayDeltaTicks;
    std::optional<int64_t> AnimationDeltaTicks;
    std::optional<int64_t> AnimationErrorTicks;
    int64_t DriftTicks{0};
    //! SkippedBefore, UncertainStart, Torn, Late, Static.
    std::vector<std::string> Flags;
    std::optional<int64_t> IntendedDisplayTicks;
    //! OnDemandFrameTicks on demand.
    std::optional<int64_t> MarkerTargetTicks;
    //! In whole refreshes; empty on demand.
    std::optional<int64_t> TargetTicks;
    //! The frame time the application wants to run at; OnDemandFrameTicks on demand.
    std::optional<int64_t> MarkerPreferredTicks;
    //! The preferred frame time the late share is measured against, in whole refreshes; empty on demand.
    std::optional<int64_t> PreferredTicks;
    std::optional<int64_t> PacingErrorTicks;
    std::optional<int64_t> PredictionErrorTicks;
    std::optional<int64_t> LatenessTicks;
    std::optional<int64_t> LastSeenTicks;
    std::optional<int64_t> CpuStartTicks;
    std::optional<int64_t> CpuBusyTicks;
    std::optional<int64_t> FrameTimeTicks;
    std::optional<int64_t> CpuWaitTicks;
    //! The captures that showed an older frame out of order while this frame was the newest, in capture order.
    std::vector<OlderFrame> OlderFrames;
    std::optional<int64_t> MainMarkerFirstSeenTicks;
    std::optional<int64_t> ScanoutDelayTicks;
  };
}

#endif
