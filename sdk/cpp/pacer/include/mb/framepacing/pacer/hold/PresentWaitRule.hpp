#ifndef MB_FRAMEPACING_PACER_HOLD_PRESENTWAITRULE_HPP
#define MB_FRAMEPACING_PACER_HOLD_PRESENTWAITRULE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). What holds the frame loop where the
  //! application can wait until a present was shown: one of the parts a pacer is put together from, the same in every tier
  //! that has the wait. It says which present the frame start plan asks for and how long the wait may take, and it knows
  //! what a wait that ran out means. Values in, values out: no clock is read and nothing is allocated.
  //!
  //! The wait is for the present so many frames back that the presents that may wait (PacerSettings::WaitingPresents) are in
  //! between. A wait that ends without its present shown is counted (Timeouts), and the frame it held is not to be judged by
  //! the pacer: the pacer asked for the wait. After WaitsRunOutToStop of them in a row the display is not taking the window's
  //! frames (a window that is covered or minimised): the waits stop (Stopped), and once in FramesBetweenAsks frames the plan
  //! asks, with no time to wait, whether an older present was shown. AsksShownToWait answers in a row that say shown end it.
  class PresentWaitRule
  {
    // The presents: the last one the system took, and the first that can still be waited for (0: none yet)
    uint64_t m_lastAcceptedId{0};
    uint64_t m_oldestWaitableId{1};
    uint64_t m_timeouts{0};
    // The present the wait before the next frame was made for: it is not asked for again
    uint64_t m_waitedForId{0};
    // The waits in a row that ran out (no more than it takes to stop waiting), and whether one held the loop before the frame
    // that is about to start
    uint32_t m_waitsRunOut{0};
    bool m_waitRanOut{false};
    // The present the first of those waits was for: one made before it was shown, and says nothing of the display now
    uint64_t m_runOutFromId{0};
    // While the waits are stopped: the frames since the plan last asked after a present, and the answers in a row that said
    // shown
    uint32_t m_framesSinceAsk{0};
    uint32_t m_shownAsks{0};
    // A wait was reported since the last frame started: a frame has one wait for a present, not two
    bool m_waitReported{false};
    // A wait ran out, or was answered while the waits were stopped, since the last frame started
    bool m_disturbed{false};

  public:
    //! The waits in a row that run out before the waits stop. One by itself happens (a present at the start of a window that
    //! is never shown); two in a row is a display that does not take this window's frames.
    static constexpr uint32_t WaitsRunOutToStop = 2;

    //! While the waits are stopped the plan asks after a present once in this many frames. Asking is not free everywhere (on
    //! the first integration's system it took 10 ms while the window was covered), and a covered window's frames are shown
    //! now and then, a few in a row.
    static constexpr uint32_t FramesBetweenAsks = 16;

    //! The answers in a row that say shown before the waits are made again: one can be a covered window's frame shown in
    //! passing.
    static constexpr uint32_t AsksShownToWait = 2;

    //! A wait held the loop when it took this share of a refresh period or more: one in this many.
    static constexpr int64_t HeldDivisor = 8;

    //! A frame start plan with the wait for a present the next frame begins with, if there is one to make: the present so
    //! many back, and the longest the wait may take (a few of the frame's own swap intervals, as a present that is never
    //! shown is to hold the loop no longer). While the waits are stopped the plan asks once in a number of frames, with no
    //! time to wait, after a present that has had the time a wait would have given it. The plan has no start time.
    [[nodiscard]] FrameStartPlan Plan(const PacerSettings& settings, RefreshPeriod period, uint32_t swapInterval) const noexcept;

    //! True when the wait took a share of a refresh period (one in HeldDivisor) or more: it held the loop.
    [[nodiscard]] static bool HeldTheLoop(const PresentWaitReport& report, RefreshPeriod period) noexcept;

    //! How a wait the plan asked for ended. True when it was a wait that was made (not an answer while the waits were
    //! stopped) and its present was shown: its end then says something of the display, which is the pacer's to use.
    [[nodiscard]] bool AddPresentWait(const PresentWaitReport& report, RefreshPeriod period) noexcept;

    //! A present was made: one the system took can be waited for, and one it refused is gone with the presents before it.
    void AddPresent(const PresentReport& report) noexcept;

    //! The swap chain was made anew: the presents made so far are never shown, and none of them is waited for. The waits
    //! that ran out go with them.
    void ForgetPresents() noexcept;

    //! A frame starts: the waits before it are done with. Call it after reading WaitRanOut and Disturbed for that frame.
    void BeginFrame() noexcept;

    //! ForgetPresents, and what the waits before the next frame said is dropped.
    void Reset() noexcept;

    //! True when a wait since the last frame started held the loop until it ran out (or an answer while the waits are
    //! stopped held it): the frame that starts now starts late by the pacer's own doing, and is not to be judged.
    [[nodiscard]] bool WaitRanOut() const noexcept
    {
      return m_waitRanOut;
    }

    //! True when the display is not, or was not a moment ago, showing the window's frames: a wait ran out or was answered
    //! while stopped since the last frame started, or the waits are stopped.
    [[nodiscard]] bool Disturbed() const noexcept
    {
      return m_disturbed || Stopped();
    }

    //! True while no wait is made because the waits ran out.
    [[nodiscard]] bool Stopped() const noexcept
    {
      return m_waitsRunOut >= WaitsRunOutToStop;
    }

    //! The waits and answers that ended without their present shown.
    [[nodiscard]] uint64_t Timeouts() const noexcept
    {
      return m_timeouts;
    }
  };
}

#endif
