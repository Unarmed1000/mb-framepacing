// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The smallest program using mb_framepacing's modules: encode one frame marker and draw it as a triangle list (marker), parse a CSV time
// (data), and print what came out with the library version (core).
#include <mb/framepacing/Data.hpp>
#include <mb/framepacing/Marker.hpp>
#include <array>
#include <cstdio>
#include <string_view>

namespace FP = MB::FramePacing;
namespace FM = MB::FramePacing::Marker;

int main()
{
  std::array<FM::Vertex, FM::MaxTriangleVertexCount()> vertices{};
  const FM::Options options{};
  const FP::Point origin = options.RecommendedOrigin(FM::MarkerKind::Frame, 1920, 1080);
  FM::ModuleMatrix matrix;
  const bool encoded = FM::GenerateModules({FM::MarkerKind::Frame, 1u, 1u, FM::MarkerFlags::None, FP::TicksPerSecond / 60}, matrix);
  const std::size_t count = encoded ? FM::ModulesToTriangles(matrix, options, origin, vertices) : 0u;
  const int64_t ticks = FP::Data::ParseTicks("16.6667");
  const std::string_view version = FP::GetLibraryVersion().Text;
  std::printf("mb_framepacing %.*s: %zu vertices, %lld ticks\n", static_cast<int>(version.size()), version.data(), count,
              static_cast<long long>(ticks));
  return count > 0 && ticks == 166'667 ? 0 : 1;
}
