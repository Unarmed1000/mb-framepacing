// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// Encoding: the payload's bytes, and the QR symbol (GenerateModules) every frame starts from. The frame index changes every iteration,
// as it does every frame, so the QR encoder picks its mask for a new symbol each time. QrcodegenEncode is the original encoder, the
// vendored QR Code generator library, with the same payloads: the marker's own encoder gives the same symbols
// (doc/encoding-performance.md).
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/marker/payload/StartMetadata.hpp>
#include <benchmark/benchmark.h>
#include <array>
#include <cstddef>
#include <cstdint>
#include "BenchmarkMarkers.hpp"
#include "QrcodegenReference.h"

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

  //! The original encoder on the same payloads: the payload's bytes as a symbol of the kind's version, the mask chosen automatically.
  void QrcodegenEncode(benchmark::State& state)
  {
    const FM::MarkerKind kind = BM::KindOf(state);
    const FM::StartMetadata metadata = BM::Metadata();
    const int version = kind == FM::MarkerKind::Sync ? 2 : 6;
    std::array<uint8_t, qrcodegen_BUFFER_LEN_FOR_VERSION(6)> dataAndTemp{};
    std::array<uint8_t, qrcodegen_BUFFER_LEN_FOR_VERSION(6)> symbol{};
    uint64_t frameIndex = 0;
    for ([[maybe_unused]] auto iteration : state)
    {
      const std::size_t count = FM::EncodePayload(BM::FramePayload(frameIndex++).WithKind(kind), metadata, dataAndTemp);
      bool encoded =
        qrcodegen_encodeBinary(dataAndTemp.data(), count, symbol.data(), qrcodegen_Ecc_MEDIUM, version, version, qrcodegen_Mask_AUTO, false);
      benchmark::DoNotOptimize(encoded);
      benchmark::DoNotOptimize(symbol.data());
      benchmark::ClobberMemory();
    }
    BM::SetKindLabel(state, kind);
  }
}

BENCHMARK(EncodePayload)->Arg(BM::MainArgument)->Arg(static_cast<int64_t>(FM::MarkerKind::SequenceStart))->Arg(BM::SyncArgument);
BENCHMARK(GenerateModules)->Arg(BM::MainArgument)->Arg(static_cast<int64_t>(FM::MarkerKind::SequenceStart))->Arg(BM::SyncArgument);
BENCHMARK(QrcodegenEncode)->Arg(BM::MainArgument)->Arg(static_cast<int64_t>(FM::MarkerKind::SequenceStart))->Arg(BM::SyncArgument);
