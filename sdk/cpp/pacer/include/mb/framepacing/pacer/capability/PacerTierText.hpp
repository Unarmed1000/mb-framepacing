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
//! reaches. A rating that reports display times (PacerRating::ReportsDisplayTimes) is shown with DisplayTimesMark after it, and
//! one whose display skips a frame that is overdue (PacerRating::SkipsOverdue) with SkipsOverdueMark after that: "3.1+",
//! "1.2+s", "3.1s".
namespace MB::FramePacing::Pacer::PacerTierText
{
  //! The tiers there are: PacerTier's values are 1 to this.
  inline constexpr uint32_t TierCount = 12;
  //! The major tiers there are: PacerMajorTier's values are 1 to this.
  inline constexpr uint32_t MajorTierCount = 3;
  //! What follows a tier's number when the application reports display times.
  inline constexpr std::string_view DisplayTimesMark = "+";
  //! What follows a tier's number (and DisplayTimesMark) when the display's side skips a frame that is overdue.
  inline constexpr std::string_view SkipsOverdueMark = "s";
  //! The capabilities there are: capability number index is PacerCapability(1 << index), index from 0 to this less one.
  inline constexpr uint32_t CapabilityCount = 16;

  //! The longest a short description is, in characters: one line of a narrow panel.
  inline constexpr uint32_t ShortDescriptionMaxLength = 44;

  //! What is shown in place of the tier that is pacing while the application is not pacing (TierPacer::IsPacing is false):
  //! a name for a label, a sentence, and one short line.
  inline constexpr std::string_view NotPacingName = "Not pacing";
  inline constexpr std::string_view NotPacingDescription =
    "The application is not pacing its frames. The pacer gives them nothing to wait for and takes what is reported, so that "
    "pacing starts again with a history.";
  inline constexpr std::string_view NotPacingShortDescription = "Frames are not paced; reports are taken";

