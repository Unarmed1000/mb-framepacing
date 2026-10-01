// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The swap interval rule of sdk/doc/pacer.md: the adaptive swap interval rule (as mb-framepacing-explained simulates it in
// tools/frame_pacing_video/adaptive_rate.py) and the late count fix. Integer arithmetic only, so C++ and C# decide exactly alike.
#include <mb/framepacing/pacer/rule/SwapIntervalRule.hpp>
#include <algorithm>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    constexpr int64_t OneTickQ32 = RefreshPeriod::OneTickQ32;
    //! A remainder of at most 500 ns does not need another refresh
    constexpr int64_t RemainderMarginTicks = 5;
    std::size_t Capacity(const PacerSettings& settings) noexcept
    {
      if (settings.WindowCapacity() > 0)
      {
        return settings.WindowCapacity();
      }
      // The frames of a window at the preferred swap interval, and the frame on either side; twice that for a faster display mode
      const int64_t frames = (settings.WindowTicks() / (settings.Refresh().Ticks() * settings.PreferredSwapInterval())) + 2;
      return static_cast<std::size_t>(std::min(frames * 2, int64_t{PacerSettings::MaxWindowCapacity}));
    }

    //! The refreshes a frame of frameTicks needs: at least 1, and one more for a remainder beyond 500 ns;
    //! at most PacerSettings::MaxSwapInterval. frameTicks is at most a window and two margins, so frameTicks * 2^32 fits 64 bits.
    uint32_t NeededSwapInterval(const int64_t frameTicks, const RefreshPeriod period) noexcept
    {
      const int64_t frameQ32 = frameTicks * OneTickQ32;
      if (frameQ32 < period.TicksQ32())
      {
        return 1;
      }
      const int64_t whole = frameQ32 / period.TicksQ32();
      const int64_t rest = frameQ32 - (whole * period.TicksQ32());
      const int64_t needed = whole + (rest > RemainderMarginTicks * OneTickQ32 ? 1 : 0);
      return static_cast<uint32_t>(std::min(needed, int64_t{PacerSettings::MaxSwapInterval}));
    }

    //! 100 * late / frames rounded to a whole percent, a half to the even one (as Python's round in the simulation).
    int64_t LatePercent(const uint32_t late, const uint32_t frames) noexcept
    {
      const int64_t numerator = int64_t{100} * late;
      const int64_t count = frames;
      const int64_t whole = numerator / count;
      const int64_t twiceRest = 2 * (numerator % count);
      return whole + ((twiceRest > count || (twiceRest == count && whole % 2 == 1)) ? 1 : 0);
    }

    //! The frames a full window holds at swapInterval: windowTicks / (swapInterval * period), rounded to the nearest.
    int64_t FullWindowFrames(const int64_t windowTicks, const uint32_t swapInterval, const RefreshPeriod period) noexcept
    {
      const int64_t numerator = windowTicks * OneTickQ32;
      const int64_t denominator = static_cast<int64_t>(swapInterval) * period.TicksQ32();
      return (numerator + (denominator / 2)) / denominator;
    }
  }

  SwapIntervalRule::SwapIntervalRule(const PacerSettings& settings)
    : m_settings(settings)
    , m_entries(Capacity(settings))
    , m_swapInterval(settings.PreferredSwapInterval())
  {
  }

  SwapIntervalChange SwapIntervalRule::AddFrame(const int64_t displayTicks, const int64_t workTicks, const bool late,
                                                const RefreshPeriod period) noexcept
  {
    // The frames of the last WindowTicks: the oldest go once the second oldest is more than WindowTicks older than this one (so the
    // window keeps one frame beyond it, as the reference simulation's does), and when the window is full
    if (m_count == m_entries.size())
    {
      PopFront();
    }
    m_entries[(m_first + m_count) % m_entries.size()] = Entry{displayTicks, std::clamp(workTicks, int64_t{0}, m_settings.WindowTicks()), late};
    ++m_count;
    m_workSum += At(m_count - 1u).WorkTicks;
    m_lateCount += late ? 1u : 0u;
    while (m_count >= 2u && displayTicks - At(1).DisplayTicks > m_settings.WindowTicks())
    {
      PopFront();
    }

    if (!m_settings.AutoSwapInterval())
    {
      return SwapIntervalChange::None;
    }
    const auto frames = static_cast<uint32_t>(m_count);
    const bool full = displayTicks - At(0).DisplayTicks > m_settings.WindowTicks();
    const int64_t frameTicks = (m_workSum / frames) + m_settings.FrameMarginTicks();

    const bool mayGoSlower =
      m_swapInterval < PacerSettings::MaxSwapInterval &&
      static_cast<int64_t>(m_swapInterval) * period.TicksQ32() <= (m_settings.SlowestFrameTicks() + m_settings.FrameMarginTicks()) * OneTickQ32;
    const bool slowerByShare = full && LatePercent(m_lateCount, frames) > m_settings.SlowDownLatePercent();
    // The fix: as many late frames as would make a full window's share late, without waiting for the window to fill again
    const bool slowerByCount =
      m_settings.SlowDown() == SlowDownRule::LateCount &&
      int64_t{100} * m_lateCount > m_settings.SlowDownLatePercent() * FullWindowFrames(m_settings.WindowTicks(), m_swapInterval, period);

    SwapIntervalChange change = SwapIntervalChange::None;
    uint32_t swapInterval = m_swapInterval;
    if (mayGoSlower && (slowerByShare || slowerByCount))
    {
      swapInterval = std::max(m_swapInterval + 1u, NeededSwapInterval(frameTicks, period));
      change = SwapIntervalChange::Slower;
    }
    else if (full && m_lateCount == 0 && m_swapInterval > m_settings.PreferredSwapInterval() &&
             (frameTicks + m_settings.FrameMarginTicks()) * OneTickQ32 < static_cast<int64_t>(m_swapInterval - 1u) * period.TicksQ32())
    {
      swapInterval = std::max(m_settings.PreferredSwapInterval(), NeededSwapInterval(frameTicks, period));
      change = SwapIntervalChange::Faster;
    }
    if (change != SwapIntervalChange::None)
    {
      m_swapInterval = swapInterval;
      Clear();
    }
    return change;
  }

  void SwapIntervalRule::Reset(const uint32_t swapInterval) noexcept
  {
    m_swapInterval = std::clamp(swapInterval, m_settings.PreferredSwapInterval(), PacerSettings::MaxSwapInterval);
    Clear();
  }

  void SwapIntervalRule::Clear() noexcept
  {
    m_first = 0;
    m_count = 0;
    m_workSum = 0;
    m_lateCount = 0;
  }

  WindowState SwapIntervalRule::Window() const noexcept
  {
    if (m_count == 0)
    {
      return {};
    }
    const int64_t span = At(m_count - 1u).DisplayTicks - At(0).DisplayTicks;
    return {static_cast<uint32_t>(m_count), m_lateCount, m_workSum / static_cast<int64_t>(m_count), span, span > m_settings.WindowTicks()};
  }

  const SwapIntervalRule::Entry& SwapIntervalRule::At(const std::size_t index) const noexcept
  {
    return m_entries[(m_first + index) % m_entries.size()];
  }

  void SwapIntervalRule::PopFront() noexcept
  {
    const Entry& oldest = At(0);
    m_workSum -= oldest.WorkTicks;
    m_lateCount -= oldest.Late ? 1u : 0u;
    m_first = (m_first + 1u) % m_entries.size();
    --m_count;
  }
}
