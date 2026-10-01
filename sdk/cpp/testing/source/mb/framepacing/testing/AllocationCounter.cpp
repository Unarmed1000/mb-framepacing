// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/testing/AllocationCounter.hpp>
#include <atomic>
#include <cstddef>
#include <cstdlib>
#include <new>

namespace
{
  std::atomic<bool> g_countAllocations{false};
  std::atomic<std::size_t> g_allocationCount{0};
}

namespace MB::FramePacing::Testing
{
  AllocationCounter::AllocationCounter() noexcept
  {
    g_allocationCount = 0;
    g_countAllocations = true;
  }

  AllocationCounter::~AllocationCounter()
  {
    g_countAllocations = false;
  }

  std::size_t AllocationCounter::Count() noexcept
  {
    return g_allocationCount;
  }
}

// Counting replacements of the global allocation functions. Every non-aligned new (throwing and nothrow; libstdc++'s std::stable_sort
// uses the nothrow one) allocates with malloc, because every non-aligned delete below frees with free: a sanitizer reports any
// mismatched pair. The aligned variants keep their default, matching pair.
// No top-level const on the parameters: clang 22 then treats the sized operator delete as "non-usual" and rejects libstdc++'s
// __builtin_operator_delete calls (clang-tidy on Linux).
// NOLINTBEGIN(cppcoreguidelines-no-malloc,cppcoreguidelines-owning-memory,misc-new-delete-overloads)
namespace
{
  void* CountedMalloc(const std::size_t size) noexcept
  {
    if (g_countAllocations)
    {
      ++g_allocationCount;
    }
    return std::malloc(size == 0 ? 1 : size);
  }
}

void* operator new(std::size_t size)
{
  if (void* const memory = CountedMalloc(size))
  {
    return memory;
  }
  throw std::bad_alloc();
}

void* operator new[](std::size_t size)
{
  return operator new(size);
}

void* operator new(std::size_t size, const std::nothrow_t& /*tag*/) noexcept
{
  return CountedMalloc(size);
}

void* operator new[](std::size_t size, const std::nothrow_t& /*tag*/) noexcept
{
  return CountedMalloc(size);
}

void operator delete(void* memory, const std::nothrow_t& /*tag*/) noexcept
{
  std::free(memory);
}

void operator delete[](void* memory, const std::nothrow_t& /*tag*/) noexcept
{
  std::free(memory);
}

void operator delete(void* memory) noexcept
{
  std::free(memory);
}

void operator delete[](void* memory) noexcept
{
  std::free(memory);
}

void operator delete(void* memory, std::size_t /*size*/) noexcept
{
  std::free(memory);
}

void operator delete[](void* memory, std::size_t /*size*/) noexcept
{
  std::free(memory);
}
// NOLINTEND(cppcoreguidelines-no-malloc,cppcoreguidelines-owning-memory,misc-new-delete-overloads)
