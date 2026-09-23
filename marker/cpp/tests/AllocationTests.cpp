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
#include <string_view>

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

// Counting replacements of the global allocation functions (the aligned variants keep their default, matching pair).
// NOLINTBEGIN(cppcoreguidelines-no-malloc,cppcoreguidelines-owning-memory,misc-new-delete-overloads)
void* operator new(const std::size_t size)
{
  if (g_countAllocations)
  {
    ++g_allocationCount;
  }
  if (void* const memory = std::malloc(size == 0 ? 1 : size))
  {
    return memory;
  }
  throw std::bad_alloc();
}

void* operator new[](const std::size_t size)
{
  return operator new(size);
}

void operator delete(void* const memory) noexcept
{
  std::free(memory);
}

void operator delete[](void* const memory) noexcept
{
  std::free(memory);
}

void operator delete(void* const memory, const std::size_t /*size*/) noexcept
{
  std::free(memory);
}

void operator delete[](void* const memory, const std::size_t /*size*/) noexcept
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
  std::array<uint8_t, FM::MaxEncodedPayloadByteCount> g_payloadBytes{};
  FM::ModuleMatrix g_matrix{};

  // 64 bytes, the longest start marker name
  constexpr std::string_view LongestName = "allocation-test 012345678901234567890123456789012345678901234567";
  static_assert(LongestName.size() == FM::MaxStartNameBytes);
}

TEST(Allocations, CountingWorks)
{
  const AllocationCounter counter;
  // NOLINTNEXTLINE(cppcoreguidelines-owning-memory)
  delete new int(1);
  EXPECT_EQ(AllocationCounter::Count(), 1u);
}

TEST(Allocations, GeneratingMarkersDoesNotAllocate)
{
  const FM::Options options{};
  const FM::Point origin = FM::RecommendedOrigin(FM::MarkerSlot::TopLeft, 1920, 1080, options, 2);
  const FM::StartMetadata metadata{FM::UnixEpochDateTimeTicks, LongestName};

  std::size_t written = 0;
  {
    const AllocationCounter counter;
    for (uint64_t frame = 0; frame < 200u; ++frame)
    {
      const auto ticks = static_cast<int64_t>(frame) * (FM::TicksPerSecond / 60);
      const FM::Payload framePayload{frame, ticks, 7u, FM::MarkerKind::Frame};
      const FM::Payload endPayload{frame, ticks, 7u, FM::MarkerKind::SequenceEnd};

      written += FM::GenerateQuads(framePayload, options, origin, g_quads);
      written += FM::GenerateQuads(endPayload, options, origin, g_quads);
      written += FM::GenerateStartQuads(framePayload, metadata, options, origin, g_quads);
      written += FM::GenerateTriangles(framePayload, options, origin, g_triangleVertices);
      written += FM::GenerateStartTriangles(framePayload, metadata, options, origin, g_triangleVertices);
      written += FM::GenerateIndexed(framePayload, options, origin, g_indexedVertices, g_indices).IndexCount;
      written += FM::GenerateStartIndexed(framePayload, metadata, options, origin, g_indexedVertices, g_indices, 16u).IndexCount;
      written += FM::GenerateModules(framePayload, g_matrix) ? 1u : 0u;
      written += FM::EncodePayload(framePayload, metadata, g_payloadBytes);
      written += FM::EncodePayload(framePayload).size();

      const std::size_t quadCount = FM::GenerateQuads(framePayload, options, origin, g_quads);
      const std::span<const FM::Quad> quads(g_quads.data(), quadCount);
      written += FM::QuadsToTriangles(quads, g_triangleVertices);
      written += FM::QuadsToIndexed(quads, g_indexedVertices, g_indices).IndexCount;

      FM::Payload decoded;
      FM::StartMetadata decodedMetadata;
      const std::size_t byteCount = FM::EncodePayload(framePayload, metadata, g_payloadBytes);
      written += FM::TryDecodePayload(std::span<const uint8_t>(g_payloadBytes.data(), byteCount), decoded, &decodedMetadata) ? 1u : 0u;
    }
    EXPECT_EQ(AllocationCounter::Count(), 0u);
  }
  EXPECT_GT(written, 0u) << "the calls must actually have produced output";
}
