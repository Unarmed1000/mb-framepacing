#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERTIERTEXT_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERTIERTEXT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerMajorTier.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <cstdint>
#include <string_view>

//! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md). The tiers and the capabilities in words, for an application that shows
//! them: a short name for a label, and for a tier what its pacer uses and what that gives, as a sentence or two (DescriptionOf)
//! and as one short line (ShortDescriptionOf, at most ShortDescriptionMaxLength characters). English, plain ASCII. Every
//! text is a string literal: it is there for the life of the program, nothing is allocated, and its data() ends with a zero, so it
//! can be given to a function that takes a C string. A value that is not one of the type's has no text: empty.
//!
//! A tier is shown as its major tier and its sub tier (NumberOf): "1.1" is the best and "3.4" the baseline that every set
//! reaches. A rating that reports display times (PacerRating::ReportsDisplayTimes) is shown with DisplayTimesMark after it:
//! "3.1+".
namespace MB::FramePacing::Pacer::PacerTierText
{
  //! The tiers there are: PacerTier's values are 1 to this.
  inline constexpr uint32_t TierCount = 12;
  //! The major tiers there are: PacerMajorTier's values are 1 to this.
  inline constexpr uint32_t MajorTierCount = 3;
  //! What follows a tier's number when the application reports display times.
  inline constexpr std::string_view DisplayTimesMark = "+";
  //! The capabilities there are: capability number index is PacerCapability(1 << index), index from 0 to this less one.
  inline constexpr uint32_t CapabilityCount = 16;

  //! The longest a short description is, in characters: one line of a narrow panel.
  inline constexpr uint32_t ShortDescriptionMaxLength = 44;

  //! A tier as it is shown: its major tier, a point and its sub tier.
  [[nodiscard]] constexpr std::string_view NumberOf(const PacerTier tier) noexcept
  {
    switch (tier)
    {
    case PacerTier::TimedSkipVBlankWaitForPresent:
      return "1.1";
    case PacerTier::TimedSkipTimerWaitForPresent:
      return "1.2";
    case PacerTier::TimedSkipVBlankPeriodOnly:
      return "1.3";
    case PacerTier::TimedSkipTimerPeriodOnly:
      return "1.4";
    case PacerTier::TimedVBlankWaitForPresent:
      return "2.1";
    case PacerTier::TimedTimerWaitForPresent:
      return "2.2";
    case PacerTier::TimedVBlankPeriodOnly:
      return "2.3";
    case PacerTier::TimedTimerPeriodOnly:
      return "2.4";
    case PacerTier::VBlankWaitForPresent:
      return "3.1";
    case PacerTier::VBlankPeriodOnly:
      return "3.2";
    case PacerTier::TimerWaitForPresent:
      return "3.3";
    case PacerTier::TimerPeriodOnly:
      return "3.4";
    }
    return {};
  }

  //! A major tier's name.
  [[nodiscard]] constexpr std::string_view NameOf(const PacerMajorTier major) noexcept
  {
    switch (major)
    {
    case PacerMajorTier::DisplayPlacesAndSkips:
      return "the display places a frame and skips one that is overdue";
    case PacerMajorTier::DisplayPlaces:
      return "the display places a frame";
    case PacerMajorTier::LoopPlaces:
      return "the frame loop places a frame";
    }
    return {};
  }

  //! Who places a frame on its refresh in a major tier, and what that gives.
  [[nodiscard]] constexpr std::string_view DescriptionOf(const PacerMajorTier major) noexcept
  {
    switch (major)
    {
    case PacerMajorTier::DisplayPlacesAndSkips:
      return "The present takes a time before which the frame is not shown, and of two frames that are both due the display's "
             "side shows the later and never the earlier. Every frame is shown at the refresh it is for whenever its present is "
             "made, after a frame that came late the frame on screen is the one made for that refresh, and frames can not pile "
             "up. No pacer is built for it yet: such a set is paced as the major tier below, which takes every frame as shown.";
    case PacerMajorTier::DisplayPlaces:
      return "The present takes a time before which the frame is not shown, and the display's side shows every frame in the "
             "order it was presented. Every frame is shown at the refresh it is for whenever its present is made, so where in a "
             "refresh the present call lands does not matter.";
    case PacerMajorTier::LoopPlaces:
      return "The present takes no time before which the frame is not shown, so the frame loop places a frame: it has to make "
             "the present at the right moment in the refresh before the one the frame is for.";
    }
    return {};
  }

