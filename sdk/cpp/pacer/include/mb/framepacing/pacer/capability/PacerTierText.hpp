#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERTIERTEXT_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERTIERTEXT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/capability/HoldTier.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/QueueTier.hpp>
#include <cstdint>
#include <string_view>

//! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md). The tiers and the capabilities in words, for an application that shows
//! them: a short name for a label, and for a tier a line that says what it uses and what that gives. English, plain ASCII. Every
//! text is a string literal: it is there for the life of the program, nothing is allocated, and its data() ends with a zero, so it
//! can be given to a function that takes a C string. A value that is not one of the type's has no text: empty.
//!
//! A tier's number is its enumerator's value, 1 the best: "hold tier 3 of 3" is the value of HoldTier::Timer of HoldTierCount.
namespace MB::FramePacing::Pacer::PacerTierText
{
  //! The hold tiers there are: their numbers are 1 to this.
  inline constexpr uint32_t HoldTierCount = 3;
  //! The queue tiers there are: their numbers are 1 to this.
  inline constexpr uint32_t QueueTierCount = 3;
  //! The capabilities there are: capability number index is PacerCapability(1 << index), index from 0 to this less one.
  inline constexpr uint32_t CapabilityCount = 15;

  //! A hold tier's name.
  [[nodiscard]] constexpr std::string_view NameOf(const HoldTier tier) noexcept
  {
    switch (tier)
    {
    case HoldTier::DisplaySide:
      return "display side";
    case HoldTier::VBlank:
      return "vertical blank times";
    case HoldTier::Timer:
      return "timer";
    }
    return {};
  }

  //! What a hold tier uses to hold a frame for its swap interval, and what that gives.
  [[nodiscard]] constexpr std::string_view DescriptionOf(const HoldTier tier) noexcept
  {
    switch (tier)
    {
    case HoldTier::DisplaySide:
      return "The present holds the frame: it is given a time, a minimum duration or a swap interval, and the display side keeps "
             "the frame on screen for its refreshes.";
    case HoldTier::VBlank:
      return "The frame loop holds the frame and knows where the refreshes are from vertical blank times: it presents in the "
             "refresh before the one the frame is aimed at.";
    case HoldTier::Timer:
      return "The frame loop holds the frame on a timer and the refresh period alone: where the refreshes are is not known, so "
             "the moment of the present is a guess.";
    }
    return {};
  }

  //! A queue tier's name.
  [[nodiscard]] constexpr std::string_view NameOf(const QueueTier tier) noexcept
  {
    switch (tier)
    {
    case QueueTier::WaitForPresent:
      return "wait for a present";
    case QueueTier::DisplayTimes:
      return "display times";
    case QueueTier::PeriodOnly:
      return "refresh period only";
    }
    return {};
  }

  //! What a queue tier uses to keep the frames that wait to be shown few, and what that gives.
  [[nodiscard]] constexpr std::string_view DescriptionOf(const QueueTier tier) noexcept
  {
    switch (tier)
    {
    case QueueTier::WaitForPresent:
      return "Before a frame the loop waits until the display took an earlier present: the frames that wait to be shown stay as "
             "few as asked for, whatever happens.";
    case QueueTier::DisplayTimes:
      return "The presents not yet shown are counted from the display times reported frames later, and a frame start is taken "
             "back for each one too many.";
    case QueueTier::PeriodOnly:
      return "Never more frames than the display takes, from the refresh period alone: a frame that waits to be shown is not "
             "seen, and stays.";
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
