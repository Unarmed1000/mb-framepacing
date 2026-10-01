// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The smallest program using mb_framepacing's modules: encode the frame marker of one frame at 60 fps and draw it as a triangle
// list (marker), parse a CSV time (data), and print what came out with the library version (core).
#include <mb/framepacing/core/GetLibraryVersion.hpp>
#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/data/analysis/Milliseconds.hpp>
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
#include <string_view>

namespace FP = MB::FramePacing;
namespace FM = MB::FramePacing::Marker;

int main()
{
  // The application's own frame counter and animation time: frame 60 of an animation at 60 fps
  const uint64_t frameIndex = 60;
  const int64_t animationTicks = FP::TimeSpan::FromSeconds(1).Ticks();
  const uint32_t frameTicks = 166'667;
  std::array<FM::Vertex, FM::MaxTriangleVertexCount()> vertices{};
  const FM::Options options{};
  const FP::Point origin = options.RecommendedOrigin(FM::MarkerKind::Frame, 1080);
  FM::ModuleMatrix matrix;
  const FM::Payload payload{
    FM::MarkerKind::Frame,     1u, frameIndex, FM::MarkerFlags::None, FP::TimeSpan{animationTicks}, FP::TimeSpan32{frameTicks},
    FP::TimeSpan32{frameTicks}};
  const bool encoded = FM::GenerateModules(payload, matrix);
  const std::size_t count = encoded ? FM::ModulesToTriangles(matrix, options, origin, vertices) : 0u;
  const int64_t ticks = FP::Data::ParseTicks("16.6667");
  const std::string_view version = FP::GetLibraryVersion().Text;
  std::printf("mb_framepacing %.*s: %zu vertices, %lld ticks, a %u tick frame\n", static_cast<int>(version.size()), version.data(), count,
              static_cast<long long>(ticks), static_cast<unsigned>(payload.TargetFrameTime().Ticks()));
  return count > 0 && ticks == 166'667 && payload.TargetFrameTime().Ticks() == 166'667u ? 0 : 1;
}
