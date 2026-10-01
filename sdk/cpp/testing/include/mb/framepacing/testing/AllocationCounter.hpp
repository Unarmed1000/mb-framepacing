#ifndef MB_FRAMEPACING_TESTING_ALLOCATIONCOUNTER_HPP
#define MB_FRAMEPACING_TESTING_ALLOCATIONCOUNTER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Test support (never part of the library): a test executable that links mb_framepacing_test_support gets counting replacements of the
// global operator new and delete, and this counter to read them.

#include <cstddef>

namespace MB::FramePacing::Testing
{
  //! Counts the allocations made through the global operator new while it is alive (the tests run on one thread).
  class AllocationCounter
  {
  public:
    AllocationCounter() noexcept;
    ~AllocationCounter();

    AllocationCounter(const AllocationCounter&) = delete;
    AllocationCounter& operator=(const AllocationCounter&) = delete;
    AllocationCounter(AllocationCounter&&) = delete;
    AllocationCounter& operator=(AllocationCounter&&) = delete;

    //! The allocations since the newest counter was made.
    static std::size_t Count() noexcept;
  };
}

#endif
