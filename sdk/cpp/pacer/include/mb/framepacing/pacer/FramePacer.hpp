#ifndef MB_FRAMEPACING_PACER_FRAMEPACER_HPP
#define MB_FRAMEPACING_PACER_FRAMEPACER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/FrameEnd.hpp>
#include <mb/framepacing/pacer/FrameInput.hpp>
#include <mb/framepacing/pacer/FrameSchedule.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/SwapIntervalRule.hpp>
#include <mb/framepacing/pacer/WindowState.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! Paces frames on a grid of refreshes (sdk/doc/pacer.md). Every frame: BeginFrame with what the platform knows, apply the schedule
  //! (a swap interval, a present time, or sleep until EarliestPresentTicks), draw, EndFrame when presenting. The pacer aims every frame at
  //! the refresh one swap interval after the previous frame's, and the swap interval rule adapts the interval to how long frames take.
  //! Values in, values out: the pacer calls no platform API. Made once (it allocates the rule's window); nothing after that allocates.
  class FramePacer
  {
    SwapIntervalRule m_rule;
    RefreshPeriod m_period;
    // The refresh grid: refresh m_originSlot is at m_originTicks + m_originFraction / 2^32 ticks
    int64_t m_originTicks{0};
    uint32_t m_originFraction{0};
    int64_t m_originSlot{0};
    bool m_started{false};
    uint64_t m_nextFrameIndex{0};
    // The frame between BeginFrame and the next BeginFrame
    bool m_frameOpen{false};
    bool m_frameEnded{false};
    int64_t m_targetSlot{0};
    int64_t m_cpuStartTicks{0};
    int64_t m_presentTicks{0};
    int64_t m_workTicks{0};

  public:
    explicit FramePacer(const PacerSettings& settings);

    //! Start a frame: the previous frame's display is resolved (from PreviousDisplayTicks, or inferred from its Present), the rule
    //! decides, and this frame is planned.
    FrameSchedule BeginFrame(const FrameInput& input) noexcept;

    //! The frame is presented now. Returns the CPU busy time (PresentTicks - the frame's CpuStartTicks) for the marker.
    uint32_t EndFrame(const FrameEnd& end) noexcept;

    //! The display's refresh period changed (a mode change, another monitor): the pacer starts again on a new grid with an empty window
    //! at the preferred swap interval. The frame count goes on. The period the pacer has already changes nothing. A platform that
    //! reports the period with every frame passes it in FrameInput::RefreshPeriodNanoseconds instead.
    void SetRefreshPeriod(RefreshPeriod period) noexcept;

    //! Start again (after a pause): the next frame is planned as the first, the window is empty, the swap interval the preferred one.
    void Reset() noexcept;

    [[nodiscard]] WindowState Window() const noexcept
    {
      return m_rule.Window();
    }

    //! The swap interval the next frame is paced at.
    [[nodiscard]] uint32_t SwapInterval() const noexcept
    {
      return m_rule.SwapInterval();
    }

    //! The refresh period the pacer paces at now (PacerSettings::Refresh until the period changes).
    [[nodiscard]] RefreshPeriod Refresh() const noexcept
    {
      return m_period;
    }

    //! The settings the pacer was made with.
    [[nodiscard]] const PacerSettings& Settings() const noexcept
    {
      return m_rule.Settings();
    }

  private:
    [[nodiscard]] int64_t SlotTicks(int64_t slot) const noexcept;
    [[nodiscard]] int64_t SlotAtOrBefore(int64_t ticks) const noexcept;
    [[nodiscard]] int64_t SlotAtOrAfter(int64_t ticks) const noexcept;
    [[nodiscard]] int64_t NearestSlot(int64_t ticks) const noexcept;
    [[nodiscard]] bool IsNearGrid(int64_t ticks) const noexcept;
    void Anchor(int64_t ticks, int64_t slot) noexcept;
    void MoveOrigin(int64_t slot) noexcept;
  };
}

#endif
