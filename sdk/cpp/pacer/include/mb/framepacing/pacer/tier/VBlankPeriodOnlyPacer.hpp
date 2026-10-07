#ifndef MB_FRAMEPACING_PACER_TIER_VBLANKPERIODONLYPACER_HPP
#define MB_FRAMEPACING_PACER_TIER_VBLANKPERIODONLYPACER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeDuration.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
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
  //! the pacer the library has until they replace it). The pacer of HoldTier::VBlank with QueueTier::PeriodOnly: a whole pacer by
  //! itself, for an application that has the baseline (a steady clock, the refresh period of the display its window is on, a wait
  //! until a time, a present that shows every frame in order for at least a refresh) and is told when that display's vertical
  //! blanks are (PacerCapability::VBlankTimes).
  //!
  //! It holds every rule and every time calculation, and the application carries out what it is given. Its calls a frame:
  //!   AddVBlank   a vertical blank's time, whenever the application has one: where the display's refreshes are
  //!   PlanFrame   before the frame takes anything: the time to wait until
  //!   BeginFrame  the frame starts: its swap interval, its animation time and the marker's values
  //!   EndFrame    the CPU's work is done: the time to wait until before the present
  //!   AddPresent  after the present, before the next frame is planned: when it was called and returned, and whether the
  //!               system took it
  //! and, where the application has it, AddGpuWork: the GPU's work on an earlier frame.
  //!
  //! It knows where the refreshes are, so it does not guess. Every frame is for one vertical blank, the one it is to be shown at,
  //! and that blank follows from the frame before it: its swap interval later. A frame is shown at a blank when it is ready the
  //! frame margin before it (PacerSettings::FrameMargin); ready is presented, and with GPU work reports the GPU done with it. The
  //! pacer aims a frame to be ready at PacerSettings::ReadyPlacePercent of the refresh before its blank. The grid follows every
  //! reading, so a refresh period that is a little off the display's does not add up, which it does for a pacer on a timer.
  //!
  //! When a frame is made is its aim (PacerSettings::Aim), and it has both.
  //!
  //! PacerAim::Smoothness, the default: a frame starts at once and is made early, and its present is held until the time that
  //! has it ready at its place. Work that runs longer than usual uses up the time the frame would have waited. At one refresh per
  //! frame it also keeps a reserve: PacerSettings::WaitingPresents less one frames are ready that many refreshes before they are
  //! shown and wait, so a frame may be that many refreshes late and is still shown at its blank, and the frames after it are made
  //! back to back until the reserve is there again.
  //!
  //! PacerAim::LowLatency: a frame's start is held so that it is ready at its place and no sooner, from how long the frames
  //! before it took (the longest of the last eight but no longer than its swap interval's time, and the GPU time where it is
  //! reported), and it is presented when it is done, though never before the refresh it is to be ready in begins. The frame
  //! shown is as new as it can be, and a frame that takes longer than the ones before it can miss its blank. For the frames
  //! that pile up behind the first presents of a new swap chain it pauses once, as TimerPeriodOnlyPacer does.
  //!
  //! What it knows before a frame is in the frame: a frame that starts too late for its blank is for the first blank it can be
  //! ready for, and its animation step is the refreshes from the frame before it to that blank, so its animation time is the
  //! time it is shown. What it learns only after a frame (its own work ran long and it missed its blank) is not caught up with:
  //! the frame after it is its swap interval later and its animation step is its swap interval, and RefreshesBehindClock() counts
  //! those refreshes.
  //!
  //! What it does not do: learn of a frame that waits to be shown although it was ready in time. It sees the display's refreshes
  //! and not what the display shows.
  //!
  //! Until the first vertical blank reading it takes the first frame's start as a vertical blank: a pacer on a timer, by the
  //! same rules. A reading's own refresh period is not used yet: the period is the settings'.
  //!
  //! Values in, values out: no platform API, no clock read, no wait. Made once (it allocates the rule's frame window); pacing
  //! frames never allocates.
  class VBlankPeriodOnlyPacer
  {
    //! The frames whose time from start to present the low latency aim holds a frame's start by
    static constexpr std::size_t LeadFrames = 8;

    SwapIntervalRule m_rule;
    FrameWorkRule m_frameWork;
    // The display's refreshes: vertical blank m_anchorSlot is at m_anchorTime. From the newest reading, and from the first frame's
    // start until there is one
    TickCount64 m_anchorTime;
    int64_t m_anchorSlot{0};
    bool m_hasAnchor{false};
    bool m_hasReading{false};
    TickCount64 m_lastReadTime;
    uint64_t m_vblankJumps{0};
    // The frame between BeginFrame and the next BeginFrame: the vertical blank it is for, and the one it is shown at as far as
    // that is known (later than the one it is for once its present says so)
    bool m_hasFrame{false};
    uint64_t m_frameId{0};
    TickCount64 m_startTime;
    TickCount64 m_plannedStartTime;
    uint32_t m_swapInterval{1};
    int64_t m_displaySlot{0};
    bool m_startedLate{false};
    TimeSpan m_work;
    bool m_frameOpen{false};
    bool m_frameEnded{false};
    TickCount64 m_presentTime;
    bool m_hasPresentTime{false};
    // How long the last frames took from their start to the end of their CPU work
    std::array<TimeSpan, LeadFrames> m_leads{};
    std::size_t m_leadCount{0};
    RefreshTime m_animationTime;
    TimeSpan m_lastAnimationTime;
    uint64_t m_refreshesBehindClock{0};
    TimeDuration m_lastPresentBlocked;
    // The pause after start-up (low latency): still to be made, the first frame's start since it was asked for, whether the
    // system took a present since, and the refreshes the next frame is later by when it is made
    bool m_pausePending{true};
    bool m_pauseHasFirstFrame{false};
    TickCount64 m_pauseFirstFrameTime;
    bool m_presentTaken{false};
    int64_t m_pauseSlots{0};
    uint64_t m_startupPauses{0};

  public:
    //! The tiers this pacer is for.
    static constexpr HoldTier Hold = HoldTier::VBlank;
    static constexpr QueueTier Queue = QueueTier::PeriodOnly;

    explicit VBlankPeriodOnlyPacer(const PacerSettings& settings);

    //! Where the display's refreshes are: the time of a vertical blank of the display the window is on, a recent one or the next.
    //! Given whenever the application has one; the newest by its ReadTime counts. The frames go on from where they are.
    void AddVBlank(const VBlankReading& reading) noexcept;

    //! Before a frame takes anything, at now on the application's steady clock: the time to wait until before the frame starts,
    //! with the aim of low latency and when that is still to come. It changes nothing, so a frame may be planned again.
    [[nodiscard]] FrameStartPlan PlanFrame(TickCount64 now) const noexcept;

    //! The frame starts, at cpuStartTime: the previous frame is judged, the rule decides, and this frame is planned.
    FrameSchedule BeginFrame(TickCount64 cpuStartTime) noexcept;

    //! The frame's CPU work is done, at workDoneTime: how to present it.
    PresentPlan EndFrame(TickCount64 workDoneTime) noexcept;

    //! The frame's CPU busy time so far, at now, for a marker that is drawn while the frame's work is still going on: from the
    //! frame's start to now. Zero: no frame is open, or it does not fit the marker's field.
    [[nodiscard]] TimeSpan32 CpuBusyAt(TickCount64 now) const noexcept;

    //! After the present, before the next frame is planned: when it was called says which vertical blank the frame is shown at
    //! (without the report it is taken as made when EndFrame said). A present the system did not take says the swap chain is
    //! gone: the one made after it gets the pause of a start (ForgetPresents).
    void AddPresent(const PresentReport& report) noexcept;

    //! The GPU's work on an earlier frame, when the application has it: from then on a frame is ready when the GPU is done with
    //! it, and a frame's work is the CPU's and the GPU's (FrameWorkRule).
    void AddGpuWork(const GpuWorkReport& report) noexcept;

    //! The presents made so far are gone (a swap chain was made anew): with the aim of low latency the pause after start-up is
    //! made once more, counted from the next frame. Nothing else changes.
    void ForgetPresents() noexcept;

    //! The display's refresh period changed (a mode change, the window on another display): the frames start again on it with an
    //! empty frame window, at the swap interval the application prefers there, and the vertical blank readings from before are
    //! not of this display. The animation time goes on.
    void SetRefreshPeriod(RefreshPeriod period) noexcept;

    //! Other settings on a live pacer: it starts again with them. The same settings change nothing. Allocates when the frame
    //! window needs more room than it has, and only then.
    void SetSettings(const PacerSettings& settings);

    //! Start again (after a pause the application knows of): the next frame is for the first vertical blank it can be ready for,
    //! the frame window is empty, the swap interval the preferred one, the GPU's work is forgotten, and the pause after start-up
    //! is made once more. Where the refreshes are is kept, and the animation time goes on.
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

    //! The pauses after start-up that were made, since the pacer was made.
    [[nodiscard]] uint64_t StartupPauses() const noexcept
    {
      return m_startupPauses;
    }

    //! The GPU time a frame is judged with: the newest that was reported, zero without one.
    [[nodiscard]] TimeDuration GpuTime() const noexcept
    {
      return m_frameWork.GpuTime();
    }

    //! How long the last present that was reported held the frame loop.
    [[nodiscard]] TimeDuration LastPresentBlocked() const noexcept
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
    [[nodiscard]] bool StartsAgainAt(TickCount64 time) const noexcept;
    [[nodiscard]] TickCount64 TimeOfBlank(int64_t slot) const noexcept;
    [[nodiscard]] int64_t BlankAtOrBefore(TickCount64 time) const noexcept;
    [[nodiscard]] int64_t FirstBlankAfterReadyAt(TickCount64 readyTime) const noexcept;
    [[nodiscard]] int64_t Reserve() const noexcept;
    [[nodiscard]] TimeSpan ReadyPlace() const noexcept;
    [[nodiscard]] TimeSpan GpuLead() const noexcept;
    [[nodiscard]] TimeSpan ShortestLead() const noexcept;
    [[nodiscard]] TimeSpan LongestLead() const noexcept;
    [[nodiscard]] int64_t ShownSlot() const noexcept;
    [[nodiscard]] int64_t DisplaySlotFor(TickCount64 startTime) const noexcept;
    [[nodiscard]] TickCount64 StartTimeFor(int64_t displaySlot) const noexcept;
    [[nodiscard]] TickCount64 PresentTimeFor(int64_t displaySlot) const noexcept;
    void ArmStartupPause() noexcept;
    [[nodiscard]] int64_t StartupPauseAt(TickCount64 cpuStartTime) noexcept;
  };
}

#endif
