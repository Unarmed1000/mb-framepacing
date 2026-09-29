#ifndef MB_FRAMEMARKER_MARKERFLAGS_HPP
#define MB_FRAMEMARKER_MARKERFLAGS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FrameMarker
{
  //! The payload's flags byte. See doc/marker-format.md "Flags". Bits 1 to 7 are reserved: write 0; a decoded payload keeps whatever it
  //! carried, so values without a name here survive a round trip.
  enum class MarkerFlags : uint8_t
  {
    None = 0,
    //! Nothing animates in this frame (an idle screen, a paused menu with nothing moving): the analysis does not judge the animation
    //! error of a step from or to it.
    Static = 1u << 0u,
  };

  constexpr MarkerFlags operator|(const MarkerFlags lhs, const MarkerFlags rhs) noexcept
  {
    return static_cast<MarkerFlags>(static_cast<uint8_t>(static_cast<uint32_t>(lhs) | static_cast<uint32_t>(rhs)));
  }

  constexpr MarkerFlags operator&(const MarkerFlags lhs, const MarkerFlags rhs) noexcept
  {
    return static_cast<MarkerFlags>(static_cast<uint8_t>(static_cast<uint32_t>(lhs) & static_cast<uint32_t>(rhs)));
  }

  //! True when every bit of flag is set in flags.
  constexpr bool HasFlag(const MarkerFlags flags, const MarkerFlags flag) noexcept
  {
    return (flags & flag) == flag;
  }
}

#endif