  //! A tier's name.
  [[nodiscard]] constexpr std::string_view NameOf(const PacerTier tier) noexcept
  {
    switch (tier)
    {
    case PacerTier::TimedSkipVBlankWaitForPresent:
      return "timed present that skips, vertical blank times, wait for a present";
    case PacerTier::TimedSkipTimerWaitForPresent:
      return "timed present that skips, wait for a present";
    case PacerTier::TimedSkipVBlankPeriodOnly:
      return "timed present that skips, vertical blank times";
    case PacerTier::TimedSkipTimerPeriodOnly:
      return "timed present that skips";
    case PacerTier::TimedVBlankWaitForPresent:
      return "timed present, vertical blank times, wait for a present";
    case PacerTier::TimedTimerWaitForPresent:
      return "timed present, wait for a present";
    case PacerTier::TimedVBlankPeriodOnly:
      return "timed present, vertical blank times";
    case PacerTier::TimedTimerPeriodOnly:
      return "timed present";
    case PacerTier::VBlankWaitForPresent:
      return "vertical blank times, wait for a present";
    case PacerTier::VBlankPeriodOnly:
      return "vertical blank times";
    case PacerTier::TimerWaitForPresent:
      return "timer, wait for a present";
    case PacerTier::TimerPeriodOnly:
      return "timer";
    }
    return {};
  }

  //! What a tier's pacer uses, and what that gives.
  [[nodiscard]] constexpr std::string_view DescriptionOf(const PacerTier tier) noexcept
  {
    switch (tier)
    {
    case PacerTier::TimedSkipVBlankWaitForPresent:
      return "The present takes a time, and of two frames that are both due the display's side shows the later and never the "
             "earlier: every frame is shown at the refresh it is for, and one that is overdue is left out. The time is that of a "
             "real refresh, from vertical blank times, and before a frame the loop waits until the display took an earlier "
             "present. No pacer is built for it yet: the set is paced as tier 2.1, which takes every frame as shown.";
    case PacerTier::TimedSkipTimerWaitForPresent:
      return "The present takes a time, and of two frames that are both due the display's side shows the later and never the "
             "earlier: every frame is shown at the refresh it is for, counted in refresh periods on the clock, and one that is "
             "overdue is left out. Before a frame the loop waits until the display took an earlier present. No pacer is built "
             "for it yet: the set is paced as tier 2.2, which takes every frame as shown.";
    case PacerTier::TimedSkipVBlankPeriodOnly:
      return "The present takes a time, and of two frames that are both due the display's side shows the later and never the "
             "earlier: every frame is shown at the refresh it is for, and one that is overdue is left out, so no frame stays "
             "waiting. The time is that of a real refresh, from vertical blank times. No pacer is built for it yet: the set is "
             "paced as tier 2.3, which takes every frame as shown.";
    case PacerTier::TimedSkipTimerPeriodOnly:
      return "The present takes a time, and of two frames that are both due the display's side shows the later and never the "
             "earlier: every frame is shown at the refresh it is for, counted in refresh periods on the clock, and one that is "
             "overdue is left out, so no frame stays waiting. No pacer is built for it yet: the set is paced as tier 2.4, which "
             "takes every frame as shown.";
    case PacerTier::TimedVBlankWaitForPresent:
      return "The present takes a time, so the display's side shows every frame at the refresh it is for. The time is that of a "
             "real refresh, from vertical blank times, and before a frame the loop waits until the display took an earlier "
             "present: the frames that wait to be shown stay as few as asked for, and the pacer learns which refresh a frame "
             "was shown at.";
    case PacerTier::TimedTimerWaitForPresent:
      return "The present takes a time, so the display's side shows every frame at the refresh it is for, counted in refresh "
             "periods on the clock. Before a frame the loop waits until the display took an earlier present: the frames that "
             "wait to be shown stay as few as asked for. Where in a refresh the loop is, is not known.";
    case PacerTier::TimedVBlankPeriodOnly:
      return "The present takes a time, so the display's side shows every frame at the refresh it is for. The time is that of a "
             "real refresh, from vertical blank times, and nothing drifts. A frame that waits to be shown although it was "
             "ready in time is not seen, and stays.";
    case PacerTier::TimedTimerPeriodOnly:
      return "The present takes a time, so the display's side shows every frame at the refresh it is for, counted in refresh "
             "periods on the clock. Where in a refresh the loop is, is not known, and a frame that waits to be shown is not "
             "seen, and stays.";
    case PacerTier::VBlankWaitForPresent:
      return "The pacer knows where the display's refreshes are from vertical blank times, and before a frame the loop waits "
             "until the display took an earlier present: every frame is for one refresh, the frames that wait to be shown stay "
             "as few as asked for, and the pacer learns which refresh a frame was shown at.";
    case PacerTier::VBlankPeriodOnly:
      return "The pacer knows where the display's refreshes are from vertical blank times: every frame is for one refresh and "
             "nothing drifts. A frame that waits to be shown although it was ready in time is not seen, and stays.";
    case PacerTier::TimerWaitForPresent:
      return "The frame loop is paced on a timer and the refresh period, and before a frame it waits until the display took an "
             "earlier present: the frames that wait to be shown stay as few as asked for, and the timer follows the waits.";
    case PacerTier::TimerPeriodOnly:
      return "The frame loop is paced on a timer and the refresh period alone: where the refreshes are is not known, so the "
             "moment of a present is a guess, and a frame that waits to be shown is not seen, and stays.";
    }
    return {};
  }

