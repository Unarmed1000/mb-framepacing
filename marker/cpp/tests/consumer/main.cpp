// SPDX-License-Identifier: BSD-3-Clause
//
// The smallest program using mb_framemarker: generate one frame marker as a triangle list and print what was generated.
#include <mb/framemarker/FrameMarker.hpp>
#include <array>
#include <cstdio>

namespace FM = MB::FrameMarker;

int main()
{
  std::array<FM::Vertex, FM::MaxFrameTriangleVertexCount()> vertices{};
  const FM::Options options{};
  const FM::Point origin = FM::RecommendedOrigin(FM::MarkerSlot::TopLeft, 1920, 1080, options);
  const std::size_t count = FM::GenerateTriangles({1u, FM::TicksPerSecond / 60, 1u, FM::MarkerKind::Frame}, options, origin, vertices);
  std::printf("mb_framemarker %.*s: %zu vertices\n", static_cast<int>(FM::VersionString.size()), FM::VersionString.data(), count);
  return count > 0 ? 0 : 1;
}
