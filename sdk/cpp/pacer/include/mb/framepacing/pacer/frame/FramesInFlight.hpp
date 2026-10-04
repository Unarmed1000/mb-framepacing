#ifndef MB_FRAMEPACING_PACER_FRAME_FRAMESINFLIGHT_HPP
#define MB_FRAMEPACING_PACER_FRAME_FRAMESINFLIGHT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/frame/PresentFeedback.hpp>
#include <mb/framepacing/pacer/frame/PresentFeedbackState.hpp>
#include <array>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). The frames that were begun and whose present feedback has not come yet, and what
  //! the display times the platform reports say about them: statistics, and the time the newest frame is due on the display. FramePacer
  //! uses it when PacerSettings::UsePresentFeedback is on; an application with its own frame loop can use it alone. Nothing here paces
  //! a frame: the pacer decides by the frame starts and the frames' work, with or without feedback.
  //!
  //! Feedback comes a few frames after its frame, may be missing for any frame, and is refused when it can not be a refresh of the
  //! display: before the frame's present, or not a whole number of refreshes (within an eighth of one) after the display time used
  //! before it. A refused display time is remembered: when the next one is a whole number of refreshes after it, the count starts
  //! again from that one (a new swap chain, a mode change).
  //!
  //! The whole refreshes between two display times, against the swap intervals of the frames between, is how many refreshes the
  //! display fell behind (PresentFeedbackState::LateRefreshes). A frame shown sooner than its swap interval after the frame before it
  //! (two presents the display took in one refresh, a sleep that lands a present a refresh early) puts the count ahead (the lead), and
  //! later refreshes use the lead up before they count: a frame shown early and its neighbour shown as much later lost no refresh.
  //! No allocation.
  class FramesInFlight
  {
  public:
    //! The frames kept. Feedback for an older frame is refused: more than any platform's queue of results (VK_EXT_present_timing's
    //! is 32 here).
    static constexpr uint32_t Capacity = 64;

  private:
    struct Entry
    {
      // The swap intervals of every frame begun up to this one, added up
      uint64_t SwapSum{0};
      TickCount64 PresentTime;
    };

    RefreshPeriod m_period;
    std::array<Entry, Capacity> m_entries{};
    // The newest frame begun (0: none), the oldest one kept, and the newest one feedback came for
    uint64_t m_newestId{0};
    uint64_t m_oldestId{1};
    uint64_t m_newestFeedbackId{0};
    uint64_t m_swapSum{0};
    // The display time used last: the count's anchor
    bool m_hasAnchor{false};
    TickCount64 m_anchorTime;
    uint64_t m_anchorId{0};
    uint64_t m_anchorSwapSum{0};
    // Refreshes the count is ahead of the display
    uint64_t m_lead{0};
    // A display time that was refused as off the anchor's grid
    bool m_hasCandidate{false};
    TickCount64 m_candidateTime;
    PresentFeedbackState m_state;

  public:
    explicit FramesInFlight(RefreshPeriod period) noexcept;

    //! A frame begins, at startTime on the application's steady clock, paced at swapInterval: its id (the first is 1, each one more).
    //! The oldest frame leaves when Capacity frames are kept.
    uint64_t Begin(uint32_t swapInterval, TickCount64 startTime) noexcept;

    //! The time the newest frame is presented at (the frame's start until this is called). Nothing without a frame.
    void End(TickCount64 presentTime) noexcept;

    //! Present feedback for a frame that was begun: oldest first, at most once a frame.
    void Add(const PresentFeedback& feedback) noexcept;

    //! When the newest frame is shown if no frame from the newest display time used to it is late, on the application's steady clock:
    //! that display time plus the swap intervals since. TickCount64() (unknown) while no display time is in use.
    [[nodiscard]] TickCount64 IntendedDisplayTime() const noexcept;

    //! The id of the newest frame begun, 0 before the first.
    [[nodiscard]] uint64_t NewestFrameId() const noexcept
    {
      return m_newestId;
    }

    //! Start again: the frames begun so far are forgotten (feedback for them is refused), and nothing is counted across.
    void Restart() noexcept;

    //! The display's refresh period changed: Restart, on the new period.
    void SetRefreshPeriod(RefreshPeriod period) noexcept;

    [[nodiscard]] RefreshPeriod Refresh() const noexcept
    {
      return m_period;
    }

    //! What became of the feedback so far, and what it said.
    [[nodiscard]] PresentFeedbackState State() const noexcept
    {
      return m_state;
    }

  private:
    [[nodiscard]] Entry& At(uint64_t frameId) noexcept;
    [[nodiscard]] bool IsWholeRefreshes(TimeSpan step) const noexcept;
  };
}

#endif
