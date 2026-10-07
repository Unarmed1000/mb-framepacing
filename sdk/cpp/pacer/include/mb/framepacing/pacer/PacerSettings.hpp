#ifndef MB_FRAMEPACING_PACER_PACERSETTINGS_HPP
#define MB_FRAMEPACING_PACER_PACERSETTINGS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/rule/SlowDownRule.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). How a FramePacer paces. The display's refresh period is required; every other value
  //! has a default, the rule's being those of the adaptive swap interval rule the mb-framepacing-explained repository describes and
  //! simulates: they are settings, not properties of frame pacing in general. Always valid: every setter asserts that its value is within
  //! its range; without asserts it clamps a value outside into it.
  class PacerSettings
  {
    RefreshPeriod m_refresh;
    TimeSpan m_preferredFrameTime;
    uint32_t m_preferredSwapInterval{1};
    bool m_autoSwapInterval{true};
    SlowDownRule m_slowDown{SlowDownRule::LateCount};
    TimeSpan m_frameWindowLength{2 * TimeSpan::TicksPerSecond};
    uint32_t m_slowDownLatePercent{10};
    TimeSpan m_frameMargin{TimeSpan::TicksPerMillisecond};
    bool m_frameMarginSet{false};
    TimeSpan m_slowestFrameTime{50 * TimeSpan::TicksPerMillisecond};
    bool m_usePresentFeedback{false};
    uint32_t m_waitingPresents{2};
    TimeSpan m_presentWaitTimeout{250 * TimeSpan::TicksPerMillisecond};

  public:
    static constexpr uint32_t MaxSwapInterval = 100;
    static constexpr TimeSpan MaxPreferredFrameTime{10 * TimeSpan::TicksPerSecond};
    static constexpr TimeSpan MinFrameWindowLength{1};
    static constexpr TimeSpan MaxFrameWindowLength{60 * TimeSpan::TicksPerSecond};
    static constexpr uint32_t MaxSlowDownLatePercent = 100;
    static constexpr TimeSpan MaxFrameMargin{TimeSpan::TicksPerSecond};
    //! The default frame margin is this, and on a fast display less: the refresh period divided by DefaultFrameMarginDivisor
    static constexpr TimeSpan DefaultFrameMargin{TimeSpan::TicksPerMillisecond};
    static constexpr int64_t DefaultFrameMarginDivisor = 8;
    static constexpr TimeSpan MaxSlowestFrameTime{10 * TimeSpan::TicksPerSecond};
    static constexpr uint32_t MaxWaitingPresents = 8;
    static constexpr TimeSpan MinPresentWaitTimeout{TimeSpan::TicksPerMillisecond};
    static constexpr TimeSpan MaxPresentWaitTimeout{10 * TimeSpan::TicksPerSecond};

    //! The display's refresh period, from its display mode (a DXGI output mode, Display.getRefreshRate, wl_output's mode).
    explicit PacerSettings(const RefreshPeriod refresh) noexcept
      : m_refresh(refresh)
    {
    }

    //! The display's refresh period.
    [[nodiscard]] RefreshPeriod Refresh() const noexcept
    {
      return m_refresh;
    }

    void SetRefresh(const RefreshPeriod refresh) noexcept
    {
      m_refresh = refresh;
    }

    //! The frame time the application wants: its target frame rate, as the time of one frame (0 to MaxPreferredFrameTime). 0, the
    //! default: none, the display's rate. The pacer holds every frame for the whole refreshes that frame time needs on the display it
    //! runs on (PreferredSwapIntervalAt) and never runs faster.
    [[nodiscard]] TimeSpan PreferredFrameTime() const noexcept
    {
      return m_preferredFrameTime;
    }

    void SetPreferredFrameTime(TimeSpan frameTime) noexcept;

    //! The target frame rate as numerator / denominator frames a second: 30, or 30000 / 1001. From 0.1 fps; a numerator or denominator
    //! of 0 is outside (SetPreferredFrameTime({}) for none).
    void SetPreferredFrameRate(uint32_t numerator, uint32_t denominator = 1) noexcept;

    //! The swap interval the application wants, in refreshes: 1 = every refresh (1 to MaxSwapInterval): half rate on any display is 2.
    //! With a preferred frame time as well, the slower of the two counts.
    [[nodiscard]] uint32_t PreferredSwapInterval() const noexcept
    {
      return m_preferredSwapInterval;
    }

    void SetPreferredSwapInterval(uint32_t swapInterval) noexcept;

    //! The swap interval the application wants on a display with this refresh period: the pacer's fastest, and the marker's preferred
    //! frame time. The preferred frame time in whole refreshes, rounded up, with a twentieth of a refresh of slack (so 60 fps on a
    //! 59.94 Hz display is every refresh, and 60 fps on 144 Hz every third: never faster than asked), and at least the preferred swap
    //! interval; at most MaxSwapInterval. The rounding is the one the tools judge a target frame rate by.
    [[nodiscard]] uint32_t PreferredSwapIntervalAt(RefreshPeriod refresh) const noexcept;

    //! Adapt the swap interval to the frames (the rule). false: always the preferred one, a fixed frame rate.
    [[nodiscard]] bool AutoSwapInterval() const noexcept
    {
      return m_autoSwapInterval;
    }

    void SetAutoSwapInterval(const bool autoSwapInterval) noexcept
    {
      m_autoSwapInterval = autoSwapInterval;
    }

    //! When the rule slows down (SlowDownRule).
    [[nodiscard]] SlowDownRule SlowDown() const noexcept
    {
      return m_slowDown;
    }

    void SetSlowDown(SlowDownRule rule) noexcept;

    //! How long a stretch of frames the rule looks at: its frame window (MinFrameWindowLength to MaxFrameWindowLength). A frame that
    //! begins longer than this after the previous one starts again with an empty frame window.
    [[nodiscard]] TimeSpan FrameWindowLength() const noexcept
    {
      return m_frameWindowLength;
    }

    void SetFrameWindowLength(TimeSpan length) noexcept;

    //! The rule slows down when more than this share of the frame window's frames was late (percent, 0 to 100).
    [[nodiscard]] uint32_t SlowDownLatePercent() const noexcept
    {
      return m_slowDownLatePercent;
    }

    void SetSlowDownLatePercent(uint32_t percent) noexcept;

    //! Added to the frames' average work time before it is compared with swap intervals, and asked for as room to spare to speed up (0
    //! to MaxFrameMargin): on the settings' own display (FrameMarginAt(Refresh())).
    [[nodiscard]] TimeSpan FrameMargin() const noexcept
    {
      return FrameMarginAt(m_refresh);
    }

    //! The frame margin on a display with this refresh period: the one that was set, on any display. By default the smaller of
    //! DefaultFrameMargin (1 ms) and an eighth of the refresh period (above 125 Hz): the rule speeds up only when the frames' work
    //! and twice the margin fit a refresh, and 1 ms is half a refresh at 500 Hz.
    [[nodiscard]] TimeSpan FrameMarginAt(RefreshPeriod refresh) const noexcept;

    //! Set the frame margin: it then is this on every display.
    void SetFrameMargin(TimeSpan margin) noexcept;

    //! The rule slows down no further once the current swap interval is longer than this plus the margin (0 to MaxSlowestFrameTime).
    [[nodiscard]] TimeSpan SlowestFrameTime() const noexcept
    {
      return m_slowestFrameTime;
    }

    void SetSlowestFrameTime(TimeSpan frameTime) noexcept;

    //! Take the display times the application reports (FramePacer::AddPresentFeedback): for a platform with present feedback, on a
    //! display with a fixed refresh rate. The pacer paces the same with it (by the frame starts and the frames' work); the display
    //! times are counted (FramePacer::FeedbackState) and the intended display time is counted from them, unknown while there is
    //! none. false, the default: feedback is not looked at.
    [[nodiscard]] bool UsePresentFeedback() const noexcept
    {
      return m_usePresentFeedback;
    }

    void SetUsePresentFeedback(const bool usePresentFeedback) noexcept
    {
      m_usePresentFeedback = usePresentFeedback;
    }

    //! For a pacer that waits for a present (QueueTier::WaitForPresent): the presents that may be waiting to be shown while a
    //! frame is made (1 to MaxWaitingPresents). Before a frame the pacer asks for a wait until the present that many back was
    //! shown. 1: no present waits while the next frame is made, the lowest latency, and no slack: work that does not fit in a
    //! refresh beside the wait halves the frame rate. 2, the default: one may wait, a refresh more of latency, and the frame
    //! rate holds.
    [[nodiscard]] uint32_t WaitingPresents() const noexcept
    {
      return m_waitingPresents;
    }

    void SetWaitingPresents(uint32_t presents) noexcept;

    //! The longest a wait for a present may take (MinPresentWaitTimeout to MaxPresentWaitTimeout): a present of a window that
    //! is not shown may never be shown. 250 ms by default.
    [[nodiscard]] TimeSpan PresentWaitTimeout() const noexcept
    {
      return m_presentWaitTimeout;
    }

    void SetPresentWaitTimeout(TimeSpan timeout) noexcept;

    constexpr bool operator==(const PacerSettings&) const noexcept = default;
  };
}

#endif