  //! The same in one short line.
  [[nodiscard]] constexpr std::string_view ShortDescriptionOf(const PacerTier tier) noexcept
  {
    switch (tier)
    {
    case PacerTier::TimedSkipVBlankWaitForPresent:
      return "Timed, skips overdue: real refreshes, wait.";
    case PacerTier::TimedSkipTimerWaitForPresent:
      return "Timed, skips overdue: a clock grid, wait.";
    case PacerTier::TimedSkipVBlankPeriodOnly:
      return "Timed, skips overdue: on real refreshes.";
    case PacerTier::TimedSkipTimerPeriodOnly:
      return "Timed, skips overdue: on a clock grid.";
    case PacerTier::TimedVBlankWaitForPresent:
      return "Timed present on real refreshes, and a wait.";
    case PacerTier::TimedTimerWaitForPresent:
      return "Timed present on a clock grid, and a wait.";
    case PacerTier::TimedVBlankPeriodOnly:
      return "Timed present on real refreshes; no wait.";
    case PacerTier::TimedTimerPeriodOnly:
      return "Timed present on a clock grid; no wait.";
    case PacerTier::VBlankWaitForPresent:
      return "On the refreshes; knows what was shown.";
    case PacerTier::VBlankPeriodOnly:
      return "On the display's refreshes; no drift.";
    case PacerTier::TimerWaitForPresent:
      return "A timer; waits until a present was shown.";
    case PacerTier::TimerPeriodOnly:
      return "A timer and the refresh period: a guess.";
    }
    return {};
  }

  //! A capability's name: of one capability. No capability at all is the baseline; a set of several has no name.
  [[nodiscard]] constexpr std::string_view NameOf(const PacerCapability capability) noexcept
  {
    switch (capability)
    {
    case PacerCapability::NoCapabilities:
      return "baseline";
    case PacerCapability::PresentSwapInterval:
      return "present with a swap interval";
    case PacerCapability::PresentAtTime:
      return "present at a time";
    case PacerCapability::PresentAfterDuration:
      return "present after a duration";
    case PacerCapability::PresentWaits:
      return "present waits";
    case PacerCapability::PresentReturnsAtOnce:
      return "present returns at once";
    case PacerCapability::AcquireWaits:
      return "acquire waits";
    case PacerCapability::AcquireReturnsAtOnce:
      return "acquire returns at once";
    case PacerCapability::WaitForPresent:
      return "wait for a present";
    case PacerCapability::WaitForGpuWork:
      return "wait for the GPU's work";
    case PacerCapability::WaitForImage:
      return "wait for an image";
    case PacerCapability::FrameCallback:
      return "frame callback";
    case PacerCapability::VBlankTimes:
      return "vertical blank times";
    case PacerCapability::GpuWorkTimes:
      return "GPU work times";
    case PacerCapability::GpuWorkDurations:
      return "GPU work durations";
    case PacerCapability::DisplayTimes:
      return "display times";
    case PacerCapability::PresentSkipsOverdue:
      return "present skips an overdue frame";
    case PacerCapability::AllCapabilities:
      break;
    }
    return {};
  }
}

#endif
