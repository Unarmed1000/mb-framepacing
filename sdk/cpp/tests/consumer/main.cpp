// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The smallest program using mb_framepacing's modules: plan one frame at 60 Hz (pacer), encode its frame marker and draw it as a triangle
// list (marker), parse a CSV time (data), and print what came out with the library version (core).
#include <mb/framepacing/core/GetLibraryVersion.hpp>
#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/data/Milliseconds.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/MarkerFlags.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/ModuleMatrix.hpp>
#include <mb/framepacing/marker/Options.hpp>
#include <mb/framepacing/marker/Payload.hpp>
#include <mb/framepacing/marker/Vertex.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/FrameSchedule.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <array>
#include <cstdio>
#include <string_view>

namespace FP = MB::FramePacing;
namespace FM = MB::FramePacing::Marker;
namespace PC = MB::FramePacing::Pacer;

int main()
{
  PC::FramePacer pacer(PC::PacerSettings(PC::RefreshPeriod::FromRate(60)));
  const PC::FrameSchedule schedule = pacer.BeginFrame({FP::TimeSpan::TicksPerSecond});
  std::array<FM::Vertex, FM::MaxTriangleVertexCount()> vertices{};
  const FM::Options options{};
  const FP::Point origin = options.RecommendedOrigin(FM::MarkerKind::Frame, 1920, 1080);
  FM::ModuleMatrix matrix;
  const FM::Payload payload{FM::MarkerKind::Frame,         1u,
                            schedule.FrameIndex,           FM::MarkerFlags::None,
                            schedule.IntendedDisplayTicks, schedule.PreferredFrameTicks,
                            schedule.TargetFrameTicks,     schedule.IntendedDisplayTicks};
  const bool encoded = FM::GenerateModules(payload, matrix);
  const std::size_t count = encoded ? FM::ModulesToTriangles(matrix, options, origin, vertices) : 0u;
  const int64_t ticks = FP::Data::ParseTicks("16.6667");
  const std::string_view version = FP::GetLibraryVersion().Text;
  std::printf("mb_framepacing %.*s: %zu vertices, %lld ticks, a %u tick frame\n", static_cast<int>(version.size()), version.data(), count,
              static_cast<long long>(ticks), static_cast<unsigned>(schedule.TargetFrameTicks));
  return count > 0 && ticks == 166'667 && schedule.TargetFrameTicks == 166'667u ? 0 : 1;
}
