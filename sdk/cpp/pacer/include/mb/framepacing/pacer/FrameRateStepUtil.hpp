#ifndef MB_FRAMEPACING_PACER_FRAMERATESTEPUTIL_HPP
#define MB_FRAMEPACING_PACER_FRAMERATESTEPUTIL_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/FrameRateStep.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <cstdint>

//! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). The frame rates a display with a fixed refresh rate can show every frame at
//! (FrameRateStep), for an application that lets its user choose one: a display's steps to list in a menu, the step a frame rate
//! becomes on it, and whether a rate is one of its steps. Functions of the refresh period alone: they change nothing and need no
//! pacer.
//!
//! A display's steps are a frame every 1, 2, 3, ... refreshes, fastest first, down to SlowestFrameTime (20 frames a second): on
//! 240 Hz that is 240, 120, 80, 60, 48, 40, 34.29, 30, 26.67, 24, 21.82 and 20.
namespace MB::FramePacing::Pacer::FrameRateStepUtil
{
  //! The slowest step is the last one that is not slower than this: 50 ms a frame, 20 frames a second. Judged with the slack the
  //! pacer judges a frame rate with (a twentieth of a refresh), so every third refresh of a 59.94 Hz display, 19.98 frames a second,
  //! is its last step.
  inline constexpr NanosecondTimeSpan SlowestFrameTime{50 * NanosecondTimeSpan::NanosecondsPerMillisecond};

  //! How many steps the display has: its steps are the swap intervals 1 to this. At least 1 (a display slower than 20 Hz has its
  //! own rate and nothing else), at most PacerSettings::MaxSwapInterval.
  [[nodiscard]] uint32_t StepCount(RefreshPeriod refresh) noexcept;

  //! The step of a swap interval: a frame every so many refreshes (1 to PacerSettings::MaxSwapInterval; asserted, then clamped).
  //! For a menu: StepAt(refresh, 1) to StepAt(refresh, StepCount(refresh)).
  [[nodiscard]] FrameRateStep StepAt(RefreshPeriod refresh, uint32_t swapInterval) noexcept;

  //! The step the pacer paces a preferred frame time at on this display (PacerSettings::PreferredSwapIntervalAt: the whole
  //! refreshes it needs, rounded up, with a twentieth of a refresh of slack: never faster than asked). No frame time (zero) is the
  //! display's own rate. It is the pacer's answer whatever was asked: a frame time slower than SlowestFrameTime gives a step
  //! beyond the display's StepCount.
  [[nodiscard]] FrameRateStep StepFor(RefreshPeriod refresh, NanosecondTimeSpan preferredFrameTime) noexcept;

  //! The same for a frame rate of numerator / denominator frames a second: 30, or 30000 / 1001. A numerator or a denominator of 0
  //! is no rate: the display's own.
  [[nodiscard]] FrameRateStep StepForRate(RefreshPeriod refresh, uint32_t numerator, uint32_t denominator = 1) noexcept;

  //! Whether a frame time is one of the display's steps: within the slack of a whole number of refreshes, and not slower than the
  //! slowest step. 20 ms is none at 60 Hz (the pacer makes it 30 frames a second); 16.67 ms is one at 59.94 Hz.
  [[nodiscard]] bool IsStep(RefreshPeriod refresh, NanosecondTimeSpan frameTime) noexcept;

  //! The same for a frame rate of numerator / denominator frames a second. A numerator or a denominator of 0 is no step.
  [[nodiscard]] bool IsStepRate(RefreshPeriod refresh, uint32_t numerator, uint32_t denominator = 1) noexcept;
}

#endif
