#ifndef MB_FRAMEPACING_CORE_TIME_NANOSECONDTIMESPAN32_HPP
#define MB_FRAMEPACING_CORE_TIME_NANOSECONDTIMESPAN32_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <compare>
#include <cstdint>
#include <limits>
#include <stdexcept>

namespace MB::FramePacing
{
  //! A time interval of 0 to 4.294967295 s in nanoseconds, unsigned 32-bit: a 32-bit field that holds an interval to the
  //! nanosecond, as TimeSpan32 is one that holds it in ticks of 100 ns. It says what a value becomes when it is put into four
  //! bytes: the conversion from a NanosecondTimeSpan is exact or it throws, never a silent cut. It has no arithmetic: compute with
  //! NanosecondTimeSpan and convert the result.
  class NanosecondTimeSpan32
  {
    uint32_t m_nanoseconds{0};

  public:
    constexpr NanosecondTimeSpan32() noexcept = default;

    constexpr explicit NanosecondTimeSpan32(const uint32_t nanoseconds) noexcept
      : m_nanoseconds(nanoseconds)
    {
    }

    static constexpr NanosecondTimeSpan32 Zero() noexcept
    {
      return {};
    }

    static constexpr NanosecondTimeSpan32 MaxValue() noexcept
    {
      return NanosecondTimeSpan32(std::numeric_limits<uint32_t>::max());
    }

    static constexpr NanosecondTimeSpan32 FromNanoseconds(const uint32_t nanoseconds) noexcept
    {
      return NanosecondTimeSpan32(nanoseconds);
    }

    //! The span exactly. Throws std::out_of_range for a negative span or one longer than MaxValue().
    static constexpr NanosecondTimeSpan32 FromNanosecondTimeSpan(const NanosecondTimeSpan span)
    {
      if (span.Nanoseconds() < 0 || span.Nanoseconds() > int64_t{std::numeric_limits<uint32_t>::max()})
      {
        throw std::out_of_range("NanosecondTimeSpan32: the span is negative or longer than 4.294967295 s");
      }
      return NanosecondTimeSpan32(static_cast<uint32_t>(span.Nanoseconds()));
    }

    //! A span in ticks of 100 ns, exactly. Throws std::out_of_range for a negative span or one longer than MaxValue() (42'949'672
    //! ticks is the longest).
    static constexpr NanosecondTimeSpan32 FromTimeSpan(const TimeSpan span)
    {
      constexpr int64_t MaxTicks = int64_t{std::numeric_limits<uint32_t>::max()} / NanosecondTimeSpan::NanosecondsPerTick;
      if (span.Ticks() < 0 || span.Ticks() > MaxTicks)
      {
        throw std::out_of_range("NanosecondTimeSpan32: the span is negative or longer than 4.294967295 s");
      }
      return NanosecondTimeSpan32(static_cast<uint32_t>(span.Ticks() * NanosecondTimeSpan::NanosecondsPerTick));
    }

    [[nodiscard]] constexpr uint32_t Nanoseconds() const noexcept
    {
      return m_nanoseconds;
    }

    [[nodiscard]] constexpr NanosecondTimeSpan ToNanosecondTimeSpan() const noexcept
    {
      return NanosecondTimeSpan(m_nanoseconds);
    }

    //! The span in ticks of 100 ns, truncated to a tick, as NanosecondTimeSpan's.
    [[nodiscard]] constexpr TimeSpan ToTimeSpan() const noexcept
    {
      return ToNanosecondTimeSpan().ToTimeSpan();
    }

    constexpr bool operator==(const NanosecondTimeSpan32&) const noexcept = default;
    constexpr std::strong_ordering operator<=>(const NanosecondTimeSpan32&) const noexcept = default;
  };
}

#endif
