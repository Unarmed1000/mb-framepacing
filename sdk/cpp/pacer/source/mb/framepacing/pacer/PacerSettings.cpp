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

  void PacerSettings::SetPreferredFrameTime(const TimeSpan frameTime) noexcept
  {
    assert(frameTime >= TimeSpan() && frameTime <= MaxPreferredFrameTime);
    m_preferredFrameTime = std::clamp(frameTime, TimeSpan(), MaxPreferredFrameTime);
  }

  void PacerSettings::SetPreferredFrameRate(const uint32_t numerator, const uint32_t denominator) noexcept
  {
    assert(numerator > 0 && denominator > 0);
    if (numerator == 0 || denominator == 0)
    {
      m_preferredFrameTime = TimeSpan();
      return;
    }
    // TicksPerSecond * denominator / numerator, rounded to the nearest tick, at least a tick
    const uint64_t dividend = static_cast<uint64_t>(TimeSpan::TicksPerSecond) * denominator;
    const uint64_t ticks =
      std::clamp((dividend + (numerator / 2u)) / numerator, uint64_t{1}, static_cast<uint64_t>(MaxPreferredFrameTime.Ticks()) + 1u);
    SetPreferredFrameTime(TimeSpan(static_cast<int64_t>(ticks)));
  }

  void PacerSettings::SetPreferredSwapInterval(const uint32_t swapInterval) noexcept
  {
    assert(swapInterval >= 1u && swapInterval <= MaxSwapInterval);
    m_preferredSwapInterval = std::clamp(swapInterval, 1u, MaxSwapInterval);
  }

  uint32_t PacerSettings::PreferredSwapIntervalAt(const RefreshPeriod refresh) const noexcept
  {
    const TimeSpan slack(refresh.ToTimeSpan().Ticks() / RefreshesPerSlack);
    const int64_t needed = refresh.RefreshesToFit(TimeSpan(m_preferredFrameTime.Ticks() - slack.Ticks()));
    return static_cast<uint32_t>(std::clamp(needed, int64_t{m_preferredSwapInterval}, int64_t{MaxSwapInterval}));
  }

  void PacerSettings::SetSlowDown(const SlowDownRule rule) noexcept
  {
    const bool known = rule == SlowDownRule::LateCount || rule == SlowDownRule::FullWindow;
    assert(known);
    m_slowDown = known ? rule : SlowDownRule::LateCount;
  }

  void PacerSettings::SetFrameWindowLength(const TimeSpan length) noexcept
  {
    assert(length >= MinFrameWindowLength && length <= MaxFrameWindowLength);
    m_frameWindowLength = std::clamp(length, MinFrameWindowLength, MaxFrameWindowLength);
  }

  void PacerSettings::SetSlowDownLatePercent(const uint32_t percent) noexcept
  {
    assert(percent <= MaxSlowDownLatePercent);
    m_slowDownLatePercent = std::min(percent, MaxSlowDownLatePercent);
  }

  TimeSpan PacerSettings::FrameMarginAt(const RefreshPeriod refresh) const noexcept
  {
    return m_frameMarginSet ? m_frameMargin : std::min(DefaultFrameMargin, TimeSpan(refresh.ToTimeSpan().Ticks() / DefaultFrameMarginDivisor));
  }

  void PacerSettings::SetFrameMargin(const TimeSpan margin) noexcept
  {
    assert(margin >= TimeSpan() && margin <= MaxFrameMargin);
    m_frameMargin = std::clamp(margin, TimeSpan(), MaxFrameMargin);
    m_frameMarginSet = true;
  }

  void PacerSettings::SetSlowestFrameTime(const TimeSpan frameTime) noexcept
  {
    assert(frameTime >= TimeSpan() && frameTime <= MaxSlowestFrameTime);
    m_slowestFrameTime = std::clamp(frameTime, TimeSpan(), MaxSlowestFrameTime);
  }

  void PacerSettings::SetAim(const PacerAim aim) noexcept
  {
    const bool known = aim == PacerAim::Smoothness || aim == PacerAim::LowLatency;
    assert(known);
    m_aim = known ? aim : PacerAim::Smoothness;
  }

  void PacerSettings::SetWaitingPresents(const uint32_t presents) noexcept
  {
    assert(presents >= 1 && presents <= MaxWaitingPresents);
    m_waitingPresents = std::clamp(presents, 1u, MaxWaitingPresents);
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

  void PacerSettings::SetStartupPauseDelay(const TimeSpan delay) noexcept
  {
    assert(delay >= TimeSpan() && delay <= MaxStartupPauseDelay);
    m_startupPauseDelay = std::clamp(delay, TimeSpan(), MaxStartupPauseDelay);
  }
}
