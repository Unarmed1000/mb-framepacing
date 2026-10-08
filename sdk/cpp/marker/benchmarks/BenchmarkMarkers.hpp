#ifndef MB_FRAMEPACING_MARKER_BENCHMARKS_BENCHMARKMARKERS_HPP
#define MB_FRAMEPACING_MARKER_BENCHMARKS_BENCHMARKMARKERS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The markers the benchmarks measure: a frame marker with every field set, as a paced application writes it every frame.

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/marker/payload/SequenceId.hpp>
#include <mb/framepacing/marker/payload/StartMetadata.hpp>
#include <benchmark/benchmark.h>
#include <cstdint>
#include <stdexcept>

namespace MB::FramePacing::Marker::BenchmarkMarkers
{
  //! The marker kinds the drawing benchmarks take as their argument: the main marker and the sync marker.
  inline constexpr int64_t MainArgument = static_cast<int64_t>(MarkerKind::Frame);
  inline constexpr int64_t SyncArgument = static_cast<int64_t>(MarkerKind::Sync);

  //! Frame frameIndex of a 60 fps run: every field set, the times advancing one frame time per frame.
  inline Payload FramePayload(const uint64_t frameIndex)
  {
    constexpr NanosecondTimeDuration FrameTime = NanosecondTimeDuration::FromNanoseconds(16'666'667);
    const auto offset = static_cast<int64_t>(frameIndex) * FrameTime.Nanoseconds();
    return {MarkerKind::Frame,
            0x12345678u,
            frameIndex,
            MarkerFlags::NoFlags,
            NanosecondTimeSpan(offset),
            FrameTime,
            FrameTime,
            NanosecondTickCount(3'600'000'000'000 + offset),
            NanosecondTickCount(3'599'990'000'000 + offset),
            NanosecondTimeDuration::FromNanoseconds(8'000'000)};
  }

  //! A start marker's metadata: a start time and a text sequence id.
  inline StartMetadata Metadata()
  {
    StartMetadata metadata;
    metadata.UtcTicks = 639'257'616'000'000'000;
    if (!SequenceId::TryFromText("benchmark-run", metadata.Id))
    {
      throw std::logic_error("the benchmark's sequence id is not valid");
    }
    return metadata;
  }

  //! The kind a benchmark's first argument names.
  inline MarkerKind KindOf(const benchmark::State& state)
  {
    return static_cast<MarkerKind>(state.range(0));
  }

  //! The matrix of a typical frame of kind (the frame marker's payload, with that kind).
  inline ModuleMatrix Encode(const MarkerKind kind)
  {
    ModuleMatrix matrix;
    if (!GenerateModules(FramePayload(1'000).WithKind(kind), matrix, Metadata()))
    {
      throw std::logic_error("GenerateModules failed");
    }
    return matrix;
  }

  //! Label a benchmark with its marker kind.
  inline void SetKindLabel(benchmark::State& state, const MarkerKind kind)
  {
    state.SetLabel(kind == MarkerKind::Sync ? "sync" : kind == MarkerKind::SequenceStart ? "start" : "main");
  }
}

#endif
