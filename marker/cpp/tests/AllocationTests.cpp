// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The marker is generated every frame, so the library must never allocate. This test binary replaces the global operator new/delete
// with counting versions and checks that every generate / encode / convert call stays at zero allocations.
#include <mb/framemarker/FrameMarker.hpp>
#include <gtest/gtest.h>
#include <array>
#include <atomic>
#include <cstdint>
#include <cstdlib>
#include <new>
#include <span>

namespace
{
  std::atomic<bool> g_countAllocations{false};
  std::atomic<std::size_t> g_allocationCount{0};

  //! Counts the allocations made while it is alive (the tests run on one thread).
  class AllocationCounter
  {
  public:
    AllocationCounter() noexcept
    {
      g_allocationCount = 0;
      g_countAllocations = true;
    }

    ~AllocationCounter()
    {
      g_countAllocations = false;
    }

    AllocationCounter(const AllocationCounter&) = delete;
    AllocationCounter& operator=(const AllocationCounter&) = delete;
    AllocationCounter(AllocationCounter&&) = delete;
    AllocationCounter& operator=(AllocationCounter&&) = delete;

    static std::size_t Count() noexcept
    {
      return g_allocationCount;
    }
  };
}

// Counting replacements of the global allocation functions. Every non-aligned new (throwing and nothrow; libstdc++'s
// std::stable_sort uses the nothrow one) allocates with malloc, because every non-aligned delete below frees with free: a
// sanitizer reports any mismatched pair. The aligned variants keep their default, matching pair.
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

namespace FM = MB::FrameMarker;

namespace
{
  // Static: several of these are tens of kilobytes. Sized with the library's own Max... helpers, exactly like an engine would.
  std::array<FM::Quad, FM::MaxQuadCount()> g_quads{};
  std::array<FM::Vertex, FM::MaxTriangleVertexCount()> g_triangleVertices{};
  std::array<FM::Vertex, FM::MaxIndexedVertexCount()> g_indexedVertices{};
  std::array<uint32_t, FM::MaxIndexCount()> g_indices{};
  std::array<FM::Vertex, FM::MaxGridVertexCount()> g_grid{};
  std::array<uint8_t, FM::MaxEncodedPayloadByteCount> g_payloadBytes{};
  std::array<uint8_t, 294u * 294u * 4u> g_pixels{};
  FM::ModuleMatrix g_matrix{};
  FM::ModuleMatrix g_startMatrix{};
}

TEST(Allocations, CountingWorks)
{
  const AllocationCounter counter;
  // An explicit call: compilers may elide a new-expression pair like 'delete new int(1)' (clang does), but not this.
  void* const memory = ::operator new(sizeof(int));
  ::operator delete(memory);
  EXPECT_EQ(AllocationCounter::Count(), 1u);
}

TEST(Allocations, GeneratingMarkersDoesNotAllocate)
{
  const FM::Options options{};
  const FM::Point origin = FM::RecommendedOrigin(FM::MarkerKind::Frame, 1920, 1080, options, 2);
  FM::StartMetadata metadata{FM::UnixEpochDateTimeTicks, {}};

  std::size_t written = 0;
  {
    const AllocationCounter counter;
    for (uint64_t frame = 0; frame < 200u; ++frame)
    {
      const auto ticks = static_cast<int64_t>(frame) * (FM::TicksPerSecond / 60);
      const FM::Payload framePayload{frame, ticks, 7u, FM::MarkerKind::Frame, ticks + 50'000, 166'667u, ticks - 10'000, 80'000u};
      const FM::Payload endPayload{frame, ticks, 7u, FM::MarkerKind::SequenceEnd};
      const FM::Payload startPayload{frame, ticks, 7u, FM::MarkerKind::SequenceStart};
      written += FM::SequenceId::TryFromText("allocation-test", metadata.Id) ? 1u : 0u;

      written += FM::GenerateModules(framePayload, g_matrix) ? 1u : 0u;
      written += FM::GenerateModules(startPayload, g_startMatrix, metadata) ? 1u : 0u;
      written += FM::ModulesToQuads(g_matrix, options, origin, g_quads);
      written += FM::ModulesToQuads(g_startMatrix, options, origin, g_quads);
      written += FM::ModulesToTriangles(g_matrix, options, origin, g_triangleVertices);
      written += FM::ModulesToIndexed(g_matrix, options, origin, g_indexedVertices, g_indices, 16u).IndexCount;
      written += FM::ModulesToBitmap(g_matrix, options, {0, 0}, g_pixels, 294, 294, FM::PixelFormat::Rgba32) ? 1u : 0u;
      written += FM::ModulesToBitmap(g_startMatrix, {1, 0}, {0, 0}, g_pixels, 41, 41, FM::PixelFormat::Gray8) ? 1u : 0u;
      written += g_matrix.Bits().size();
      written += FM::GridVertices(FM::MarkerKind::Frame, options, origin, g_grid);
      written += FM::ModulesToGridIndices(g_matrix, g_indices, 32u);
      written += FM::GenerateModules(endPayload, g_matrix) ? 1u : 0u;
      written += FM::EncodePayload(framePayload, metadata, g_payloadBytes);
      written += FM::GetLibraryVersion().Text.size();


      FM::Payload decoded;
      FM::StartMetadata decodedMetadata;
      const std::size_t byteCount = FM::EncodePayload(framePayload, metadata, g_payloadBytes);
      written += FM::TryDecodePayload(std::span<const uint8_t>(g_payloadBytes.data(), byteCount), decoded, &decodedMetadata) ? 1u : 0u;
      const std::size_t startByteCount = FM::EncodePayload(startPayload, metadata, g_payloadBytes);
      written += FM::TryDecodePayload(std::span<const uint8_t>(g_payloadBytes.data(), startByteCount), decoded, &decodedMetadata) ? 1u : 0u;
    }
    EXPECT_EQ(AllocationCounter::Count(), 0u);
  }
  EXPECT_GT(written, 0u) << "the calls must actually have produced output";
}
