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
  //! (the present waits and the present returns at once; the same for the acquire), and its longest swap interval is at least 1
  //! with a present that takes one and 0 without. A set outside that is asserted, and without asserts made valid: a pair that
  //! excludes itself is taken out (the call may wait), bits without a meaning are dropped.
  class PacerCapabilities
  {
    PacerCapability m_capabilities{PacerCapability::NoCapabilities};
    uint32_t m_maxPresentSwapInterval{0};

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

    //! The capabilities, and with PresentSwapInterval the longest swap interval the present takes (at least 1; it has no meaning
    //! without that capability and is not kept).
    constexpr explicit PacerCapabilities(const PacerCapability capabilities, const uint32_t maxPresentSwapInterval = 1) noexcept
      : m_capabilities(Valid(capabilities))
      , m_maxPresentSwapInterval(HasCapability(capabilities, PacerCapability::PresentSwapInterval) ? std::max(maxPresentSwapInterval, 1u) : 0u)
    {
      assert(m_capabilities == capabilities);
      assert(!HasCapability(capabilities, PacerCapability::PresentSwapInterval) || maxPresentSwapInterval >= 1u);
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

    //! True when the set has every one of the capabilities.
    [[nodiscard]] constexpr bool Has(const PacerCapability capabilities) const noexcept
    {
      return HasCapability(m_capabilities, capabilities);
    }

    //! True when the set has everything the other set has: its capabilities, and a swap interval at least as long. The active set
    //! is one the set an application has contains.
    [[nodiscard]] constexpr bool Contains(const PacerCapabilities& other) const noexcept
    {
      return HasCapability(m_capabilities, other.m_capabilities) && m_maxPresentSwapInterval >= other.m_maxPresentSwapInterval;
    }

    //! The set without the capabilities: how an application or a test switches a mechanism off.
    [[nodiscard]] constexpr PacerCapabilities Without(const PacerCapability capabilities) const noexcept
    {
      return PacerCapabilities(Pacer::Without(m_capabilities, capabilities), std::max(m_maxPresentSwapInterval, 1u));
    }

    constexpr bool operator==(const PacerCapabilities& other) const noexcept = default;
  };
}

#endif
