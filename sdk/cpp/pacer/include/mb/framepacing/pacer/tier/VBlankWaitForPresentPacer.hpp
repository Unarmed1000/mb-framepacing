#ifndef MB_FRAMEPACING_PACER_TIER_VBLANKWAITFORPRESENTPACER_HPP
#define MB_FRAMEPACING_PACER_TIER_VBLANKWAITFORPRESENTPACER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan32.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/RefreshTime.hpp>
#include <mb/framepacing/pacer/capability/HoldTier.hpp>
#include <mb/framepacing/pacer/capability/QueueTier.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/rule/FrameWorkRule.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalRule.hpp>
#include <array>
#include <cstddef>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built one tier's pacer at a time; FramePacer is
  //! the pacer the library has until they replace it). The pacer of HoldTier::VBlank with QueueTier::WaitForPresent: a whole pacer
  //! by itself, for an application that has the baseline (a steady clock, the refresh period of the display its window is on, a
  //! wait until a time, a present that shows every frame in order for at least a refresh), is told when that display's vertical
  //! blanks are (PacerCapability::VBlankTimes) and can wait until a present it made was shown (PacerCapability::WaitForPresent).
  //!
  //! It holds every rule and every time calculation, and the application carries out what it is given. Its calls a frame:
  //!   AddVBlank       a vertical blank's time, whenever the application has one: where the display's refreshes are
  //!   PlanFrame       before the frame takes anything: the present to wait for, and then the time to wait until
  //!   AddPresentWait  after that wait: what became of it. PlanFrame is then called again
  //!   BeginFrame      the frame starts: its swap interval, its animation time and the marker's values
  //!   EndFrame        the CPU's work is done: the time to wait until before the present
  //!   AddPresent      after the present, before the next frame is planned: when it was called and returned, and whether the
  //!                   system took it
  //! and, where the application has it, AddGpuWork: the GPU's work on an earlier frame.
  //!
  //! It is VBlankPeriodOnlyPacer with the wait of TimerWaitForPresentPacer. Every frame is for one vertical blank, the one of
  //! the frame before it and its swap interval later; a frame is shown at a blank when it is ready the frame margin before it;
  //! and the pacer aims a frame to be ready at PacerSettings::ReadyPlacePercent of the refresh before its blank. What the wait
  //! adds:
  //!
  //! The frames that wait can not grow. Before a frame the loop waits until the present PacerSettings::WaitingPresents back was
  //! shown, whatever the pacer had worked out, so no pause after start-up is needed.
  //!
  //! Which vertical blank a frame was shown at becomes a fact. A wait that held the loop ended shortly after the display took
  //! its frame, so the vertical blank at or before the wait's end is the one that frame was shown at, and since the display
  //! takes one frame per refresh the frames made after it are shown that many blanks later at the soonest. Where that is later
  //! than the pacer had it (a frame that waited although it was ready in time, a refresh the display lost by itself), the frame
  //! that was last made is late and the next one is for a blank that much later (ShownLaterByWaits). A wait that returned at
  //! once for the frame last made says that it was shown by then, which the aim of low latency does not count on by itself.
  //!
  //! Where a frame has to be ready is learnt from that. One frame shown later than worked out is a refresh the display lost.
  //! Two within a few frames is a display that takes a frame sooner before a vertical blank than the pacer has it ready (or a GPU whose
  //! time nobody reported), and the place a frame is to be ready at is moved an eighth of a period earlier (ReadyPlaceNow),
  //! down to the start of the refresh. The frames that showed it are late; what is late even then is late by its work.
  //!
  //! PacerAim::Smoothness, the default: a frame starts when the wait is over and its present is held until the time that has
  //! it ready at its place. At one refresh per frame PacerSettings::ReserveFrames frames are ready ahead of the display, and
  //! the wait keeps them to that.
  //!
  //! PacerAim::LowLatency: after the wait a frame's start is held so that it is ready at its place and no sooner, and it is
  //! presented when it is done, though never before the refresh it is to be ready in begins.
  //!
  //! A wait that runs out is the pacer's own doing: the frame it held is not judged. After WaitsRunOutToStop of them in a row
  //! the display is not taking the window's frames (a window that is covered or minimised) and the pacer stops waiting, all as
  //! TimerWaitForPresentPacer does (PresentWaitsStopped).
  //!
  //! The animation time is the time a frame is shown as far as that is known before the frame is made, as in
  //! VBlankPeriodOnlyPacer; what a wait says of a frame after the frames behind it were made is not caught up with
  //! (RefreshesBehindClock).
  //!
  //! Until the first vertical blank reading it takes the first frame's start as a vertical blank. A reading's own refresh
  //! period is not used yet: the period is the settings'.
  //!
  //! Values in, values out: no platform API, no clock read, no wait. Made once (it allocates the rule's frame window); pacing
  //! frames never allocates.
  class VBlankWaitForPresentPacer
  {
    //! The frames whose time from start to present the low latency aim holds a frame's start by
    static constexpr std::size_t LeadFrames = 8;

    SwapIntervalRule m_rule;
    FrameWorkRule m_frameWork;
    // The display's refreshes: vertical blank m_anchorSlot is at m_anchorTime. From the newest reading, and from the first frame's
    // start until there is one
    NanosecondTickCount m_anchorTime;
    int64_t m_anchorSlot{0};
    bool m_hasAnchor{false};
    bool m_hasReading{false};
    NanosecondTickCount m_lastReadTime;
    uint64_t m_vblankJumps{0};
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
    // How long the last frames took from their start to the end of their CPU work
    std::array<NanosecondTimeSpan, LeadFrames> m_leads{};
    std::size_t m_leadCount{0};
    RefreshTime m_animationTime;
    NanosecondTimeSpan m_lastAnimationTime;
    uint64_t m_refreshesBehindClock{0};
    NanosecondTimeDuration m_lastPresentBlocked;
    // The presents: the last one the system took, and the first that can still be waited for (0: none yet)
    uint64_t m_lastAcceptedId{0};
    uint64_t m_oldestWaitableId{1};
    uint64_t m_presentWaitTimeouts{0};
    // The present the wait before the next frame was made for: it is not asked for again
    uint64_t m_waitedForId{0};
    // The waits in a row that ran out (no more than it takes to stop waiting), and whether one held the loop before the frame
    // that is about to start
    uint32_t m_waitsRunOut{0};
    bool m_waitRanOut{false};
    // The present the first of those waits was for: one made before it was shown, and says nothing of the display now
    uint64_t m_runOutFromId{0};
    // While the waits are stopped: the frames since the pacer last asked after a present, and the answers in a row that said
    // shown
    uint32_t m_framesSinceAsk{0};
    uint32_t m_shownAsks{0};
    // A wait was reported since the last frame started: a frame has one wait for a present, not two
    bool m_waitReported{false};

  public:
    //! The waits in a row that run out before the pacer stops waiting, the frames between two asks while it is stopped, and the
    //! answers in a row that say shown before it waits again: as TimerWaitForPresentPacer's.
    static constexpr uint32_t WaitsRunOutToStop = 2;
    static constexpr uint32_t FramesBetweenAsks = 16;
    static constexpr uint32_t AsksShownToWait = 2;

    //! The tiers this pacer is for.
    static constexpr HoldTier Hold = HoldTier::VBlank;
    static constexpr QueueTier Queue = QueueTier::WaitForPresent;

    explicit VBlankWaitForPresentPacer(const PacerSettings& settings);

    //! Where the display's refreshes are: the time of a vertical blank of the display the window is on, a recent one or the next.
    //! Given whenever the application has one; the newest by its ReadTime counts. The frames go on from where they are.
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
    //! WaitsRunOutToStop of them in a row the pacer stops waiting (PresentWaitsStopped) until presents are shown again.
    void AddPresentWait(const PresentWaitReport& report) noexcept;

    //! The frame starts, at cpuStartTime: the previous frame is judged, the rule decides, and this frame is planned.
    FrameSchedule BeginFrame(NanosecondTickCount cpuStartTime) noexcept;

    //! The frame's CPU work is done, at workDoneTime: how to present it.
    PresentPlan EndFrame(NanosecondTickCount workDoneTime) noexcept;

    //! The frame's CPU busy time so far, at now, for a marker that is drawn while the frame's work is still going on: from the
    //! frame's start to now. Zero: no frame is open.
    [[nodiscard]] NanosecondTimeSpan32 CpuBusyAt(NanosecondTickCount now) const noexcept;

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

    //! True once a vertical blank reading was given for the display the pacer is on: until then the refreshes are a guess.
    [[nodiscard]] bool HasVBlankReading() const noexcept
    {
      return m_hasReading;
    }

    //! The readings that were more than an eighth of a refresh period off where the readings before them put the vertical
    //! blanks, since the pacer was made: a display that changed, or readings that are not exact.
    [[nodiscard]] uint64_t VBlankJumps() const noexcept
    {
      return m_vblankJumps;
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

    //! True while the pacer does not wait for presents, because its waits ran out: every FramesBetweenAsks frames the plan asks,
    //! with no time to wait, whether an older present was shown, and AsksShownToWait answers in a row that say shown end it.
    [[nodiscard]] bool PresentWaitsStopped() const noexcept
    {
      return m_waitsRunOut >= WaitsRunOutToStop;
    }

    //! The waits for a present that ended without the present being shown, since the pacer was made.
    [[nodiscard]] uint64_t PresentWaitTimeouts() const noexcept
    {
      return m_presentWaitTimeouts;
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
  };
}

#endif
