#ifndef MB_FRAMEPACING_PACER_PACERSETTINGS_HPP
#define MB_FRAMEPACING_PACER_PACERSETTINGS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerAim.hpp>
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
    NanosecondTimeSpan m_preferredFrameTime;
    uint32_t m_preferredSwapInterval{1};
    bool m_autoSwapInterval{true};
    SlowDownRule m_slowDown{SlowDownRule::LateCount};
    NanosecondTimeSpan m_frameWindowLength{2 * NanosecondTimeSpan::NanosecondsPerSecond};
    uint32_t m_slowDownLatePercent{10};
    NanosecondTimeSpan m_frameMargin{NanosecondTimeSpan::NanosecondsPerMillisecond};
    bool m_frameMarginSet{false};
    NanosecondTimeSpan m_slowestFrameTime{50 * NanosecondTimeSpan::NanosecondsPerMillisecond};
    bool m_usePresentFeedback{false};
    PacerAim m_aim{PacerAim::Smoothness};
    uint32_t m_waitingPresents{2};
    uint32_t m_presentWaitSwapIntervals{4};
    uint32_t m_maxFramesInFlight{1};
    uint32_t m_startupPauseRefreshes{4};
    NanosecondTimeSpan m_startupPauseDelay{500 * NanosecondTimeSpan::NanosecondsPerMillisecond};
    uint32_t m_readyPlacePercent{50};
    uint32_t m_swapChainImages{0};
    bool m_systemHoldsLoop{false};

  public:
    static constexpr uint32_t MaxSwapInterval = 100;
    static constexpr NanosecondTimeSpan MaxPreferredFrameTime{10 * NanosecondTimeSpan::NanosecondsPerSecond};
    static constexpr NanosecondTimeSpan MinFrameWindowLength{1};
    static constexpr NanosecondTimeSpan MaxFrameWindowLength{60 * NanosecondTimeSpan::NanosecondsPerSecond};
    static constexpr uint32_t MaxSlowDownLatePercent = 100;
    static constexpr NanosecondTimeSpan MaxFrameMargin{NanosecondTimeSpan::NanosecondsPerSecond};
    //! The default frame margin is this, and on a fast display less: the refresh period divided by DefaultFrameMarginDivisor
    static constexpr NanosecondTimeSpan DefaultFrameMargin{NanosecondTimeSpan::NanosecondsPerMillisecond};
    static constexpr int64_t DefaultFrameMarginDivisor = 8;
    static constexpr NanosecondTimeSpan MaxSlowestFrameTime{10 * NanosecondTimeSpan::NanosecondsPerSecond};
    static constexpr uint32_t MaxWaitingPresents = 8;
    static constexpr uint32_t MaxSwapChainImages = 64;
    static constexpr uint32_t MaxPresentWaitSwapIntervals = 64;
    static constexpr uint32_t MaxMaxFramesInFlight = 8;
    static constexpr uint32_t MaxStartupPauseRefreshes = 64;
    static constexpr NanosecondTimeSpan MaxStartupPauseDelay{10 * NanosecondTimeSpan::NanosecondsPerSecond};
    static constexpr uint32_t MaxReadyPlacePercent = 100;

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
    [[nodiscard]] NanosecondTimeSpan PreferredFrameTime() const noexcept
    {
      return m_preferredFrameTime;
    }

    void SetPreferredFrameTime(NanosecondTimeSpan frameTime) noexcept;

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

    //! The slack a frame rate is judged with on a display with this refresh period: a twentieth of a refresh. A frame time that
    //! is this much longer than a whole number of refreshes still is that many (PreferredSwapIntervalAt, FrameRateStepUtil).
    [[nodiscard]] static NanosecondTimeSpan FrameRateSlackAt(RefreshPeriod refresh) noexcept;

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
    [[nodiscard]] NanosecondTimeSpan FrameWindowLength() const noexcept
    {
      return m_frameWindowLength;
    }

    void SetFrameWindowLength(NanosecondTimeSpan length) noexcept;

    //! The rule slows down when more than this share of the frame window's frames was late (percent, 0 to 100).
    [[nodiscard]] uint32_t SlowDownLatePercent() const noexcept
    {
      return m_slowDownLatePercent;
    }

    void SetSlowDownLatePercent(uint32_t percent) noexcept;

    //! Added to the frames' average work time before it is compared with swap intervals, and asked for as room to spare to speed up (0
    //! to MaxFrameMargin): on the settings' own display (FrameMarginAt(Refresh())).
    [[nodiscard]] NanosecondTimeSpan FrameMargin() const noexcept
    {
      return FrameMarginAt(m_refresh);
    }

    //! The frame margin on a display with this refresh period: the one that was set, on any display. By default the smaller of
    //! DefaultFrameMargin (1 ms) and an eighth of the refresh period (above 125 Hz): the rule speeds up only when the frames' work
    //! and twice the margin fit a refresh, and 1 ms is half a refresh at 500 Hz.
    [[nodiscard]] NanosecondTimeSpan FrameMarginAt(RefreshPeriod refresh) const noexcept;

    //! Set the frame margin: it then is this on every display.
    void SetFrameMargin(NanosecondTimeSpan margin) noexcept;

    //! The rule slows down no further once the current swap interval is longer than this plus the margin (0 to MaxSlowestFrameTime).
    [[nodiscard]] NanosecondTimeSpan SlowestFrameTime() const noexcept
    {
      return m_slowestFrameTime;
    }

    void SetSlowestFrameTime(NanosecondTimeSpan frameTime) noexcept;

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

    //! What the tier pacers optimize for (PacerAim): PacerAim::Smoothness, the default, or PacerAim::LowLatency. FramePacer has
    //! no aims and does not read it.
    [[nodiscard]] PacerAim Aim() const noexcept
    {
      return m_aim;
    }

    void SetAim(PacerAim aim) noexcept;

    //! The presents that may be waiting to be shown while a frame is made, the frame itself counted (1 to MaxWaitingPresents;
    //! 2 by default, so one may wait).
    //! A pacer that waits for a present (QueueTier::WaitForPresent) asks before a frame for a wait until the present that
    //! many back was shown. 1: no present waits while the next frame is made, the lowest latency, and no slack: work that
    //! does not fit in a refresh beside the wait halves the frame rate. 2: one may wait, a refresh more of latency, and the
    //! frame rate holds.
    //! With PacerAim::Smoothness it is also the reserve a tier pacer keeps at one refresh per frame: that many less one frames
    //! are made ahead of the display and wait to be shown, and a frame that ran long is forgiven that many steps.
    [[nodiscard]] uint32_t WaitingPresents() const noexcept
    {
      return m_waitingPresents;
    }

    void SetWaitingPresents(uint32_t presents) noexcept;

    //! The images the application's swap chain has (0 to MaxSwapChainImages). 0, the default: not known. One of them is on
    //! screen and one is drawn into, so the frames that can wait to be shown are that many less two: a tier pacer keeps no
    //! larger reserve (ReserveFrames).
    [[nodiscard]] uint32_t SwapChainImages() const noexcept
    {
      return m_swapChainImages;
    }

    void SetSwapChainImages(uint32_t images) noexcept;

    //! True when the system holds the frame loop while its queue of frames is full (a present or a wait for an image that
    //! waits for the display), and the application reports those waits (SystemWaitReport). Off by default. With
    //! PacerAim::Smoothness a tier pacer then lets the system pace the loop on purpose: the reserve is what the swap chain
    //! holds, a frame the system held is not late, and the pacer's own start time only keeps the loop from running away
    //! where the system does not hold it after all.
    [[nodiscard]] bool SystemHoldsLoop() const noexcept
    {
      return m_systemHoldsLoop;
    }

    void SetSystemHoldsLoop(const bool systemHoldsLoop) noexcept
    {
      m_systemHoldsLoop = systemHoldsLoop;
    }

    //! The frames a tier pacer makes ahead of the display with PacerAim::Smoothness at one refresh per frame: WaitingPresents
    //! less one, and no more than the swap chain can hold when its images are known. Where the system holds the loop
    //! (SystemHoldsLoop) and the images are known it is what the swap chain holds, whatever WaitingPresents says.
    [[nodiscard]] uint32_t ReserveFrames() const noexcept;

    //! The longest a wait for a present may take, in swap intervals of the frame that waits (1 to MaxPresentWaitSwapIntervals;
    //! 4 by default): some presents are never shown (the first ones of a new window, those of a window that is hidden), and
    //! the loop stands for this long when it waits for one. Counted in the frame's own time, so a slow loop is given as
    //! many of its frames as a fast one.
    [[nodiscard]] uint32_t PresentWaitSwapIntervals() const noexcept
    {
      return m_presentWaitSwapIntervals;
    }

    void SetPresentWaitSwapIntervals(uint32_t swapIntervals) noexcept;

    //! The frames the application lets be in flight at once (1 to MaxMaxFramesInFlight; 1 by default). 1: a frame starts
    //! when the GPU is done with the one before it, so the CPU's and the GPU's work on a frame come one after the other and
    //! a frame needs the two added. 2 or more: the CPU works on a frame while the GPU works on the one before it, and a
    //! frame needs the longer of the two. It is the application's word for what the frames' times can not always show (at
    //! a longer swap interval nothing overlaps); where GPU work reports with an end time show the two side by side, that
    //! counts too. Without GPU work reports it is not used.
    [[nodiscard]] uint32_t MaxFramesInFlight() const noexcept
    {
      return m_maxFramesInFlight;
    }

    void SetMaxFramesInFlight(uint32_t frames) noexcept;

    //! For a pacer that can neither wait for a present nor see what the display shows (QueueTier::PeriodOnly): the length of
    //! the one pause it makes after start-up, in refreshes (0 to MaxStartupPauseRefreshes; 0: no pause; 4 by default). The
    //! first presents of a new swap chain can take longer to reach the display than the later ones, and the frames that
    //! pile up behind them then wait for as long as the loop runs at one refresh per frame. The pause lets the display
    //! take them. A guess, as that pacer does not learn whether any frame waits: the default is what emptied the queue on
    //! the one system measured.
    [[nodiscard]] uint32_t StartupPauseRefreshes() const noexcept
    {
      return m_startupPauseRefreshes;
    }

    void SetStartupPauseRefreshes(uint32_t refreshes) noexcept;

    //! How long after the first frame of a start (or of a new swap chain) that pause is made (0 to MaxStartupPauseDelay;
    //! half a second by default): after the presents that pile up were made. A guess as well.
    [[nodiscard]] NanosecondTimeSpan StartupPauseDelay() const noexcept
    {
      return m_startupPauseDelay;
    }

    void SetStartupPauseDelay(NanosecondTimeSpan delay) noexcept;

    //! For a pacer that knows where the display's refreshes are (HoldTier::VBlank): where in a refresh a frame is to be ready,
    //! in percent of the refresh period after its vertical blank (0 to MaxReadyPlacePercent; 50 by default). Ready is
    //! presented, and with GPU work reports the GPU done with it. A frame that is ready there is shown at the next vertical
    //! blank. The middle is as far from either vertical blank as a frame can be, so it is the default without knowing a
    //! system: earlier leaves more room for a frame that runs long, later shows a newer frame.
    [[nodiscard]] uint32_t ReadyPlacePercent() const noexcept
    {
      return m_readyPlacePercent;
    }

    void SetReadyPlacePercent(uint32_t percent) noexcept;

    constexpr bool operator==(const PacerSettings&) const noexcept = default;
  };
}

#endif
