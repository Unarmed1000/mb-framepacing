// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The marker is generated every frame, so the library must never allocate. This test binary links the counting global operator
// new/delete (mb_framepacing_test_support) and checks that every generate / encode / convert call stays at zero allocations.
#include <mb/framepacing/core/GetLibraryVersion.hpp>
#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/Options.hpp>
#include <mb/framepacing/marker/geometry/MarkerQuad.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/geometry/PixelFormat.hpp>
#include <mb/framepacing/marker/geometry/PixelFormatUtil.hpp>
#include <mb/framepacing/marker/geometry/Vertex.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/marker/payload/SequenceId.hpp>
#include <mb/framepacing/marker/payload/StartMetadata.hpp>
#include <mb/framepacing/testing/AllocationCounter.hpp>
#include <gtest/gtest.h>
#include <array>
#include <cstdint>
#include <new>
#include <span>

namespace FP = MB::FramePacing;
namespace FT = MB::FramePacing::Testing;
namespace FM = MB::FramePacing::Marker;

namespace
{
  constexpr FP::TimeSpan32 FrameTime60{166'667u};
}

namespace
{
  // Static: several of these are tens of kilobytes. Sized with the library's own Max... helpers, exactly like an engine would.
  std::array<FM::MarkerQuad, FM::MaxQuadCount()> g_quads{};
  std::array<FM::Vertex, FM::MaxTriangleVertexCount()> g_triangleVertices{};
  std::array<FM::Vertex, FM::MaxIndexedVertexCount()> g_indexedVertices{};
  std::array<uint32_t, FM::MaxIndexCount()> g_indices{};
  std::array<FM::Vertex, FM::MaxGridVertexCount()> g_grid{};
  std::array<uint8_t, FM::Payload::MaxEncodedByteCount> g_payloadBytes{};
  std::array<uint8_t, std::size_t{294} * 294u * 4u> g_pixels{};
  FM::ModuleMatrix g_matrix{};
  FM::ModuleMatrix g_startMatrix{};
  FM::ModuleMatrix g_syncMatrix{};
}

TEST(Allocations, CountingWorks)
{
  const FT::AllocationCounter counter;
  // An explicit call: compilers may elide a new-expression pair like 'delete new int(1)' (clang does), but not this.
  void* const memory = ::operator new(sizeof(int));
  ::operator delete(memory);
  EXPECT_EQ(FT::AllocationCounter::Count(), 1u);
}

TEST(Allocations, GeneratingMarkersDoesNotAllocate)
{
  const FM::Options options{};
  const FP::Point origin = options.RecommendedOrigin(FM::MarkerKind::Frame, 1080, 2);
  FM::StartMetadata metadata{int64_t{621'355'968'000'000'000}, {}};

  std::size_t written = 0;
  {
    const FT::AllocationCounter counter;
    for (uint64_t frame = 0; frame < 200u; ++frame)
    {
      const auto ticks = static_cast<int64_t>(frame) * (MB::FramePacing::TimeSpan::TicksPerSecond / 60);
      const FM::Payload framePayload{FM::MarkerKind::Frame,
                                     7u,
                                     frame,
                                     FM::MarkerFlags::StaticAfter,
                                     FP::TimeSpan{ticks},
                                     FrameTime60,
                                     FrameTime60,
                                     FP::TickCount64{ticks + 50'000},
                                     FP::TickCount64{ticks - 10'000},
                                     FP::TimeSpan32{80'000u}};
      const FM::Payload endPayload{FM::MarkerKind::SequenceEnd, 7u, frame, FM::MarkerFlags::None, FP::TimeSpan{ticks}};
      const FM::Payload startPayload{FM::MarkerKind::SequenceStart, 7u, frame, FM::MarkerFlags::None, FP::TimeSpan{ticks}};
      written += FM::SequenceId::TryFromText("allocation-test", metadata.Id) ? 1u : 0u;

      written += FM::GenerateModules(framePayload, g_matrix) ? 1u : 0u;
      written += FM::GenerateModules(startPayload, g_startMatrix, metadata) ? 1u : 0u;
      written += FM::ModulesToQuads(g_matrix, options, origin, g_quads);
      written += FM::ModulesToQuads(g_startMatrix, options, origin, g_quads);
      written += FM::ModulesToTriangles(g_matrix, options, origin, g_triangleVertices);
      written += FM::ModulesToIndexed(g_matrix, options, origin, g_indexedVertices, g_indices, 16u).IndexCount;
      written += FM::ModulesToBitmap(g_matrix, options, {0, 0}, g_pixels, 294, 294, FM::PixelFormat::R8G8B8A8) ? 1u : 0u;
      written += FM::ModulesToBitmap(g_startMatrix, FM::Options(1, 0), {0, 0}, g_pixels, 41, 41, FM::PixelFormat::R8) ? 1u : 0u;
      written += g_matrix.Bits().size();
      written += FM::GridVertices(FM::MarkerKind::Frame, options, origin, g_grid);
      written += FM::ModulesToGridIndices(g_matrix, g_indices, 32u);
      written += FM::GenerateModules(endPayload, g_matrix) ? 1u : 0u;

      // The sync marker, the values around a payload and the sizing: every call an application makes per frame
      const FM::Payload syncPayload = framePayload.WithKind(FM::MarkerKind::Sync);
      written += FM::GenerateModules(syncPayload, g_syncMatrix) ? 1u : 0u;
      written += FM::ModulesToQuads(g_syncMatrix, options, options.RecommendedOrigin(FM::MarkerKind::Sync, 1080, 2), g_quads);
      written += FM::GridVertices(FM::MarkerKind::Sync, options, origin, g_grid);
      written += FM::ModulesToGridIndices(g_syncMatrix, g_indices, 16u);
      written += FM::ModulesToBitmap(g_syncMatrix, FM::Options(2, 1), {3, 5}, g_pixels, 64, 64, FM::PixelFormat::R8G8B8, 200u) ? 1u : 0u;
      FM::ModuleMatrix copy;
      written += FM::ModuleMatrix::TryFromBits(g_syncMatrix.Size(), g_syncMatrix.Bits(), copy) && copy == g_syncMatrix ? 1u : 0u;
      written += copy.IsDark(0, 0) ? 1u : 0u;
      written += static_cast<std::size_t>(FM::ModuleMatrix::SizeFor(syncPayload.Kind()));
      const FM::Options recommended = FM::Options::Recommended(1080, 540);
      const FM::Options minimum = FM::Options::Minimum(1080, 540);
      written += static_cast<std::size_t>(recommended.MarkerSizePx() + minimum.MarkerSizePx(FM::MarkerKind::Sync) + recommended.QuietZonePx());
      written += static_cast<std::size_t>(FM::PixelFormatUtil::BytesPerPixel(FM::PixelFormat::R8G8B8A8));
      const FM::MarkerFlags flags = (framePayload.Flags() | FM::MarkerFlags::StaticBefore) & FM::MarkerFlags::StaticAfter;
      written += FM::HasFlag(flags, FM::MarkerFlags::StaticAfter) ? 1u : 0u;
      written += metadata.Id.IsEmpty() ? 0u : 1u;
      written += FM::EncodePayload(framePayload, metadata, g_payloadBytes);
      written += MB::FramePacing::GetLibraryVersion().Text.size();


      FM::Payload decoded;
      FM::StartMetadata decodedMetadata;
      const std::size_t byteCount = FM::EncodePayload(framePayload, metadata, g_payloadBytes);
      written += FM::TryDecodePayload(std::span<const uint8_t>(g_payloadBytes.data(), byteCount), decoded, &decodedMetadata) ? 1u : 0u;
      const std::size_t startByteCount = FM::EncodePayload(startPayload, metadata, g_payloadBytes);
      written += FM::TryDecodePayload(std::span<const uint8_t>(g_payloadBytes.data(), startByteCount), decoded, &decodedMetadata) ? 1u : 0u;
    }
    EXPECT_EQ(FT::AllocationCounter::Count(), 0u);
  }
  EXPECT_GT(written, 0u) << "the calls must actually have produced output";
}
