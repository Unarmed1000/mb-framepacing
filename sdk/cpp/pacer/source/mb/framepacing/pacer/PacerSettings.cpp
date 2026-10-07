// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The pacer's settings (sdk/doc/pacer.md): every setter asserts its range and, without asserts, clamps into it.
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <algorithm>
#include <cassert>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    //! The slack of a target frame rate: one refresh in this many
    constexpr int64_t RefreshesPerSlack = 20;
  }

  void PacerSettings::SetPreferredFrameTime(const NanosecondTimeSpan frameTime) noexcept
  {
    assert(frameTime >= NanosecondTimeSpan() && frameTime <= MaxPreferredFrameTime);
    m_preferredFrameTime = std::clamp(frameTime, NanosecondTimeSpan(), MaxPreferredFrameTime);
  }

  void PacerSettings::SetPreferredFrameRate(const uint32_t numerator, const uint32_t denominator) noexcept
  {
    assert(numerator > 0 && denominator > 0);
    if (numerator == 0 || denominator == 0)
    {
      m_preferredFrameTime = NanosecondTimeSpan();
      return;
    }
    // NanosecondsPerSecond * denominator / numerator, rounded to the nearest nanosecond, at least a nanosecond
    const uint64_t dividend = static_cast<uint64_t>(NanosecondTimeSpan::NanosecondsPerSecond) * denominator;
    const uint64_t nanoseconds =
      std::clamp((dividend + (numerator / 2u)) / numerator, uint64_t{1}, static_cast<uint64_t>(MaxPreferredFrameTime.Nanoseconds()) + 1u);
    SetPreferredFrameTime(NanosecondTimeSpan(static_cast<int64_t>(nanoseconds)));
  }

  void PacerSettings::SetPreferredSwapInterval(const uint32_t swapInterval) noexcept
  {
    assert(swapInterval >= 1u && swapInterval <= MaxSwapInterval);
    m_preferredSwapInterval = std::clamp(swapInterval, 1u, MaxSwapInterval);
  }

  uint32_t PacerSettings::PreferredSwapIntervalAt(const RefreshPeriod refresh) const noexcept
  {
    const int64_t needed = refresh.RefreshesToFit(NanosecondTimeSpan(m_preferredFrameTime.Nanoseconds() - FrameRateSlackAt(refresh).Nanoseconds()));
    return static_cast<uint32_t>(std::clamp(needed, int64_t{m_preferredSwapInterval}, int64_t{MaxSwapInterval}));
  }

  NanosecondTimeSpan PacerSettings::FrameRateSlackAt(const RefreshPeriod refresh) noexcept
  {
    return NanosecondTimeSpan(refresh.ToNanosecondTimeSpan().Nanoseconds() / RefreshesPerSlack);
  }

  void PacerSettings::SetSlowDown(const SlowDownRule rule) noexcept
  {
    const bool known = rule == SlowDownRule::LateCount || rule == SlowDownRule::FullWindow;
    assert(known);
    m_slowDown = known ? rule : SlowDownRule::LateCount;
  }

  void PacerSettings::SetFrameWindowLength(const NanosecondTimeSpan length) noexcept
  {
    assert(length >= MinFrameWindowLength && length <= MaxFrameWindowLength);
    m_frameWindowLength = std::clamp(length, MinFrameWindowLength, MaxFrameWindowLength);
  }

  void PacerSettings::SetSlowDownLatePercent(const uint32_t percent) noexcept
  {
    assert(percent <= MaxSlowDownLatePercent);
    m_slowDownLatePercent = std::min(percent, MaxSlowDownLatePercent);
  }

  NanosecondTimeSpan PacerSettings::FrameMarginAt(const RefreshPeriod refresh) const noexcept
  {
    return m_frameMarginSet
             ? m_frameMargin
             : std::min(DefaultFrameMargin, NanosecondTimeSpan(refresh.ToNanosecondTimeSpan().Nanoseconds() / DefaultFrameMarginDivisor));
  }

  void PacerSettings::SetFrameMargin(const NanosecondTimeSpan margin) noexcept
  {
    assert(margin >= NanosecondTimeSpan() && margin <= MaxFrameMargin);
    m_frameMargin = std::clamp(margin, NanosecondTimeSpan(), MaxFrameMargin);
    m_frameMarginSet = true;
  }

  void PacerSettings::SetSlowestFrameTime(const NanosecondTimeSpan frameTime) noexcept
  {
    assert(frameTime >= NanosecondTimeSpan() && frameTime <= MaxSlowestFrameTime);
    m_slowestFrameTime = std::clamp(frameTime, NanosecondTimeSpan(), MaxSlowestFrameTime);
  }

  void PacerSettings::SetAim(const PacerAim aim) noexcept
  {
    const bool known = aim == PacerAim::Smoothness || aim == PacerAim::LowLatency;
    assert(known);
    m_aim = known ? aim : PacerAim::Smoothness;
  }

  void PacerSettings::SetWaitingPresents(const uint32_t presents) noexcept
  {
    assert(presents <= MaxWaitingPresents);
    m_waitingPresents = std::min(presents, MaxWaitingPresents);
  }

  void PacerSettings::SetSwapChainImages(const uint32_t images) noexcept
  {
    assert(images <= MaxSwapChainImages);
    m_swapChainImages = std::min(images, MaxSwapChainImages);
  }

  uint32_t PacerSettings::ReserveFrames() const noexcept
  {
    const uint32_t asked = WaitingPresents() - 1u;
    if (m_swapChainImages == 0)
    {
      return asked;
    }
    // One image is on screen and one is drawn into: the rest can wait
    const uint32_t held = m_swapChainImages > 2u ? m_swapChainImages - 2u : 0u;
    return m_systemHoldsLoop ? held : std::min(asked, held);
  }

  void PacerSettings::SetPresentWaitSwapIntervals(const uint32_t swapIntervals) noexcept
  {
    assert(swapIntervals >= 1 && swapIntervals <= MaxPresentWaitSwapIntervals);
    m_presentWaitSwapIntervals = std::clamp(swapIntervals, 1u, MaxPresentWaitSwapIntervals);
  }

  void PacerSettings::SetMaxFramesInFlight(const uint32_t frames) noexcept
  {
    assert(frames >= 1 && frames <= MaxMaxFramesInFlight);
    m_maxFramesInFlight = std::clamp(frames, 1u, MaxMaxFramesInFlight);
  }

  void PacerSettings::SetStartupPauseRefreshes(const uint32_t refreshes) noexcept
  {
    assert(refreshes <= MaxStartupPauseRefreshes);
    m_startupPauseRefreshes = std::min(refreshes, MaxStartupPauseRefreshes);
  }

  void PacerSettings::SetReadyPlacePercent(const uint32_t percent) noexcept
  {
    assert(percent <= MaxReadyPlacePercent);
    m_readyPlacePercent = std::min(percent, MaxReadyPlacePercent);
  }

  void PacerSettings::SetStartupPauseDelay(const NanosecondTimeSpan delay) noexcept
  {
    assert(delay >= NanosecondTimeSpan() && delay <= MaxStartupPauseDelay);
    m_startupPauseDelay = std::clamp(delay, NanosecondTimeSpan(), MaxStartupPauseDelay);
  }
}
