#ifndef MB_FRAMEPACING_PACER_RULE_SWAPINTERVALRULE_HPP
#define MB_FRAMEPACING_PACER_RULE_SWAPINTERVALRULE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). The adaptive swap interval rule: it keeps the frames of the last FrameWindowLength
  //! since its last change and decides after every frame whether to run slower or faster. FramePacer uses it; an application with its own
  //! frame loop can use it alone. The window is allocated when the rule is made; AddFrame and the rest never allocate, except SetSettings
  //! with settings that need a larger window.
  class SwapIntervalRule
  {
    struct Entry
    {
      NanosecondTimeSpan DisplayTime;
      NanosecondTimeSpan Work;
      bool Late{false};
      NanosecondTimeSpan NextStartAhead;
    };

    PacerSettings m_settings;
    std::vector<Entry> m_entries;
    std::size_t m_first{0};
    std::size_t m_count{0};
    NanosecondTimeSpan m_workSum;
    uint32_t m_lateCount{0};
    NanosecondTimeSpan m_startsAhead;
    uint32_t m_preferredSwapInterval{1};
    uint32_t m_swapInterval{1};

  public:
    //! Allocates the window: enough frames for FrameWindowLength at the preferred swap interval, twice over (room for a faster display).
    explicit SwapIntervalRule(const PacerSettings& settings);

    //! A frame was shown: when (displayTime, on any clock that runs on: only the differences count), how long it worked and whether it
    //! was late. Then the rule decides: the new swap interval is SwapInterval(), and the window restarts on a change.
    //! nextStartAhead: how long before the time it was given the frame after it began (below zero: after it). Added up for the
    //! frames that are not late (FrameWindowState::StartsAhead), and of no weight in the decision.
    SwapIntervalChange AddFrame(NanosecondTimeSpan displayTime, NanosecondTimeSpan work, bool late, NanosecondTimeSpan nextStartAhead = {}) noexcept;

    //! The swap interval the next frame is paced at.
    [[nodiscard]] uint32_t SwapInterval() const noexcept
    {
      return m_swapInterval;
    }

    //! The fastest swap interval: the preferred one on the display's refresh period (PacerSettings::PreferredSwapIntervalAt).
    [[nodiscard]] uint32_t PreferredSwapInterval() const noexcept
    {
      return m_preferredSwapInterval;
    }

    //! The refresh period the rule decides at: its settings'.
    [[nodiscard]] RefreshPeriod Refresh() const noexcept
    {
      return m_settings.Refresh();
    }

    //! The display's refresh period changed: the rule starts again at the preferred swap interval on it, with an empty window. Its
    //! settings have the new period. Never allocates: a window that has no room for all the frames of a faster display counts as
    //! full when it holds all it can.
    void SetRefreshPeriod(RefreshPeriod period) noexcept;

    //! Other settings: the rule starts again with them at the preferred swap interval, with an empty window. The same settings change
    //! nothing. Allocates when the window needs more room than it has (a longer window, a faster display, a faster frame rate), and
    //! only then: not a call for every frame unless the settings are the same.
    void SetSettings(const PacerSettings& settings);

    //! Start again at swapInterval (at least the preferred one) with an empty window.
    void Reset(uint32_t swapInterval) noexcept;

    //! Empty the window, keeping the swap interval.
    void Clear() noexcept;

    //! Go on from another rule with the same settings: its swap interval and its window, frame for frame. Never allocates: with
    //! a window of another size (other settings) the swap interval is taken and the window starts empty.
    void TakeOver(const SwapIntervalRule& other) noexcept;

    //! The frames to come are given display times on another clock (a grid of refreshes that starts anew, another pacer's
    //! times): the window's frames are moved onto it, the newest of them to displayTime, so that their distances stay what
    //! they were and the frames to come are counted on from them. An empty window has nothing to move.
    void RebaseNewest(NanosecondTimeSpan displayTime) noexcept;

    [[nodiscard]] FrameWindowState FrameWindow() const noexcept;

    //! The settings the rule decides with: the ones it was made with, or was given since, with the refresh period it is on.
    [[nodiscard]] const PacerSettings& Settings() const noexcept
    {
      return m_settings;
    }

  private:
    [[nodiscard]] const Entry& At(std::size_t index) const noexcept;
    [[nodiscard]] bool IsFull() const noexcept;
    void PopFront() noexcept;
  };
}

#endif
