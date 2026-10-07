#ifndef MB_FRAMEPACING_PACER_DETAIL_MARKERVALUE_HPP
#define MB_FRAMEPACING_PACER_DETAIL_MARKERVALUE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the pacer module: a length of time as one of the marker's 32-bit fields holds it (nanoseconds, up to 4.29 s). A time
// that does not fit is capped, never cut and never an error: these run in a frame loop.

#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan32.hpp>
#include <algorithm>
#include <cstdint>

namespace MB::FramePacing::Pacer::MarkerValue
{
  //! How long something took (the CPU busy time): nothing for a negative time, the field's largest value for a longer one.
  [[nodiscard]] constexpr NanosecondTimeSpan32 Duration(const NanosecondTimeSpan span) noexcept
  {
    const int64_t largest = int64_t{NanosecondTimeSpan32::MaxValue().Nanoseconds()};
    return NanosecondTimeSpan32(static_cast<uint32_t>(std::clamp(span.Nanoseconds(), int64_t{0}, largest)));
  }

  //! A frame time (the target and the preferred frame time): as Duration, but a longer one is one below the field's largest value,
  //! which in these fields says "on demand".
  [[nodiscard]] constexpr NanosecondTimeSpan32 FrameTime(const NanosecondTimeSpan span) noexcept
  {
    const int64_t largest = int64_t{NanosecondTimeSpan32::MaxValue().Nanoseconds()} - 1;
    return NanosecondTimeSpan32(static_cast<uint32_t>(std::clamp(span.Nanoseconds(), int64_t{0}, largest)));
  }
}

#endif
