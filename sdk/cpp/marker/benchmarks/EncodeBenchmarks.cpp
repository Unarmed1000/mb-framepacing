// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// Encoding: the payload's bytes, and the QR symbol (GenerateModules) every frame starts from. The frame index changes every iteration,
// as it does every frame, so the QR encoder picks its mask for a new symbol each time.
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/marker/payload/StartMetadata.hpp>
#include <benchmark/benchmark.h>
#include <array>
#include <cstdint>
#include "BenchmarkMarkers.hpp"

namespace FM = MB::FramePacing::Marker;
namespace BM = MB::FramePacing::Marker::BenchmarkMarkers;

namespace
{
  void EncodePayload(benchmark::State& state)
  {
    const FM::MarkerKind kind = BM::KindOf(state);
    const FM::StartMetadata metadata = BM::Metadata();
    std::array<uint8_t, FM::Payload::MaxEncodedByteCount> bytes{};
    uint64_t frameIndex = 0;
    for ([[maybe_unused]] auto iteration : state)
    {
      std::size_t count = FM::EncodePayload(BM::FramePayload(frameIndex++).WithKind(kind), metadata, bytes);
      benchmark::DoNotOptimize(count);
      benchmark::DoNotOptimize(bytes.data());
      benchmark::ClobberMemory();
    }
    BM::SetKindLabel(state, kind);
  }

  void GenerateModules(benchmark::State& state)
  {
    const FM::MarkerKind kind = BM::KindOf(state);
    const FM::StartMetadata metadata = BM::Metadata();
    FM::ModuleMatrix matrix;
    uint64_t frameIndex = 0;
    for ([[maybe_unused]] auto iteration : state)
    {
      bool generated = FM::GenerateModules(BM::FramePayload(frameIndex++).WithKind(kind), matrix, metadata);
      benchmark::DoNotOptimize(generated);
      benchmark::DoNotOptimize(matrix);
      benchmark::ClobberMemory();
    }
    BM::SetKindLabel(state, kind);
  }
}

BENCHMARK(EncodePayload)->Arg(BM::MainArgument)->Arg(static_cast<int64_t>(FM::MarkerKind::SequenceStart))->Arg(BM::SyncArgument);
BENCHMARK(GenerateModules)->Arg(BM::MainArgument)->Arg(static_cast<int64_t>(FM::MarkerKind::SequenceStart))->Arg(BM::SyncArgument);
