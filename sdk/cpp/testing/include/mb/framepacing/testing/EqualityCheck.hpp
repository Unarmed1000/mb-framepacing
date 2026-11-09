#ifndef MB_FRAMEPACING_TESTING_EQUALITYCHECK_HPP
#define MB_FRAMEPACING_TESTING_EQUALITYCHECK_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Test support (never part of the library): a check that a type's operator== and operator!= compare every field. The SDK writes them
// by hand (no defaulted comparisons, which C++17 has not), so a field added later and left out of operator== would go unseen.

#include <cstdint>

namespace MB::FramePacing::Testing::EqualityCheck
{
  //! The index of the first change that leaves a copy of value comparing as equal to it (each change takes the copy by reference
  //! and changes one field), or that a comparison of value with itself misses: -1 when every change is seen, both ways round and by
  //! both operators.
  template <typename T, typename... TChange>
  int32_t FirstChangeNotSeen(const T& value, const TChange&... changes)
  {
    // A copy, compared with value: the value compared with itself
    const T same = value;
    if (!(same == value) || same != value)
    {
      return static_cast<int32_t>(sizeof...(changes));
    }
    int32_t index = 0;
    int32_t notSeen = -1;
    const auto check = [&value, &index, &notSeen](const auto& change)
    {
      T changed = value;
      change(changed);
      if (notSeen < 0 && (changed == value || value == changed || !(changed != value) || !(value != changed)))
      {
        notSeen = index;
      }
      ++index;
    };
    (check(changes), ...);
    return notSeen;
  }
}

#endif
