#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERCAPABILITY_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERCAPABILITY_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: part of a redesign that is not built yet). What an application can do or
  //! can tell the pacer, in terms of no graphics API: one bit each, combined into the set an application has and the set that is
  //! active (PacerCapabilities). Every application has the baseline, which is no capability: a steady clock it reads, the display's
  //! refresh period, a wait until a time on that clock, and a present.
  enum class PacerCapability : uint32_t
  {
    //! No capability: the baseline (not "None": X11's headers define that name as a macro)
    NoCapabilities = 0,

    // How the present holds a frame

    //! The present takes a swap interval, from 1 to a maximum (PacerCapabilities::MaxPresentSwapInterval), and holds the frame for
    //! that many refreshes
    PresentSwapInterval = 1u << 0u,
    //! The present takes a time before which the frame is not shown
    PresentAtTime = 1u << 1u,
    //! The present takes a time the frame before it stays on screen at least. It places no frame by itself (it counts from
    //! wherever that frame was shown), so it makes no tier: it is given wherever it is active
    PresentAfterDuration = 1u << 2u,

    // What the calls do to the frame loop. A set has one of each pair or neither: with neither the call may wait, and the pacer goes
    // by what it measures.

    //! The present waits for the display: the platform documents that the call blocks until there is room
    PresentWaits = 1u << 3u,
    //! The present does not wait: it was asked to return at once, or the application waits for the display elsewhere
    PresentReturnsAtOnce = 1u << 4u,
    //! The call that gets the next image to draw into waits until one is free
    AcquireWaits = 1u << 5u,
    //! The call that gets the next image to draw into does not wait
    AcquireReturnsAtOnce = 1u << 6u,

    // Waits the application can make when the pacer asks for one

    //! A wait until a present it names was shown, with a timeout
    WaitForPresent = 1u << 7u,
    //! A wait until the GPU finished a frame it names, with a timeout
    WaitForGpuWork = 1u << 8u,
    //! A wait until an image of its swap chain is free to draw into, with a timeout
    WaitForImage = 1u << 9u,

    // What the application can tell the pacer

    //! When the window system called for a new frame
    FrameCallback = 1u << 10u,
    //! When a vertical blank was, and the period between them
    VBlankTimes = 1u << 11u,
    //! When the GPU began and ended its work on a frame, on the application's clock, frames later
    GpuWorkTimes = 1u << 12u,
    //! How long the GPU worked on a frame, frames later: how long, not when
    GpuWorkDurations = 1u << 13u,
    //! When a present was shown, or that it has a result without a time, frames later
    DisplayTimes = 1u << 14u,

    // What the display's side does with presents that carry a time (PresentAtTime). A fact of the platform or of the present
    // mode, not something a pacer switches

    //! Of two presents whose times have both passed, the later is shown and the earlier never: a frame that is overdue is
    //! left out. Without it every present is shown, in the order it was made, for a refresh at least. It is rated
    //! (PacerMajorTier::DisplayPlacesAndSkips) and no pacer is built for it: a pacer takes every frame as shown
    PresentSkipsOverdue = 1u << 15u,

    //! Every capability there is
    AllCapabilities = (1u << 16u) - 1u,
  };

  constexpr PacerCapability operator|(const PacerCapability lhs, const PacerCapability rhs) noexcept
  {
    return static_cast<PacerCapability>(static_cast<uint32_t>(lhs) | static_cast<uint32_t>(rhs));
  }

  constexpr PacerCapability operator&(const PacerCapability lhs, const PacerCapability rhs) noexcept
  {
    return static_cast<PacerCapability>(static_cast<uint32_t>(lhs) & static_cast<uint32_t>(rhs));
  }

  //! The capabilities of lhs that rhs does not have.
  constexpr PacerCapability Without(const PacerCapability lhs, const PacerCapability rhs) noexcept
  {
    return static_cast<PacerCapability>(static_cast<uint32_t>(lhs) & ~static_cast<uint32_t>(rhs));
  }

  //! True when every bit of capability is set in capabilities.
  constexpr bool HasCapability(const PacerCapability capabilities, const PacerCapability capability) noexcept
  {
    return (capabilities & capability) == capability;
  }
}

#endif
