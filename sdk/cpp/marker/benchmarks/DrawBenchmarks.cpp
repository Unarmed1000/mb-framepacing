// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// Drawing from an encoded matrix, every output the marker library offers: quads, triangles, indexed triangles, and the static grid with
// per-frame indices. Items processed are the quads drawn (the background and one per run of dark modules).
#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/Options.hpp>
#include <mb/framepacing/marker/geometry/IndexedCount.hpp>
#include <mb/framepacing/marker/geometry/MarkerQuad.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/geometry/Vertex.hpp>
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
  constexpr FP::Point Origin{32, 32};

  void ModulesToQuads(benchmark::State& state)
  {
    const FM::MarkerKind kind = BM::KindOf(state);
    const FM::ModuleMatrix matrix = BM::Encode(kind);
    const FM::Options options;
    std::array<FM::MarkerQuad, FM::MaxQuadCount()> quads{};
    std::size_t count = 0;
    for ([[maybe_unused]] auto iteration : state)
    {
      count = FM::ModulesToQuads(matrix, options, Origin, quads);
      benchmark::DoNotOptimize(count);
      benchmark::DoNotOptimize(quads.data());
      benchmark::ClobberMemory();
    }
    state.SetItemsProcessed(state.iterations() * static_cast<int64_t>(count));
    BM::SetKindLabel(state, kind);
  }

  void ModulesToTriangles(benchmark::State& state)
  {
    const FM::MarkerKind kind = BM::KindOf(state);
    const FM::ModuleMatrix matrix = BM::Encode(kind);
    const FM::Options options;
    std::array<FM::Vertex, FM::MaxTriangleVertexCount()> vertices{};
    std::size_t count = 0;
    for ([[maybe_unused]] auto iteration : state)
    {
      count = FM::ModulesToTriangles(matrix, options, Origin, vertices);
      benchmark::DoNotOptimize(count);
      benchmark::DoNotOptimize(vertices.data());
      benchmark::ClobberMemory();
    }
    state.SetItemsProcessed(state.iterations() * static_cast<int64_t>(count / 6u));
    BM::SetKindLabel(state, kind);
  }

  void ModulesToIndexed(benchmark::State& state)
  {
    const FM::MarkerKind kind = BM::KindOf(state);
    const FM::ModuleMatrix matrix = BM::Encode(kind);
    const FM::Options options;
    std::array<FM::Vertex, FM::MaxIndexedVertexCount()> vertices{};
    std::array<uint32_t, FM::MaxIndexCount()> indices{};
    FM::IndexedCount count;
    for ([[maybe_unused]] auto iteration : state)
    {
      count = FM::ModulesToIndexed(matrix, options, Origin, vertices, indices);
      benchmark::DoNotOptimize(count);
      benchmark::DoNotOptimize(vertices.data());
      benchmark::DoNotOptimize(indices.data());
      benchmark::ClobberMemory();
    }
    state.SetItemsProcessed(state.iterations() * static_cast<int64_t>(count.VertexCount / 4u));
    BM::SetKindLabel(state, kind);
  }

  //! The static grid: built once per symbol size, options and origin, not per frame; measured for completeness.
  void GridVertices(benchmark::State& state)
  {
    const FM::MarkerKind kind = BM::KindOf(state);
    const FM::Options options;
    std::array<FM::Vertex, FM::MaxGridVertexCount()> vertices{};
    for ([[maybe_unused]] auto iteration : state)
    {
      std::size_t count = FM::GridVertices(kind, options, Origin, vertices);
      benchmark::DoNotOptimize(count);
      benchmark::DoNotOptimize(vertices.data());
      benchmark::ClobberMemory();
    }
    BM::SetKindLabel(state, kind);
  }

  void ModulesToGridIndices(benchmark::State& state)
  {
    const FM::MarkerKind kind = BM::KindOf(state);
    const FM::ModuleMatrix matrix = BM::Encode(kind);
    std::array<uint32_t, FM::MaxIndexCount()> indices{};
    std::size_t count = 0;
    for ([[maybe_unused]] auto iteration : state)
    {
      count = FM::ModulesToGridIndices(matrix, indices);
      benchmark::DoNotOptimize(count);
      benchmark::DoNotOptimize(indices.data());
      benchmark::ClobberMemory();
    }
    state.SetItemsProcessed(state.iterations() * static_cast<int64_t>(count / 6u));
    BM::SetKindLabel(state, kind);
  }
}

BENCHMARK(ModulesToQuads)->Arg(BM::MainArgument)->Arg(BM::SyncArgument);
BENCHMARK(ModulesToTriangles)->Arg(BM::MainArgument)->Arg(BM::SyncArgument);
BENCHMARK(ModulesToIndexed)->Arg(BM::MainArgument)->Arg(BM::SyncArgument);
BENCHMARK(GridVertices)->Arg(BM::MainArgument)->Arg(BM::SyncArgument);
BENCHMARK(ModulesToGridIndices)->Arg(BM::MainArgument)->Arg(BM::SyncArgument);
