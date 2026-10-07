// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The frame rates a display can show every frame at (sdk/doc/pacer.md): a frame every so many refreshes, judged by
// the rule the pacer rounds a preferred frame rate with (PacerSettings::PreferredSwapIntervalAt), so the two never disagree.
#include <mb/framepacing/pacer/FrameRateStepUtil.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <algorithm>
#include <cassert>

namespace MB::FramePacing::Pacer::FrameRateStepUtil
{
  uint32_t StepCount(const RefreshPeriod refresh) noexcept
  {
    // The most refreshes that are not slower than the slowest frame time, with the slack: a step may be that much longer
    const int64_t refreshes =
      refresh.FloorRefreshes(NanosecondTimeSpan(SlowestFrameTime.Nanoseconds() + PacerSettings::FrameRateSlackAt(refresh).Nanoseconds()));
    return static_cast<uint32_t>(std::clamp(refreshes, int64_t{1}, int64_t{PacerSettings::MaxSwapInterval}));
  }

  FrameRateStep StepAt(const RefreshPeriod refresh, const uint32_t swapInterval) noexcept
  {
    assert(swapInterval >= 1u && swapInterval <= PacerSettings::MaxSwapInterval);
    const uint32_t refreshes = std::clamp(swapInterval, 1u, PacerSettings::MaxSwapInterval);
    return {refreshes, refresh.TimeFor(int64_t{refreshes}), refresh.RateMillihertz(refreshes)};
  }

  FrameRateStep StepFor(const RefreshPeriod refresh, const NanosecondTimeSpan preferredFrameTime) noexcept
  {
    // The pacer's own rounding, from settings made for the question
    PacerSettings settings(refresh);
    settings.SetPreferredFrameTime(std::clamp(preferredFrameTime, NanosecondTimeSpan(), PacerSettings::MaxPreferredFrameTime));
    return StepAt(refresh, settings.PreferredSwapIntervalAt(refresh));
  }

  FrameRateStep StepForRate(const RefreshPeriod refresh, const uint32_t numerator, const uint32_t denominator) noexcept
  {
    PacerSettings settings(refresh);
    if (numerator != 0 && denominator != 0)
    {
      settings.SetPreferredFrameRate(numerator, denominator);
    }
    return StepAt(refresh, settings.PreferredSwapIntervalAt(refresh));
  }

  bool IsStep(const RefreshPeriod refresh, const NanosecondTimeSpan frameTime) noexcept
  {
    // What the settings take of it, as StepFor does
    const NanosecondTimeSpan asked = std::clamp(frameTime, NanosecondTimeSpan(), PacerSettings::MaxPreferredFrameTime);
    const FrameRateStep step = StepFor(refresh, asked);
    // StepFor never gives a step that is faster than asked by more than the slack; one that is slower by more is another rate
    return step.SwapInterval <= StepCount(refresh) &&
           (step.FrameTime.Nanoseconds() - asked.Nanoseconds()) <= PacerSettings::FrameRateSlackAt(refresh).Nanoseconds();
  }

  bool IsStepRate(const RefreshPeriod refresh, const uint32_t numerator, const uint32_t denominator) noexcept
  {
    if (numerator == 0 || denominator == 0)
    {
      return false;
    }
    PacerSettings settings(refresh);
    settings.SetPreferredFrameRate(numerator, denominator);
    return IsStep(refresh, settings.PreferredFrameTime());
  }
}
