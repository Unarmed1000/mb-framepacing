// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// Size probe: the marker's per-frame path, as an application draws it: encode a frame marker, then draw it as triangles.
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/Options.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/geometry/Vertex.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <array>
#include <cstdint>
#include <cstdio>
#include <span>

namespace FP = MB::FramePacing;
namespace FM = MB::FramePacing::Marker;

namespace
{
  std::array<FM::Vertex, FM::MaxTriangleVertexCount()> g_vertices{};
}

int main(int argc, char* argv[])
{
  const std::span<char* const> arguments(argv, static_cast<std::size_t>(argc));
  const FM::Payload payload(FM::MarkerKind::Frame, 1u, static_cast<uint64_t>(argc), FM::MarkerFlags::None, FP::TimeSpan::FromSeconds(argc / 60.0));
  FM::ModuleMatrix matrix;
  std::size_t count = 0;
  if (FM::GenerateModules(payload, matrix))
  {
    count = FM::ModulesToTriangles(matrix, FM::Options{}, {32, 32}, g_vertices);
  }
  std::printf("%zu %s\n", count, arguments[0]);
  return 0;
}
