#ifndef MB_FRAMEPACING_PACER_PACERSETTINGS_HPP
#define MB_FRAMEPACING_PACER_PACERSETTINGS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/Ticks.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/SlowDownRule.hpp>
#include <algorithm>
#include <cassert>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! How a FramePacer paces. The display's refresh period is required; every other value has a default, the rule's being Swappy's
  //! (Android's frame pacing library, SwappyCommon.cpp, as the mb-framepacing-explained repository describes its rule): they are
  //! settings, not properties of frame pacing in general. Always valid: every setter asserts that its value is within its range; without
  //! asserts it clamps a value outside into it. The ranges keep the rule's arithmetic within 64 bits.
  class PacerSettings
  {
    RefreshPeriod m_refresh;
    uint32_t m_preferredSwapInterval{1};
    bool m_autoSwapInterval{true};
    SlowDownRule m_slowDown{SlowDownRule::LateCount};
    int64_t m_windowTicks{2 * TicksPerSecond};
    uint32_t m_slowDownLatePercent{10};
    int64_t m_frameMarginTicks{TicksPerMillisecond};
    int64_t m_slowestFrameTicks{50 * TicksPerMillisecond};
    int64_t m_presentLatencyTicks{0};
    uint32_t m_windowCapacity{0};

  public:
    static constexpr uint32_t MaxSwapInterval = 100;
    static constexpr int64_t MaxWindowTicks = 60 * TicksPerSecond;
    static constexpr uint32_t MaxSlowDownLatePercent = 100;
    static constexpr int64_t MaxFrameMarginTicks = TicksPerSecond;
    static constexpr int64_t MaxSlowestFrameTicks = 10 * TicksPerSecond;
    static constexpr int64_t MaxPresentLatencyTicks = TicksPerSecond;
    static constexpr uint32_t MinWindowCapacity = 2;
    static constexpr uint32_t MaxWindowCapacity = 1u << 20u;

    //! The display's refresh period, from its display mode (a DXGI output mode, Display.getRefreshRate, wl_output's mode).
    constexpr explicit PacerSettings(const RefreshPeriod refresh) noexcept
      : m_refresh(refresh)
    {
    }

    //! The display's refresh period.
    [[nodiscard]] constexpr RefreshPeriod Refresh() const noexcept
    {
      return m_refresh;
    }

    constexpr void SetRefresh(const RefreshPeriod refresh) noexcept
    {
      m_refresh = refresh;
    }

    //! The swap interval the application wants, in refreshes: 1 = every refresh (1 to MaxSwapInterval). The pacer never goes faster;
    //! PreferredSwapInterval refreshes are the marker's preferred frame time.
    [[nodiscard]] constexpr uint32_t PreferredSwapInterval() const noexcept
    {
      return m_preferredSwapInterval;
    }

    constexpr void SetPreferredSwapInterval(const uint32_t swapInterval) noexcept
    {
      assert(swapInterval >= 1u && swapInterval <= MaxSwapInterval);
      m_preferredSwapInterval = std::clamp(swapInterval, 1u, MaxSwapInterval);
    }

    //! Adapt the swap interval to the frames (the rule). false: always PreferredSwapInterval.
    [[nodiscard]] constexpr bool AutoSwapInterval() const noexcept
    {
      return m_autoSwapInterval;
    }

    constexpr void SetAutoSwapInterval(const bool autoSwapInterval) noexcept
    {
      m_autoSwapInterval = autoSwapInterval;
    }

    //! When the rule slows down (SlowDownRule).
    [[nodiscard]] constexpr SlowDownRule SlowDown() const noexcept
    {
      return m_slowDown;
    }

    constexpr void SetSlowDown(const SlowDownRule rule) noexcept
    {
      const bool known = rule == SlowDownRule::LateCount || rule == SlowDownRule::FullWindow;
      assert(known);
      m_slowDown = known ? rule : SlowDownRule::LateCount;
    }

    //! How long a stretch of frames the rule looks at (1 tick to MaxWindowTicks).
    [[nodiscard]] constexpr int64_t WindowTicks() const noexcept
    {
      return m_windowTicks;
    }

    constexpr void SetWindowTicks(const int64_t ticks) noexcept
    {
      assert(ticks >= 1 && ticks <= MaxWindowTicks);
      m_windowTicks = std::clamp(ticks, int64_t{1}, MaxWindowTicks);
    }

    //! The rule slows down when more than this share of the window's frames was late (percent, 0 to 100).
    [[nodiscard]] constexpr uint32_t SlowDownLatePercent() const noexcept
    {
      return m_slowDownLatePercent;
    }

    constexpr void SetSlowDownLatePercent(const uint32_t percent) noexcept
    {
      assert(percent <= MaxSlowDownLatePercent);
      m_slowDownLatePercent = std::min(percent, MaxSlowDownLatePercent);
    }

    //! Added to the frames' average work time before it is compared with swap intervals, and asked for as room to spare to speed up (0
    //! to MaxFrameMarginTicks).
    [[nodiscard]] constexpr int64_t FrameMarginTicks() const noexcept
    {
      return m_frameMarginTicks;
    }

    constexpr void SetFrameMarginTicks(const int64_t ticks) noexcept
    {
      assert(ticks >= 0 && ticks <= MaxFrameMarginTicks);
      m_frameMarginTicks = std::clamp(ticks, int64_t{0}, MaxFrameMarginTicks);
    }

    //! The rule slows down no further once the current swap interval is longer than this plus the margin (0 to MaxSlowestFrameTicks).
    [[nodiscard]] constexpr int64_t SlowestFrameTicks() const noexcept
    {
      return m_slowestFrameTicks;
    }

    constexpr void SetSlowestFrameTicks(const int64_t ticks) noexcept
    {
      assert(ticks >= 0 && ticks <= MaxSlowestFrameTicks);
      m_slowestFrameTicks = std::clamp(ticks, int64_t{0}, MaxSlowestFrameTicks);
    }

    //! Without display-time feedback: how long after Present a frame can be shown at the earliest (0 to MaxPresentLatencyTicks). 0: at the
    //! next refresh.
    [[nodiscard]] constexpr int64_t PresentLatencyTicks() const noexcept
    {
      return m_presentLatencyTicks;
    }

    constexpr void SetPresentLatencyTicks(const int64_t ticks) noexcept
    {
      assert(ticks >= 0 && ticks <= MaxPresentLatencyTicks);
      m_presentLatencyTicks = std::clamp(ticks, int64_t{0}, MaxPresentLatencyTicks);
    }

    //! Frames the rule's window can hold (MinWindowCapacity to MaxWindowCapacity); 0: enough for WindowTicks at the preferred swap
    //! interval, twice over (room for a faster display mode). Allocated once, when the pacer is made.
    [[nodiscard]] constexpr uint32_t WindowCapacity() const noexcept
    {
      return m_windowCapacity;
    }

    constexpr void SetWindowCapacity(const uint32_t frames) noexcept
    {
      assert(frames == 0u || (frames >= MinWindowCapacity && frames <= MaxWindowCapacity));
      m_windowCapacity = frames == 0u ? 0u : std::clamp(frames, MinWindowCapacity, MaxWindowCapacity);
    }

    constexpr bool operator==(const PacerSettings&) const noexcept = default;
  };
}

#endif
