#ifndef MB_FRAMEPACING_PACER_TIER_VBLANKLOOPPACER_HPP
#define MB_FRAMEPACING_PACER_TIER_VBLANKLOOPPACER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/RefreshTime.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <mb/framepacing/pacer/hold/PresentWaitRule.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/rule/FrameWorkRule.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalRule.hpp>
#include <mb/framepacing/pacer/tier/PacerHandover.hpp>
#include <mb/framepacing/pacer/timeline/VBlankTimeline.hpp>
#include <array>
#include <cstddef>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). The frame loop places every frame
  //! on a vertical blank of the display: what the tiers with vertical blank times and no timed present have in common, with
  //! the wait for a present as an option. VBlankPeriodOnlyPacer is this without the wait and VBlankWaitForPresentPacer with
  //! it, and what each of them promises is said there.
  //!
  //! Every frame is for one vertical blank, the one of the frame before it and its swap interval later. A frame is shown
  //! at a blank when it is ready the frame margin before it, and a frame is aimed to be ready at
  //! PacerSettings::ReadyPlacePercent of the refresh before its blank. With the aim of smoothness a frame starts at once and
  //! its present is held; with low latency its start is held and it is presented when it is done.
  //!
  //! Without the wait: one pause after start-up with the aim of low latency (StartupPauses), for the frames that pile up
  //! behind a new swap chain's first presents. With the wait: the loop is held until the present so many back was shown,
  //! what the waits say of where a frame was shown is taken (ShownLaterByWaits), the place a frame is to be ready at is
  //! learnt from it (ReadyPlaceNow), and a window that is not shown stops the waits (PresentWaitsStopped).
  //!
  //! Values in, values out: no platform API, no clock read, no wait. Made once (it allocates the rule's frame window); pacing
  //! frames never allocates.
  class VBlankLoopPacer
  {
    //! The frames whose time from start to present the low latency aim holds a frame's start by
    static constexpr std::size_t LeadFrames = 8;

    SwapIntervalRule m_rule;
    // What holds the loop: a wait for a present, or nothing but the times the pacer gives
    bool m_waitsForPresent;
    FrameWorkRule m_frameWork;
    // Where the display's refreshes are: the vertical blanks, from the readings
    VBlankTimeline m_timeline;
    // The frame between BeginFrame and the next BeginFrame: the vertical blank it is for, and the one it is shown at as far as
    // that is known (later than the one it is for once its present or a wait says so)
    bool m_hasFrame{false};
    uint64_t m_frameId{0};
    NanosecondTickCount m_startTime;
    uint32_t m_swapInterval{1};
    int64_t m_displaySlot{0};
    bool m_startedLate{false};
    NanosecondTimeSpan m_work;
    bool m_frameOpen{false};
    bool m_frameEnded{false};
    NanosecondTickCount m_presentTime;
    bool m_hasPresentTime{false};
    // What the waits say of the frame last made: the earliest vertical blank it can be shown at, and the latest
    bool m_hasShownFloor{false};
    int64_t m_shownFloor{0};
    bool m_hasShownCeiling{false};
    int64_t m_shownCeiling{0};
    uint64_t m_shownLaterByWaits{0};
    // The frames a wait said were shown later than the pacer had worked out, with no more than a few frames between them, the
    // frames since the last one, and how far the place a frame is to be ready at was moved earlier for it, in steps
    uint32_t m_shownLaterCount{0};
    uint32_t m_framesSinceShownLater{0};
    uint32_t m_readyPlaceSteps{0};
    // A window that is not shown says nothing of where a display takes a frame: the frames since a wait ran out or the waits
    // were stopped, and the frames since the place was last moved (each counted to a few)
    uint32_t m_framesSinceDisturbed{UINT32_MAX};
    uint32_t m_framesSincePlaceStep{UINT32_MAX};
    // How long the last frames took from their start to the end of their CPU work
    std::array<NanosecondTimeSpan, LeadFrames> m_leads{};
    std::size_t m_leadCount{0};
    RefreshTime m_animationTime;
    NanosecondTimeSpan m_lastAnimationTime;
    uint64_t m_refreshesBehindClock{0};
    NanosecondTimeDuration m_lastPresentBlocked;
    // What holds the loop: the wait for a present
    PresentWaitRule m_wait;

    // The pause after start-up (low latency): still to be made, the first frame's start since it was asked for, whether the
    // system took a present since, and the refreshes the next frame is later by when it is made
    bool m_pausePending{true};
    bool m_pauseHasFirstFrame{false};
    NanosecondTickCount m_pauseFirstFrameTime;
    bool m_presentTaken{false};
    int64_t m_pauseSlots{0};
    uint64_t m_startupPauses{0};
    // Another pacer placed the frames before this one took over: the next frame is the first it places, held to the time
    // the frame before it gave. And the time each frame gives for the one after it
    bool m_takenOver{false};
    NanosecondTickCount m_takeOverStartTime;
    NanosecondTickCount m_nextFrameStartTime;

  public:
    //! waitsForPresent: the application can wait until a present was shown, and the frame start plan asks for it.
    VBlankLoopPacer(const PacerSettings& settings, bool waitsForPresent);

    //! The tier this pacer is for: with the wait, or without it.
    [[nodiscard]] PacerTier Tier() const noexcept
    {
      return m_waitsForPresent ? PacerTier::VBlankWaitForPresent : PacerTier::VBlankPeriodOnly;
    }

    //! Where the display's refreshes are: the time of a vertical blank of the display the window is on, a recent one or the next.
    //! Given whenever the application has one; the newest by its ReadTime counts. The frames go on from where they are. The
    //! first reading is taken whole. One after it moves the vertical blanks a quarter of the way to it (one reading is not
    //! exact), and one that is off where the readings before it put them is not taken by itself (VBlankJumps).
    void AddVBlank(const VBlankReading& reading) noexcept;

    //! Before a frame takes anything, at now on the application's steady clock: the present to wait for until it was shown (the
    //! one PacerSettings::WaitingPresents back, where the system took it and it can still be waited for) and the longest the wait
    //! may take (PacerSettings::PresentWaitSwapIntervals of the swap interval the frame is paced at), and after it, with the aim
    //! of low latency, the time to wait until. It changes nothing, so a frame may be planned again, and after AddPresentWait
    //! it is planned again: no second present is asked for, and the time is the one that holds then.
    [[nodiscard]] FrameStartPlan PlanFrame(NanosecondTickCount now) const noexcept;

    //! What became of the wait for a present the plan asked for. A wait that held the loop and ended with the present shown
    //! says which vertical blank that frame was shown at; one that returned at once says that it was shown by then. One that
    //! ended without the present shown is counted (PresentWaitTimeouts) and the frame it held is not judged; after
    //! PresentWaitRule::WaitsRunOutToStop of them in a row the pacer stops waiting (PresentWaitsStopped) until presents are shown again.
    void AddPresentWait(const PresentWaitReport& report) noexcept;

    //! The frame starts, at cpuStartTime: the previous frame is judged, the rule decides, and this frame is planned.
    FrameSchedule BeginFrame(NanosecondTickCount cpuStartTime) noexcept;

    //! The frame's CPU work is done, at workDoneTime: how to present it.
    PresentPlan EndFrame(NanosecondTickCount workDoneTime) noexcept;

    //! The frame's CPU busy time so far, at now, for a marker that is drawn while the frame's work is still going on: from the
    //! frame's start to now. Zero: no frame is open.
    [[nodiscard]] NanosecondTimeDuration CpuBusyAt(NanosecondTickCount now) const noexcept;

    //! After the present, before the next frame is planned: when it was called says which vertical blank the frame is shown at
    //! (without the report it is taken as made when EndFrame said), and whether the system took it says whether it can be
    //! waited for. A present the system did not take says the swap chain is gone: neither it nor a present before it is
    //! waited for.
    void AddPresent(const PresentReport& report) noexcept;

    //! The GPU's work on an earlier frame, when the application has it: from then on a frame is ready when the GPU is done with
    //! it, and a frame's work is the CPU's and the GPU's (FrameWorkRule).
    void AddGpuWork(const GpuWorkReport& report) noexcept;

    //! The presents made so far are gone (a swap chain was made anew): none of them is waited for, and the waits that ran out
    //! are forgotten with them. Nothing else changes.
    void ForgetPresents() noexcept;

    //! The display's refresh period changed (a mode change, the window on another display): the frames start again on it with an
    //! empty frame window, at the swap interval the application prefers there, and the vertical blank readings from before are
    //! not of this display. The animation time goes on.
    void SetRefreshPeriod(RefreshPeriod period) noexcept;

    //! Other settings on a live pacer: it starts again with them. The same settings change nothing. Allocates when the frame
    //! window needs more room than it has, and only then.
    void SetSettings(const PacerSettings& settings);

    //! Start again (after a pause the application knows of): the next frame is for the first vertical blank it can be ready for,
    //! the frame window is empty, the swap interval the preferred one, the GPU's work is forgotten and no present from before is
    //! waited for. Where the refreshes are is kept, and the animation time goes on.
    void Reset() noexcept;

    //! True between a frame's start and its end: a change of what paces waits for the end.
    [[nodiscard]] bool IsFrameOpen() const noexcept
    {
      return m_frameOpen && !m_frameEnded;
    }

    //! The swap interval rule, for a pacer that takes over.
    [[nodiscard]] const SwapIntervalRule& Rule() const noexcept
    {
      return m_rule;
    }

    //! What goes on when another pacer takes over: the frames, the animation time, the GPU's work and the presents.
    [[nodiscard]] PacerHandover GiveOver() const noexcept;

    //! Go on from another pacer, which has the same settings: its frames and their ids, its animation time, its swap interval
    //! and the rule's frame window. Where the frames are placed starts with the next frame, which is held to the time the frame
    //! before it gave for it and is not judged against it. Never allocates.
    void TakeOver(const PacerHandover& handover, const SwapIntervalRule& rule) noexcept;

    //! Whether the frame start plan asks for a wait for a present, from the next frame on. A loop that such a wait held has
    //! no frames piled up behind its first presents, so giving the wait up starts no pause after start-up.
    void SetWaitsForPresent(const bool waitsForPresent) noexcept
    {
      m_pausePending = m_pausePending && (!m_waitsForPresent || waitsForPresent);
      m_waitsForPresent = waitsForPresent;
    }

    //! The vertical blanks are not known any more: the next reading is the first. For a pacer that takes the times up again
    //! after a while without them, or gives them up.
    void ForgetVBlanks() noexcept
    {
      m_timeline.Clear();
    }

    //! True once a vertical blank reading was given for the display the pacer is on: until then the refreshes are a guess.
    [[nodiscard]] bool HasVBlankReading() const noexcept
    {
      return m_timeline.HasReading();
    }

    //! The readings that were more than an eighth of a refresh period off where the readings before them put the vertical
    //! blanks, since the pacer was made: a display that changed, or readings that are not exact. Such a reading is not
    //! taken by itself (the frames go on by the refresh period from the last reading that was taken); eight in a row
    //! that are on one grid of their own move the pacer to it. A count that rises with nearly every reading says the
    //! source is no vertical blank time, and the pacer is then a pacer on a timer.
    [[nodiscard]] uint64_t VBlankJumps() const noexcept
    {
      return m_timeline.Jumps();
    }

    //! How far the animation time is behind the display, in refreshes, since the pacer was made: the refreshes frames were
    //! shown later than they were made for, which it is not moved over.
    [[nodiscard]] uint64_t RefreshesBehindClock() const noexcept
    {
      return m_refreshesBehindClock;
    }

    //! The refreshes a wait said a frame was shown later than the pacer had worked out, since the pacer was made: a frame that
    //! waited although it was ready in time, a refresh the display lost by itself.
    [[nodiscard]] uint64_t ShownLaterByWaits() const noexcept
    {
      return m_shownLaterByWaits;
    }

    //! Where in the refresh before its vertical blank a frame is to be ready now: PacerSettings::ReadyPlacePercent of the
    //! refresh period at first, and earlier by an eighth of a period each time two frames within a few that were ready there
    //! were shown a vertical blank late (the display takes a frame sooner before a vertical blank than that, or the GPU needs time
    //! nobody reported). Never later again until the pacer starts again with other settings or is reset.
    [[nodiscard]] NanosecondTimeSpan ReadyPlaceNow() const noexcept
    {
      return ReadyPlace();
    }

    //! The pauses after start-up that were made, since the pacer was made.
    [[nodiscard]] uint64_t StartupPauses() const noexcept
    {
      return m_startupPauses;
    }

    //! True while the pacer does not wait for presents, because its waits ran out: every PresentWaitRule::FramesBetweenAsks frames the plan asks,
    //! with no time to wait, whether an older present was shown, and PresentWaitRule::AsksShownToWait answers in a row that say shown end it.
    [[nodiscard]] bool PresentWaitsStopped() const noexcept
    {
      return m_wait.Stopped();
    }

    //! The waits for a present that ended without the present being shown, since the pacer was made.
    [[nodiscard]] uint64_t PresentWaitTimeouts() const noexcept
    {
      return m_wait.Timeouts();
    }

    //! The GPU time a frame is judged with: the newest that was reported, zero without one.
    [[nodiscard]] NanosecondTimeDuration GpuTime() const noexcept
    {
      return m_frameWork.GpuTime();
    }

    //! How long the last present that was reported held the frame loop.
    [[nodiscard]] NanosecondTimeDuration LastPresentBlocked() const noexcept
    {
      return m_lastPresentBlocked;
    }

    [[nodiscard]] FrameWindowState FrameWindow() const noexcept
    {
      return m_rule.FrameWindow();
    }

    //! The swap interval the next frame is paced at.
    [[nodiscard]] uint32_t SwapInterval() const noexcept
    {
      return m_rule.SwapInterval();
    }

    [[nodiscard]] RefreshPeriod Refresh() const noexcept
    {
      return m_rule.Refresh();
    }

    [[nodiscard]] const PacerSettings& Settings() const noexcept
    {
      return m_rule.Settings();
    }

  private:
    [[nodiscard]] bool StartsAgainAt(NanosecondTickCount time) const noexcept;
    [[nodiscard]] NanosecondTickCount TimeOfBlank(int64_t slot) const noexcept;
    [[nodiscard]] int64_t BlankAtOrBefore(NanosecondTickCount time) const noexcept;
    [[nodiscard]] int64_t FirstBlankAfterReadyAt(NanosecondTickCount readyTime) const noexcept;
    [[nodiscard]] int64_t Reserve() const noexcept;
    [[nodiscard]] NanosecondTimeSpan ReadyPlace() const noexcept;
    [[nodiscard]] NanosecondTimeSpan GpuLead() const noexcept;
    [[nodiscard]] NanosecondTimeSpan ShortestLead() const noexcept;
    [[nodiscard]] NanosecondTimeSpan LongestLead() const noexcept;
    [[nodiscard]] int64_t ShownSlotByPresent() const noexcept;
    [[nodiscard]] int64_t ShownSlot() const noexcept;
    [[nodiscard]] int64_t DisplaySlotFor(NanosecondTickCount startTime, bool hasPrevious) const noexcept;
    [[nodiscard]] NanosecondTickCount StartTimeFor(int64_t displaySlot) const noexcept;
    [[nodiscard]] NanosecondTickCount PresentTimeFor(int64_t displaySlot) const noexcept;
    void ArmStartupPause() noexcept;
    [[nodiscard]] int64_t StartupPauseAt(NanosecondTickCount cpuStartTime) noexcept;
  };
}

#endif
