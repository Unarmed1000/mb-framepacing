// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// Drawing into a pixel buffer exactly the marker's size, in every pixel format: module size 1 (a module-resolution texture to scale up)
// and 6 (the default). Bytes processed are the buffer's.
#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/Options.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/geometry/PixelFormat.hpp>
#include <mb/framepacing/marker/geometry/PixelFormatUtil.hpp>
#include <benchmark/benchmark.h>
#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>
#include "BenchmarkMarkers.hpp"

namespace FM = MB::FramePacing::Marker;
namespace BM = MB::FramePacing::Marker::BenchmarkMarkers;

namespace
{
  const char* FormatName(const FM::PixelFormat format)
  {
    switch (format)
    {
    case FM::PixelFormat::R8G8B8:
      return "R8G8B8";
    case FM::PixelFormat::R8G8B8A8:
      return "R8G8B8A8";
    case FM::PixelFormat::R8:
      break;
    }
    return "R8";
  }

  void ModulesToBitmap(benchmark::State& state)
  {
    const auto format = static_cast<FM::PixelFormat>(state.range(0));
    const FM::Options options(static_cast<int32_t>(state.range(1)));
    const FM::ModuleMatrix matrix = BM::Encode(FM::MarkerKind::Frame);
    const int32_t size = options.MarkerSizePx();
    std::vector<uint8_t> pixels(static_cast<std::size_t>(size) * static_cast<std::size_t>(size) *
                                static_cast<std::size_t>(FM::PixelFormatUtil::BytesPerPixel(format)));
    for ([[maybe_unused]] auto iteration : state)
    {
      bool drawn = FM::ModulesToBitmap(matrix, options, {0, 0}, pixels, size, size, format);
      benchmark::DoNotOptimize(drawn);
      benchmark::DoNotOptimize(pixels.data());
      benchmark::ClobberMemory();
    }
    state.SetBytesProcessed(state.iterations() * static_cast<int64_t>(pixels.size()));
    state.SetLabel(std::string(FormatName(format)) + ", " + std::to_string(size) + "x" + std::to_string(size) + " px");
  }
}

BENCHMARK(ModulesToBitmap)
  ->ArgNames({"format", "module"})
  ->ArgsProduct({{static_cast<int64_t>(FM::PixelFormat::R8), static_cast<int64_t>(FM::PixelFormat::R8G8B8),
                  static_cast<int64_t>(FM::PixelFormat::R8G8B8A8)},
                 {1, FM::Options::DefaultModuleSizePx}});
