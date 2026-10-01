// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// A whole frame as an application draws it: encode the main and the sync marker of a new frame and draw both, as triangles or as the
// static grid's per-frame indices (the grid itself is built once, outside the loop).
#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/Options.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/geometry/Vertex.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <benchmark/benchmark.h>
#include <array>
#include <cstddef>
#include <cstdint>
#include "BenchmarkMarkers.hpp"

namespace FP = MB::FramePacing;
namespace FM = MB::FramePacing::Marker;
namespace BM = MB::FramePacing::Marker::BenchmarkMarkers;

namespace
{
  const FM::Options g_options;
  const FP::Point g_mainOrigin = g_options.RecommendedOrigin(FM::MarkerKind::Frame, 1080);
  const FP::Point g_syncOrigin = g_options.RecommendedOrigin(FM::MarkerKind::Sync, 1080);

  void FrameAsTriangles(benchmark::State& state)
  {
    FM::ModuleMatrix main;
    FM::ModuleMatrix sync;
    std::array<FM::Vertex, FM::MaxTriangleVertexCount()> mainVertices{};
    std::array<FM::Vertex, FM::MaxTriangleVertexCount()> syncVertices{};
    uint64_t frameIndex = 0;
    for ([[maybe_unused]] auto iteration : state)
    {
      const FM::Payload payload = BM::FramePayload(frameIndex++);
      bool generated = FM::GenerateModules(payload, main) && FM::GenerateModules(payload.WithKind(FM::MarkerKind::Sync), sync);
      std::size_t count =
        FM::ModulesToTriangles(main, g_options, g_mainOrigin, mainVertices) + FM::ModulesToTriangles(sync, g_options, g_syncOrigin, syncVertices);
      benchmark::DoNotOptimize(generated);
      benchmark::DoNotOptimize(count);
      benchmark::DoNotOptimize(mainVertices.data());
      benchmark::DoNotOptimize(syncVertices.data());
      benchmark::ClobberMemory();
    }
  }

  void FrameAsGridIndices(benchmark::State& state)
  {
    std::array<FM::Vertex, FM::GridVertexCount(FM::MarkerKind::Frame)> mainGrid{};
    std::array<FM::Vertex, FM::GridVertexCount(FM::MarkerKind::Sync)> syncGrid{};
    if (FM::GridVertices(FM::MarkerKind::Frame, g_options, g_mainOrigin, mainGrid) == 0 ||
        FM::GridVertices(FM::MarkerKind::Sync, g_options, g_syncOrigin, syncGrid) == 0)
    {
      state.SkipWithError("GridVertices failed");
      return;
    }
    FM::ModuleMatrix main;
    FM::ModuleMatrix sync;
    std::array<uint32_t, FM::MaxIndexCount()> mainIndices{};
    std::array<uint32_t, FM::MaxIndexCount()> syncIndices{};
    uint64_t frameIndex = 0;
    for ([[maybe_unused]] auto iteration : state)
    {
      const FM::Payload payload = BM::FramePayload(frameIndex++);
      bool generated = FM::GenerateModules(payload, main) && FM::GenerateModules(payload.WithKind(FM::MarkerKind::Sync), sync);
      std::size_t count = FM::ModulesToGridIndices(main, mainIndices) + FM::ModulesToGridIndices(sync, syncIndices);
      benchmark::DoNotOptimize(generated);
      benchmark::DoNotOptimize(count);
      benchmark::DoNotOptimize(mainIndices.data());
      benchmark::DoNotOptimize(syncIndices.data());
      benchmark::ClobberMemory();
    }
  }
}

BENCHMARK(FrameAsTriangles);
BENCHMARK(FrameAsGridIndices);
