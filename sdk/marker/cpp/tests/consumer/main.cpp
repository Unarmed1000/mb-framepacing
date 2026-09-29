// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The smallest program using mb_framemarker: encode one frame marker, draw it as a triangle list and print what was generated.
#include <mb/framemarker/FrameMarker.hpp>
#include <array>
#include <cstdio>
#include <string_view>

namespace FM = MB::FrameMarker;

int main()
{
  std::array<FM::Vertex, FM::MaxTriangleVertexCount()> vertices{};
  const FM::Options options{};
  const FM::Point origin = FM::RecommendedOrigin(FM::MarkerKind::Frame, 1920, 1080, options);
  FM::ModuleMatrix matrix;
  const bool encoded = FM::GenerateModules({1u, FM::TicksPerSecond / 60, 1u, FM::MarkerKind::Frame}, matrix);
  const std::size_t count = encoded ? FM::ModulesToTriangles(matrix, options, origin, vertices) : 0u;
  const std::string_view version = FM::GetLibraryVersion().Text;
  std::printf("mb_framemarker %.*s: %zu vertices\n", static_cast<int>(version.size()), version.data(), count);
  return count > 0 ? 0 : 1;
}
