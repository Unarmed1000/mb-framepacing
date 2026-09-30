#ifndef MB_FRAMEPACING_MARKER_MARKERFLAGS_HPP
#define MB_FRAMEPACING_MARKER_MARKERFLAGS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Marker
{
  //! The payload's flags byte. See doc/marker-format.md "Flags". Bits 2 to 7 are reserved: write 0; a decoded payload keeps whatever it
  //! carried, so values without a name here survive a round trip.
  enum class MarkerFlags : uint8_t
  {
    None = 0,
    //! Nothing animates while this frame is on screen, until the next frame (the application has no pending work after it). Says nothing
    //! about whether this frame itself animated. The analysis does not judge the step from it to the next frame.
    StaticAfter = 1u << 0u,
    //! Nothing animated while the frame before this one was on screen: StaticAfter of the previous frame, for an application that only
    //! knows it once it renders this frame.
    StaticBefore = 1u << 1u,
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
