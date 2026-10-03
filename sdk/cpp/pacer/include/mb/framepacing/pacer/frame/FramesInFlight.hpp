#ifndef MB_FRAMEPACING_PACER_FRAME_FRAMESINFLIGHT_HPP
#define MB_FRAMEPACING_PACER_FRAME_FRAMESINFLIGHT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/frame/MeasuredFrame.hpp>
#include <mb/framepacing/pacer/frame/PresentFeedback.hpp>
#include <mb/framepacing/pacer/frame/PresentFeedbackState.hpp>
#include <array>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). The frames that were begun and whose present feedback has not come yet, and the
  //! measurement of each from the display time the platform reports for it. FramePacer uses it when PacerSettings::UsePresentFeedback is
  //! on; an application with its own frame loop can use it alone.
  //!
  //! The frame starts measure the display while they stay within half a refresh of its refreshes (PacerRefreshClock); on a machine busy
  //! with other work, at a high refresh rate, they do not. The display times do: the whole refreshes between two of them, against the
  //! swap intervals of the frames between, is how many refreshes late the newer frame was. Feedback comes a few frames after its frame, may be
  //! missing for any frame, and is refused when it can not be a refresh of the display: before the frame's present, or not a whole number of
  //! refreshes (within an eighth of one) after the display time used before it. A refused display time is remembered: when the next
  //! one is a whole number of refreshes after it, the measurement starts again from that one (a new swap chain, a mode change).
  //!
  //! The count never runs behind the display: a frame shown sooner than its swap interval after the frame before it (two presents the
  //! display took in one refresh) puts the count ahead (the lead), and later late refreshes use the lead up before they count. No
  //! allocation.
  class FramesInFlight
  {
  public:
    //! The frames kept. A frame that many frames old leaves as on time when no display time has decided it, and feedback for an older
    //! frame is refused: more than any platform's queue of results (VK_EXT_present_timing's is 32 here).
    static constexpr uint32_t Capacity = 64;

  private:
    struct Entry
    {
      // The swap intervals of every frame begun up to this one, added up
      uint64_t SwapSum{0};
      TimeSpan AnimationTime;
      TimeSpan Work;
      TickCount64 PresentTime;
      bool Late{false};
      bool HasFeedback{false};
    };

    RefreshPeriod m_period;
    std::array<Entry, Capacity> m_entries{};
    // The newest frame begun (0: none), the oldest one kept, the next one TakeMeasured gives, and the newest one a display time decided
    uint64_t m_newestId{0};
    uint64_t m_oldestId{1};
    uint64_t m_nextMeasuredId{1};
    uint64_t m_decidedId{0};
    uint64_t m_newestFeedbackId{0};
    uint64_t m_swapSum{0};
    // The display time used last: the measurement's anchor
    bool m_hasAnchor{false};
    TickCount64 m_anchorTime;
    uint64_t m_anchorId{0};
    uint64_t m_anchorSwapSum{0};
    // Refreshes the count is ahead of the display
    uint32_t m_lead{0};
    // A display time that was refused as off the anchor's grid
    bool m_hasCandidate{false};
    TickCount64 m_candidateTime;
    uint32_t m_lateRefreshes{0};
    PresentFeedbackState m_state;

  public:
    explicit FramesInFlight(RefreshPeriod period) noexcept;

    //! A frame begins, at startTime on the application's steady clock, paced at swapInterval for animationTime: its id (the first is
    //! 1, each one more). The oldest frame leaves when Capacity frames are kept.
    uint64_t Begin(uint32_t swapInterval, TimeSpan animationTime, TickCount64 startTime) noexcept;

    //! The newest frame's work, and the time it is presented at (the frame's start until this is called). Nothing without a frame.
    void End(TimeSpan work, TickCount64 presentTime) noexcept;

    //! Present feedback for a frame that was begun: oldest first, at most once a frame.
    void Add(const PresentFeedback& feedback) noexcept;

    //! The refreshes the frames measured since the last call were late by, in all: the next frame's animation steps that much further.
    [[nodiscard]] uint32_t TakeLateRefreshes() noexcept;

    //! The next frame that is decided, oldest first: one a display time at or after it was used for, or one that is about to leave
    //! (on time, as nothing said otherwise). false: none now.
    [[nodiscard]] bool TakeMeasured(MeasuredFrame& rFrame) noexcept;

    //! When the newest frame is shown if no frame from the newest display time used to it is late, on the application's steady clock:
    //! that display time plus the swap intervals since (and the lead). TickCount64() (unknown) while no display time is in use.
    [[nodiscard]] TickCount64 IntendedDisplayTime() const noexcept;

    //! The id of the newest frame begun, 0 before the first.
    [[nodiscard]] uint64_t NewestFrameId() const noexcept
    {
      return m_newestId;
    }

    //! Start again: the frames begun so far are forgotten (feedback for them is refused), and nothing is measured across.
    void Restart() noexcept;

    //! The display's refresh period changed: Restart, on the new period.
    void SetRefreshPeriod(RefreshPeriod period) noexcept;

    [[nodiscard]] RefreshPeriod Refresh() const noexcept
    {
      return m_period;
    }

    //! What became of the feedback so far.
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
