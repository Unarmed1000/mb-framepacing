#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERCAPABILITIES_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERCAPABILITIES_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <algorithm>
#include <cassert>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: part of a redesign that is not built yet). A set of capabilities: what
  //! an application has, or what is active now. Always valid: a set never has both capabilities of a pair that excludes itself
  //! (the present waits and the present returns at once; the same for the acquire), and with a present that takes a swap
  //! interval its shortest and longest are 1 <= shortest <= longest (both 0 without one). A set outside that is asserted, and
  //! without asserts made valid: a pair that excludes itself is taken out (the call may wait), bits without a meaning are dropped,
  //! a longest swap interval of 0 is 1 and a shortest one above the longest is the longest.
  class PacerCapabilities
  {
    PacerCapability m_capabilities{PacerCapability::NoCapabilities};
    uint32_t m_maxPresentSwapInterval{0};
    uint32_t m_minPresentSwapInterval{0};

    static constexpr PacerCapability Valid(const PacerCapability capabilities) noexcept
    {
      PacerCapability valid = capabilities & PacerCapability::AllCapabilities;
      for (const PacerCapability pair : {PacerCapability::PresentWaits | PacerCapability::PresentReturnsAtOnce,
                                         PacerCapability::AcquireWaits | PacerCapability::AcquireReturnsAtOnce})
      {
        if (HasCapability(valid, pair))
        {
          valid = Pacer::Without(valid, pair);
        }
      }
      return valid;
    }

  public:
    //! The baseline: no capability.
    constexpr PacerCapabilities() noexcept = default;

    //! The capabilities, and with PresentSwapInterval the longest swap interval the present takes (at least 1) and the shortest
    //! (EGL_MIN_SWAP_INTERVAL, say: at most the longest; 0 is taken as 1, as the pacer never asks for a present that does not
    //! wait for a refresh). Both have no meaning without that capability and are not kept.
    constexpr explicit PacerCapabilities(const PacerCapability capabilities, const uint32_t maxPresentSwapInterval = 1,
                                         const uint32_t minPresentSwapInterval = 1) noexcept
      : m_capabilities(Valid(capabilities))
      , m_maxPresentSwapInterval(HasCapability(capabilities, PacerCapability::PresentSwapInterval) ? std::max(maxPresentSwapInterval, 1u) : 0u)
      , m_minPresentSwapInterval(HasCapability(capabilities, PacerCapability::PresentSwapInterval)
                                   ? std::min(std::max(minPresentSwapInterval, 1u), std::max(maxPresentSwapInterval, 1u))
                                   : 0u)
    {
      assert(m_capabilities == capabilities);
      assert(!HasCapability(capabilities, PacerCapability::PresentSwapInterval) || maxPresentSwapInterval >= 1u);
      assert(!HasCapability(capabilities, PacerCapability::PresentSwapInterval) || minPresentSwapInterval <= maxPresentSwapInterval);
    }

    [[nodiscard]] constexpr PacerCapability Capabilities() const noexcept
    {
      return m_capabilities;
    }

    //! The longest swap interval the present takes: 0 without PresentSwapInterval.
    [[nodiscard]] constexpr uint32_t MaxPresentSwapInterval() const noexcept
    {
      return m_maxPresentSwapInterval;
    }

    //! The shortest swap interval the present takes: 0 without PresentSwapInterval, else 1 to MaxPresentSwapInterval. The pacer
    //! never paces faster than it.
    [[nodiscard]] constexpr uint32_t MinPresentSwapInterval() const noexcept
    {
      return m_minPresentSwapInterval;
    }

    //! True when the set has every one of the capabilities.
    [[nodiscard]] constexpr bool Has(const PacerCapability capabilities) const noexcept
    {
      return HasCapability(m_capabilities, capabilities);
    }

    //! True when the set has everything the other set has: its capabilities, a longest swap interval at least as long and a
    //! shortest one no longer. The active set is one the set an application has contains: it never goes below the system's
    //! shortest swap interval.
    [[nodiscard]] constexpr bool Contains(const PacerCapabilities& other) const noexcept
    {
      return HasCapability(m_capabilities, other.m_capabilities) && m_maxPresentSwapInterval >= other.m_maxPresentSwapInterval &&
             (!other.Has(PacerCapability::PresentSwapInterval) || m_minPresentSwapInterval <= other.m_minPresentSwapInterval);
    }

    //! The set without the capabilities: how an application or a test switches a mechanism off.
    [[nodiscard]] constexpr PacerCapabilities Without(const PacerCapability capabilities) const noexcept
    {
      return PacerCapabilities(Pacer::Without(m_capabilities, capabilities), std::max(m_maxPresentSwapInterval, 1u),
                               std::max(m_minPresentSwapInterval, 1u));
    }

    //! What both sets have: the capabilities of both, the shorter of the two longest swap intervals and the longer of the two
    //! shortest, at most that longest. What is left of an active set when the set an application has gets smaller.
    [[nodiscard]] constexpr PacerCapabilities IntersectedWith(const PacerCapabilities& other) const noexcept
    {
      const uint32_t maxSwapInterval = std::max(std::min(m_maxPresentSwapInterval, other.m_maxPresentSwapInterval), 1u);
      const uint32_t minSwapInterval = std::min(std::max(m_minPresentSwapInterval, other.m_minPresentSwapInterval), maxSwapInterval);
      return PacerCapabilities(m_capabilities & other.m_capabilities, maxSwapInterval, minSwapInterval);
    }

    constexpr bool operator==(const PacerCapabilities& other) const noexcept
    {
      return m_capabilities == other.m_capabilities && m_maxPresentSwapInterval == other.m_maxPresentSwapInterval &&
             m_minPresentSwapInterval == other.m_minPresentSwapInterval;
    }

    constexpr bool operator!=(const PacerCapabilities& other) const noexcept
    {
      return !(*this == other);
    }
  };
}

#endif
