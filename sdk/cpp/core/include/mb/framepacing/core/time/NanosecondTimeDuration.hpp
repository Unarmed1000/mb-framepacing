#ifndef MB_FRAMEPACING_CORE_TIME_NANOSECONDTIMEDURATION_HPP
#define MB_FRAMEPACING_CORE_TIME_NANOSECONDTIMEDURATION_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/core/time/TimeDuration.hpp>
#include <cassert>
#include <compare>
#include <cstdint>

namespace MB::FramePacing
{
  //! A length of time that is never negative, in nanoseconds: how long something took, a period, the span from a begin to an end
  //! that can not be before it. It holds a NanosecondTimeSpan that is zero or more, so it is always valid: a negative span given to it becomes
  //! zero. A NanosecondTimeSpan is what can be negative (a difference between two times that can go either way), and that is what taking one
  //! duration from another gives.
  //!
  //! Where its arithmetic leaves the range it does what NanosecondTimeSpan's does: a sum throws std::overflow_error. It is to
  //! nanoseconds what TimeDuration is to ticks of 100 ns: exact from one, and to one truncated to the tick.
  class NanosecondTimeDuration
  {
    NanosecondTimeSpan m_value;

  public:
    constexpr NanosecondTimeDuration() noexcept = default;

    //! A negative value becomes zero.
    constexpr explicit NanosecondTimeDuration(const NanosecondTimeSpan value) noexcept
      : m_value(value >= NanosecondTimeSpan() ? value : NanosecondTimeSpan())
    {
    }

    //! For a value the caller knows is not negative: asserted, not checked.
    static constexpr NanosecondTimeDuration UncheckedCreate(const NanosecondTimeSpan value) noexcept
    {
      assert(value >= NanosecondTimeSpan());
      NanosecondTimeDuration duration;
      duration.m_value = value;
      return duration;
    }

    static constexpr NanosecondTimeDuration Zero() noexcept
    {
      return {};
    }

    static constexpr NanosecondTimeDuration MaxValue() noexcept
    {
      return UncheckedCreate(NanosecondTimeSpan::MaxValue());
    }

    //! A negative count becomes zero.
    static constexpr NanosecondTimeDuration FromNanoseconds(const int64_t nanoseconds) noexcept
    {
      return NanosecondTimeDuration(NanosecondTimeSpan(nanoseconds));
    }

    //! Exact: a tick is 100 ns. Throws std::out_of_range for a duration of more than 292 years, which nanoseconds can not hold.
    static constexpr NanosecondTimeDuration FromTimeDuration(const TimeDuration duration)
    {
      return UncheckedCreate(NanosecondTimeSpan::FromTimeSpan(duration.Value()));
    }

    //! The duration in ticks of 100 ns, truncated to the tick.
    [[nodiscard]] constexpr TimeDuration ToTimeDuration() const noexcept
    {
      return TimeDuration::UncheckedCreate(m_value.ToTimeSpan());
    }

    //! The duration as a NanosecondTimeSpan, which is never negative.
    [[nodiscard]] constexpr NanosecondTimeSpan Value() const noexcept
    {
      return m_value;
    }

    [[nodiscard]] constexpr int64_t Nanoseconds() const noexcept
    {
      return m_value.Nanoseconds();
    }

    [[nodiscard]] constexpr uint64_t UnsignedNanoseconds() const noexcept
    {
      return static_cast<uint64_t>(m_value.Nanoseconds());
    }

    //! Throws std::overflow_error if the sum is outside the range.
    constexpr NanosecondTimeDuration& operator+=(const NanosecondTimeDuration other)
    {
      m_value += other.m_value;
      return *this;
    }

    //! Stops at zero: a duration with a longer one taken from it is no time at all.
    constexpr NanosecondTimeDuration& operator-=(const NanosecondTimeDuration other) noexcept
    {
      m_value = m_value >= other.m_value ? NanosecondTimeSpan(m_value.Nanoseconds() - other.m_value.Nanoseconds()) : NanosecondTimeSpan();
      return *this;
    }

    //! Throws std::overflow_error if the sum is outside the range.
    friend constexpr NanosecondTimeDuration operator+(NanosecondTimeDuration lhs, const NanosecondTimeDuration rhs)
    {
      return lhs += rhs;
    }

    //! The difference of two durations can be negative: a NanosecondTimeSpan.
    friend constexpr NanosecondTimeSpan operator-(const NanosecondTimeDuration lhs, const NanosecondTimeDuration rhs) noexcept
    {
      return NanosecondTimeSpan(lhs.m_value.Nanoseconds() - rhs.m_value.Nanoseconds());
    }

    //! A duration and a span that can be negative give a span that can be negative. Each throws std::overflow_error outside the range.
    friend constexpr NanosecondTimeSpan operator+(const NanosecondTimeDuration lhs, const NanosecondTimeSpan rhs)
    {
      return lhs.m_value + rhs;
    }

    friend constexpr NanosecondTimeSpan operator+(const NanosecondTimeSpan lhs, const NanosecondTimeDuration rhs)
    {
      return lhs + rhs.m_value;
    }

    friend constexpr NanosecondTimeSpan operator-(const NanosecondTimeDuration lhs, const NanosecondTimeSpan rhs)
    {
      return lhs.m_value - rhs;
    }

    friend constexpr NanosecondTimeSpan operator-(const NanosecondTimeSpan lhs, const NanosecondTimeDuration rhs)
    {
      return lhs - rhs.m_value;
    }

    static constexpr NanosecondTimeDuration Min(const NanosecondTimeDuration lhs, const NanosecondTimeDuration rhs) noexcept
    {
      return lhs.m_value <= rhs.m_value ? lhs : rhs;
    }

    static constexpr NanosecondTimeDuration Max(const NanosecondTimeDuration lhs, const NanosecondTimeDuration rhs) noexcept
    {
      return lhs.m_value >= rhs.m_value ? lhs : rhs;
    }

    constexpr bool operator==(const NanosecondTimeDuration&) const noexcept = default;
    constexpr std::strong_ordering operator<=>(const NanosecondTimeDuration&) const noexcept = default;
  };
}

#endif
