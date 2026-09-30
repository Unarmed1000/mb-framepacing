#ifndef MB_FRAMEPACING_PACER_RULE_SWAPINTERVALRULE_HPP
#define MB_FRAMEPACING_PACER_RULE_SWAPINTERVALRULE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/rule/WindowState.hpp>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace MB::FramePacing::Pacer
{
  //! The adaptive swap interval rule (sdk/doc/pacer.md): it keeps the frames of the last WindowTicks since its last change and decides
  //! after every frame whether to run slower or faster. FramePacer uses it; an application with its own frame loop can use it alone.
  //! The window is allocated once, when the rule is made; AddFrame and the rest never allocate.
  class SwapIntervalRule
  {
    struct Entry
    {
      int64_t DisplayTicks{0};
      int64_t WorkTicks{0};
      bool Late{false};
    };

    PacerSettings m_settings;
    std::vector<Entry> m_entries;
    std::size_t m_first{0};
    std::size_t m_count{0};
    int64_t m_workSum{0};
    uint32_t m_lateCount{0};
    uint32_t m_swapInterval{1};

  public:
    //! Allocates the window (PacerSettings::WindowCapacity frames, or enough for the window at the preferred swap interval).
    explicit SwapIntervalRule(const PacerSettings& settings);

    //! A frame was shown: when (displayTicks), how long it worked (workTicks) and whether it was late. Then the rule decides at the
    //! refresh period: the new swap interval is SwapInterval(), and the window restarts on a change.
    SwapIntervalChange AddFrame(int64_t displayTicks, int64_t workTicks, bool late, RefreshPeriod period) noexcept;

    //! The swap interval the next frame is paced at.
    [[nodiscard]] uint32_t SwapInterval() const noexcept
    {
      return m_swapInterval;
    }

    //! Start again at swapInterval (at least the preferred one) with an empty window.
    void Reset(uint32_t swapInterval) noexcept;

    //! Empty the window, keeping the swap interval.
    void Clear() noexcept;

    [[nodiscard]] WindowState Window() const noexcept;

    [[nodiscard]] const PacerSettings& Settings() const noexcept
    {
      return m_settings;
    }

  private:
    [[nodiscard]] const Entry& At(std::size_t index) const noexcept;
    void PopFront() noexcept;
  };
}

#endif
