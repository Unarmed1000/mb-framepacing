#ifndef MB_FRAMEPACING_PACER_PLACEMENT_DISPLAYPLACEMENTUTIL_HPP
#define MB_FRAMEPACING_PACER_PLACEMENT_DISPLAYPLACEMENTUTIL_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/placement/PresentTiming.hpp>
#include <cstdint>

//! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built; the simulation only, no system has
//! been measured with it). The values a timed present is given, which every tier with a timed present (PacerTier 1 to 4)
//! gives its presents. With a time before which a frame is not shown the display's side places the frame: it is shown at the
//! refresh it is for whenever before it the present is made. A time the frame before it stays on screen at least places
//! nothing by itself, as it counts from wherever that frame was shown: it keeps a frame from being shown a refresh early.
//! Functions of values alone.
namespace MB::FramePacing::Pacer::DisplayPlacementUtil
{
  //! A frame may be shown from this share of a refresh period before the refresh it is for: one in this many. Half a period is as
  //! far from the refresh before it as from its own, so neither a grid on the clock that is off the display's refreshes nor a
  //! refresh period that is a little off puts a frame on another refresh.
  inline constexpr int64_t EarlyDivisor = 2;

  //! The time a pacer gives its presents with the active capabilities. A time before which a frame is not shown is taken where
  //! the present has one: it says which refresh, whenever the frame arrives. A time the frame before it stays says only how
  //! long that frame is held, which at one refresh per frame is what a display that shows one frame per refresh does anyway.
  [[nodiscard]] constexpr PresentTiming TimingFor(const PacerCapabilities& active) noexcept
  {
    if (active.Has(PacerCapability::PresentAtTime))
    {
      return PresentTiming::AtTime;
    }
    return active.Has(PacerCapability::PresentAfterDuration) ? PresentTiming::AfterDuration : PresentTiming::Untimed;
  }

  //! How long before the refresh a frame is for it may be shown.
  [[nodiscard]] inline NanosecondTimeSpan EarlyTimeFor(const RefreshPeriod period) noexcept
  {
    return NanosecondTimeSpan(period.ToNanosecondTimeSpan().Nanoseconds() / EarlyDivisor);
  }

  //! PresentPlan::NotBeforeTime for a frame that is for the refresh at intendedDisplayTime.
  [[nodiscard]] inline NanosecondTickCount NotBeforeTimeFor(const NanosecondTickCount intendedDisplayTime, const RefreshPeriod period) noexcept
  {
    return intendedDisplayTime - EarlyTimeFor(period);
  }

  //! PresentPlan::MinimumDuration for a frame that is for the refresh swapInterval (1 or more) after the frame before it.
  [[nodiscard]] inline NanosecondTimeDuration MinimumDurationFor(const uint32_t swapInterval, const RefreshPeriod period) noexcept
  {
    // A swap interval is 1 or more, and a refresh period's time is far inside the range: neither negative nor out of range
    return NanosecondTimeDuration::FromNanoseconds(period.TimeFor(int64_t{swapInterval}).Nanoseconds() - EarlyTimeFor(period).Nanoseconds());
  }

  //! Fills the plan's value for the timing: the one, and nothing for a present that takes no time.
  inline void Place(PresentPlan& plan, const PresentTiming timing, const NanosecondTickCount intendedDisplayTime, const uint32_t swapInterval,
                    const RefreshPeriod period) noexcept
  {
    if (timing == PresentTiming::AtTime)
    {
      plan.NotBeforeTime = NotBeforeTimeFor(intendedDisplayTime, period);
    }
    else if (timing == PresentTiming::AfterDuration)
    {
      plan.MinimumDuration = MinimumDurationFor(swapInterval, period);
    }
  }
}

#endif
