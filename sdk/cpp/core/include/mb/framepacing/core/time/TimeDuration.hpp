#ifndef MB_FRAMEPACING_CORE_TIME_TIMEDURATION_HPP
#define MB_FRAMEPACING_CORE_TIME_TIMEDURATION_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <cassert>
#include <compare>
#include <cstdint>

namespace MB::FramePacing
{
  //! A length of time that is never negative, in ticks of 100 ns: how long something took, a period, the span from a begin to an end
  //! that can not be before it. It holds a TimeSpan that is zero or more, so it is always valid: a negative span given to it becomes
  //! zero. A TimeSpan is what can be negative (a difference between two times that can go either way), and that is what taking one
  //! duration from another gives.
  //!
  //! Where its arithmetic leaves the range it does what TimeSpan's does: a sum or a product throws std::overflow_error.
  class TimeDuration
  {
    TimeSpan m_value;

  public:
    constexpr TimeDuration() noexcept = default;

    //! A negative value becomes zero.
    constexpr explicit TimeDuration(const TimeSpan value) noexcept
      : m_value(value >= TimeSpan() ? value : TimeSpan())
    {
    }

    //! For a value the caller knows is not negative: asserted, not checked.
    static constexpr TimeDuration UncheckedCreate(const TimeSpan value) noexcept
    {
      assert(value >= TimeSpan());
      TimeDuration duration;
      duration.m_value = value;
      return duration;
    }

    static constexpr TimeDuration Zero() noexcept
    {
      return {};
    }

    static constexpr TimeDuration MaxValue() noexcept
    {
      return UncheckedCreate(TimeSpan::MaxValue());
    }

    //! A negative count becomes zero.
    static constexpr TimeDuration FromTicks(const int64_t ticks) noexcept
    {
      return TimeDuration(TimeSpan(ticks));
    }

    //! The marker's 32-bit interval, which every TimeDuration can hold.
    static constexpr TimeDuration From(const TimeSpan32 value) noexcept
    {
      return UncheckedCreate(value.ToTimeSpan());
    }

    //! The duration as a TimeSpan, which is never negative.
    [[nodiscard]] constexpr TimeSpan Value() const noexcept
    {
      return m_value;
    }

    [[nodiscard]] constexpr int64_t Ticks() const noexcept
    {
      return m_value.Ticks();
    }

    [[nodiscard]] constexpr uint64_t UnsignedTicks() const noexcept
    {
      return static_cast<uint64_t>(m_value.Ticks());
    }

    //! Throws std::overflow_error if the sum is outside the range.
    constexpr TimeDuration& operator+=(const TimeDuration other)
    {
      m_value += other.m_value;
      return *this;
    }

    //! Stops at zero: a duration with a longer one taken from it is no time at all.
    constexpr TimeDuration& operator-=(const TimeDuration other) noexcept
    {
      m_value = m_value >= other.m_value ? TimeSpan(m_value.Ticks() - other.m_value.Ticks()) : TimeSpan();
      return *this;
    }

    //! Throws std::overflow_error if the sum is outside the range.
    friend constexpr TimeDuration operator+(TimeDuration lhs, const TimeDuration rhs)
    {
      return lhs += rhs;
    }

    //! The difference of two durations can be negative: a TimeSpan.
    friend constexpr TimeSpan operator-(const TimeDuration lhs, const TimeDuration rhs) noexcept
    {
      return TimeSpan(lhs.m_value.Ticks() - rhs.m_value.Ticks());
    }

    //! A duration and a span that can be negative give a span that can be negative. Each throws std::overflow_error outside the range.
    friend constexpr TimeSpan operator+(const TimeDuration lhs, const TimeSpan rhs)
    {
      return lhs.m_value + rhs;
    }

    friend constexpr TimeSpan operator+(const TimeSpan lhs, const TimeDuration rhs)
    {
      return lhs + rhs.m_value;
    }

    friend constexpr TimeSpan operator-(const TimeDuration lhs, const TimeSpan rhs)
    {
      return lhs.m_value - rhs;
    }

    friend constexpr TimeSpan operator-(const TimeSpan lhs, const TimeDuration rhs)
    {
      return lhs - rhs.m_value;
    }

    //! So many times the duration, as TimeSpan multiplies: throws std::overflow_error outside the range.
    friend constexpr TimeDuration operator*(const TimeDuration duration, const uint32_t factor)
    {
      return UncheckedCreate(duration.m_value * static_cast<double>(factor));
    }

    friend constexpr TimeDuration operator*(const uint32_t factor, const TimeDuration duration)
    {
      return duration * factor;
    }

    //! The duration in so many parts, as TimeSpan divides: rounded to the nearest tick, a tie to the even one, and
    //! std::overflow_error for a divisor of 0.
    friend constexpr TimeDuration operator/(const TimeDuration duration, const uint32_t divisor)
    {
      return UncheckedCreate(duration.m_value / static_cast<double>(divisor));
    }

    static constexpr TimeDuration Min(const TimeDuration lhs, const TimeDuration rhs) noexcept
    {
      return lhs.m_value <= rhs.m_value ? lhs : rhs;
    }

    static constexpr TimeDuration Max(const TimeDuration lhs, const TimeDuration rhs) noexcept
    {
      return lhs.m_value >= rhs.m_value ? lhs : rhs;
    }

    constexpr bool operator==(const TimeDuration&) const noexcept = default;
    constexpr std::strong_ordering operator<=>(const TimeDuration&) const noexcept = default;
  };
}

#endif
