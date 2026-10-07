#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERTIERTEXT_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERTIERTEXT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <cstdint>
#include <string_view>

//! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md). The tiers and the capabilities in words, for an application that shows
//! them: a short name for a label, and for a tier what its pacer uses and what that gives, as a sentence or two (DescriptionOf)
//! and as one short line (ShortDescriptionOf, at most ShortDescriptionMaxLength characters). English, plain ASCII. Every
//! text is a string literal: it is there for the life of the program, nothing is allocated, and its data() ends with a zero, so it
//! can be given to a function that takes a C string. A value that is not one of the type's has no text: empty.
//!
//! A tier's number is its enumerator's value, 1 the best: "tier 4 of 4" is the value of PacerTier::TimerPeriodOnly of TierCount.
namespace MB::FramePacing::Pacer::PacerTierText
{
  //! The tiers there are: their numbers are 1 to this.
  inline constexpr uint32_t TierCount = 4;
  //! The capabilities there are: capability number index is PacerCapability(1 << index), index from 0 to this less one.
  inline constexpr uint32_t CapabilityCount = 15;

  //! The longest a short description is, in characters: one line of a narrow panel.
  inline constexpr uint32_t ShortDescriptionMaxLength = 44;

  //! A tier's name.
  [[nodiscard]] constexpr std::string_view NameOf(const PacerTier tier) noexcept
  {
    switch (tier)
    {
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
    case PacerCapability::AllCapabilities:
      break;
    }
    return {};
  }
}

#endif
