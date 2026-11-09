#ifndef MB_FRAMEPACING_CORE_INTEGERCOMPAREUTIL_HPP
#define MB_FRAMEPACING_CORE_INTEGERCOMPAREUTIL_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <type_traits>

namespace MB::FramePacing::IntegerCompareUtil
{
  //! An integer the comparisons take: not bool.
  template <typename T>
  inline constexpr bool IsComparableInteger = std::is_integral_v<T> && !std::is_same_v<T, bool>;

  //! left < right by value, whatever the two types' signs: -1 is less than 0u. C++20's std::cmp_less, for the library's C++17
  //! build too.
  template <typename TLeft, typename TRight>
  [[nodiscard]] constexpr bool Less(const TLeft left, const TRight right) noexcept
  {
    static_assert(IsComparableInteger<TLeft> && IsComparableInteger<TRight>, "Less compares integers");
    if constexpr (std::is_signed_v<TLeft> == std::is_signed_v<TRight>)
    {
      return left < right;
    }
    else if constexpr (std::is_signed_v<TLeft>)
    {
      return left < 0 || static_cast<std::make_unsigned_t<TLeft>>(left) < right;
    }
    else
    {
      return right >= 0 && left < static_cast<std::make_unsigned_t<TRight>>(right);
    }
  }

  //! left > right by value, whatever the two types' signs: C++20's std::cmp_greater.
  template <typename TLeft, typename TRight>
  [[nodiscard]] constexpr bool Greater(const TLeft left, const TRight right) noexcept
  {
    return Less(right, left);
  }
}

#endif