  //! A tier as it is shown: its major tier, a point and its sub tier.
  [[nodiscard]] constexpr std::string_view NumberOf(const PacerTier tier) noexcept
  {
    switch (tier)
    {
    case PacerTier::AtTimeVBlankWaitForPresent:
      return "1.1";
    case PacerTier::AtTimeVBlankPeriodOnly:
      return "1.2";
    case PacerTier::AtTimeTimerWaitForPresent:
      return "1.3";
    case PacerTier::AtTimeTimerPeriodOnly:
      return "1.4";
    case PacerTier::AfterDurationVBlankWaitForPresent:
      return "2.1";
    case PacerTier::AfterDurationVBlankDisplayTimes:
      return "2.2";
    case PacerTier::AfterDurationTimerWaitForPresent:
      return "2.3";
    case PacerTier::AfterDurationTimerDisplayTimes:
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
    case PacerMajorTier::DisplayPlacesAtTime:
      return "the display places a frame at a time";
    case PacerMajorTier::DisplayPlacesAfterDuration:
      return "the display places a frame after the one before";
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
    case PacerMajorTier::DisplayPlacesAtTime:
      return "The present takes a time before which the frame is not shown, so the display's side places every frame against a "
             "clock: it is shown at the refresh it is for whenever its present is made, and where in a refresh the present call "
             "lands does not matter.";
    case PacerMajorTier::DisplayPlacesAfterDuration:
      return "The present takes how long the frame before it stays on screen (or a swap interval of two or more, the same in "
             "whole refreshes), so the display's side places every frame relative to the one before, and the loop may present "
             "early. After a late frame every later duration counts from it: the display stays smooth, and the pacer has to see "
             "where frames were shown, from a wait for a present or display times, or the latency creeps up. With the aim of "
             "low latency it starts the frames later when it sees that.";
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
    case PacerTier::AtTimeVBlankWaitForPresent:
      return "time on the present, vertical blank times, wait for a present";
    case PacerTier::AtTimeTimerWaitForPresent:
      return "time on the present, wait for a present";
    case PacerTier::AtTimeVBlankPeriodOnly:
      return "time on the present, vertical blank times";
    case PacerTier::AtTimeTimerPeriodOnly:
      return "time on the present";
    case PacerTier::AfterDurationVBlankWaitForPresent:
      return "duration on the present, vertical blank times, wait for a present";
    case PacerTier::AfterDurationTimerWaitForPresent:
      return "duration on the present, wait for a present";
    case PacerTier::AfterDurationVBlankDisplayTimes:
      return "duration on the present, vertical blank times, display times";
    case PacerTier::AfterDurationTimerDisplayTimes:
      return "duration on the present, display times";
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
    case PacerTier::AtTimeVBlankWaitForPresent:
      return "The present takes a time, so the display's side shows every frame at the refresh it is for. The time is that of "
             "a real refresh, from vertical blank times, and before a frame the loop waits until the display took an earlier "
             "present: the frames that wait to be shown stay as few as asked for, and the pacer learns which refresh a frame "
             "was shown at.";
    case PacerTier::AtTimeTimerWaitForPresent:
      return "The present takes a time, so the display's side shows every frame at the refresh it is for, counted in refresh "
             "periods on the clock. Before a frame the loop waits until the display took an earlier present: the frames that "
             "wait to be shown stay as few as asked for. Where in a refresh the loop is, is not known.";
    case PacerTier::AtTimeVBlankPeriodOnly:
      return "The present takes a time, so the display's side shows every frame at the refresh it is for. The time is that of "
             "a real refresh, from vertical blank times, and nothing drifts. A frame that waits to be shown although it was "
             "ready in time is not seen, and stays.";
    case PacerTier::AtTimeTimerPeriodOnly:
      return "The present takes a time, so the display's side shows every frame at the refresh it is for, counted in refresh "
             "periods on the clock. Where in a refresh the loop is, is not known, and a frame that waits to be shown is not "
             "seen, and stays.";
    case PacerTier::AfterDurationVBlankWaitForPresent:
      return "The present takes how long the frame before it stays, so the display's side shows every frame that long after "
             "the one before. The refreshes are known from vertical blank times, and before a frame the loop waits until the "
             "display took an earlier present, which keeps the frames that wait to be shown as few as asked for after a late "
             "frame moved the ones after it.";
    case PacerTier::AfterDurationTimerWaitForPresent:
      return "The present takes how long the frame before it stays, so the display's side shows every frame that long after "
             "the one before, counted in refresh periods on the clock. Before a frame the loop waits until the display took an "
             "earlier present, which keeps the frames that wait to be shown as few as asked for after a late frame moved the "
             "ones after it.";
    case PacerTier::AfterDurationVBlankDisplayTimes:
      return "The present takes how long the frame before it stays, so the display's side shows every frame that long after "
             "the one before. The refreshes are known from vertical blank times, and the application reports when its frames "
             "were shown, so the pacer sees a late frame move the ones after it: with the aim of low latency it starts the "
             "frames that much later.";
    case PacerTier::AfterDurationTimerDisplayTimes:
      return "The present takes how long the frame before it stays, so the display's side shows every frame that long after "
             "the one before, counted in refresh periods on the clock. The application reports when its frames were shown, so "
             "the pacer sees a late frame move the ones after it: with the aim of low latency it starts the frames that much "
             "later.";
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
    case PacerTier::AtTimeVBlankWaitForPresent:
      return "Present time, real refreshes, and a wait.";
    case PacerTier::AtTimeTimerWaitForPresent:
      return "Present time, a clock grid, and a wait.";
    case PacerTier::AtTimeVBlankPeriodOnly:
      return "Present time on real refreshes; no wait.";
    case PacerTier::AtTimeTimerPeriodOnly:
      return "Present time on a clock grid; no wait.";
    case PacerTier::AfterDurationVBlankWaitForPresent:
      return "Duration on the present, refreshes, wait.";
    case PacerTier::AfterDurationTimerWaitForPresent:
      return "Duration on the present, a clock, wait.";
    case PacerTier::AfterDurationVBlankDisplayTimes:
      return "Duration on the present, refreshes, reports.";
    case PacerTier::AfterDurationTimerDisplayTimes:
      return "Duration on the present, a clock, reports.";
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
      return "display skips an overdue frame";
    case PacerCapability::AllCapabilities:
      break;
    }
    return {};
  }
}

#endif
