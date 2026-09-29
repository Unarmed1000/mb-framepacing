#ifndef MB_FRAMEPACINGDATA_SUMMARYSTATISTICS_HPP
#define MB_FRAMEPACINGDATA_SUMMARYSTATISTICS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacingdata/ValueStatistics.hpp>
#include <cstdint>
#include <optional>

namespace MB::FramePacingData
{
  //! A run's statistics: the display and animation time steps and the animation error of the frames with one, every frame's drift and time
  //! on screen, the frame rates, and the CPU side from the markers (CPU busy, frametime, CPU wait as PresentMon names them; Count 0 when the
  //! markers carry none).
  struct SummaryStatistics
  {
    ValueStatistics DisplayDeltaMs;
    ValueStatistics AnimationDeltaMs;
    ValueStatistics AnimationErrorMs;
    ValueStatistics AbsoluteAnimationErrorMs;
    ValueStatistics DriftMs;
    ValueStatistics OnScreenMs;
    int64_t FramesWithAnimationError{0};
    double ErrorPerFrameMs{0.0};
    double PercentError{0.0};
    double AverageFps{0.0};
    std::optional<double> OnePercentLowFps;
    std::optional<double> PointOnePercentLowFps;
    ValueStatistics CpuBusyMs;
    ValueStatistics FrameTimeMs;
    ValueStatistics CpuWaitMs;
  };
}

#endif
