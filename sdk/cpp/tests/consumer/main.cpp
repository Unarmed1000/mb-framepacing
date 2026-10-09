// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The smallest program using mb_framepacing's modules: encode the frame marker of one frame at 60 fps and draw it as a triangle
// list (marker), read a summary's capture period (data), and print what came out with the library version (core). With
// MB_CONSUMER_PACER it also paces a frame with the experimental pacer module and fills the frame's marker from it.
#include <mb/framepacing/core/GetLibraryVersion.hpp>
#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeDuration.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/data/analysis/AnalysisSummary.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/Options.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/geometry/Vertex.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#ifdef MB_CONSUMER_PACER
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <mb/framepacing/pacer/capability/PacerTierText.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/tier/TierPacer.hpp>
#endif
#include <array>
#include <cstdint>
#include <cstdio>
#include <string_view>

namespace FP = MB::FramePacing;
namespace FM = MB::FramePacing::Marker;

#ifdef MB_CONSUMER_PACER
namespace
{
  // The experimental pacer: the first frame of a 30 fps target on a 60 Hz display, and its marker filled from the schedule (the
  // frame index is the application's own)
  bool PaceOneFrame(const uint64_t frameIndex)
  {
    namespace PC = MB::FramePacing::Pacer;
    PC::PacerSettings settings(PC::RefreshPeriod::FromRate(60));
    settings.SetPreferredFrameRate(30);
    // An application with the baseline only: a clock, the refresh period, a wait until a time and a present
    PC::TierPacer pacer(settings, PC::PacerCapabilities());
    const FP::NanosecondTickCount cpuStartTime = FP::NanosecondTickCount::FromSeconds(10);
    const PC::FrameSchedule schedule = pacer.BeginFrame(cpuStartTime);
    const PC::PresentPlan plan = pacer.EndFrame(cpuStartTime + FP::NanosecondTimeSpan::FromMilliseconds(4));
    const FP::NanosecondTimeDuration cpuBusy = plan.CpuBusy;
    // The pacer and the marker both count in nanoseconds: the schedule's values go into the payload as they are
    const FM::Payload payload{FM::MarkerKind::Frame,
                              1u,
                              frameIndex,
                              FM::MarkerFlags::NoFlags,
                              schedule.AnimationTime,
                              schedule.PreferredFrameTime,
                              schedule.TargetFrameTime,
                              schedule.IntendedDisplayTime,
                              cpuStartTime,
                              cpuBusy};
    FM::ModuleMatrix matrix;
    const bool encoded = FM::GenerateModules(payload, matrix);
    const PC::PacerTier tier = pacer.Rating().Tier;
    std::printf("the pacer (experimental): tier %s, swap interval %u, a %lld ns frame, %lld ns busy\n", PC::PacerTierText::NumberOf(tier).data(),
                static_cast<unsigned>(schedule.SwapInterval), static_cast<long long>(payload.TargetFrameTime().Nanoseconds()),
                static_cast<long long>(payload.CpuBusy().Nanoseconds()));
    return encoded && tier == PC::PacerTier::TimerPeriodOnly && schedule.SwapInterval == 2u &&
           payload.TargetFrameTime().Nanoseconds() == 33'333'333 && payload.CpuBusy().Nanoseconds() == 4'000'000;
  }
}
#endif

int main()
{
  // The application's own frame counter and animation time: frame 60 of an animation at 60 fps
  const uint64_t frameIndex = 60;
  const FP::NanosecondTimeSpan animationTime = FP::NanosecondTimeSpan::FromSeconds(1);
  const FP::NanosecondTimeDuration frameTime = FP::NanosecondTimeDuration::FromNanoseconds(16'666'667);
  std::array<FM::Vertex, FM::MaxTriangleVertexCount()> vertices{};
  const FM::Options options{};
  const FP::Point origin = options.RecommendedOrigin(FM::MarkerKind::Frame, 1080);
  FM::ModuleMatrix matrix;
  const FM::Payload payload{FM::MarkerKind::Frame, 1u, frameIndex, FM::MarkerFlags::NoFlags, animationTime, frameTime, frameTime};
  const bool encoded = FM::GenerateModules(payload, matrix);
  const std::size_t count = encoded ? FM::ModulesToTriangles(matrix, options, origin, vertices) : 0u;
  const int64_t capturePeriod = FP::Data::ParseSummary(R"({ "capturePeriodNs": 16666667, "errorThresholdNs": 1000000 })").CapturePeriod.Nanoseconds();
  const std::string_view version = FP::GetLibraryVersion().Text;
  std::printf("mb_framepacing %.*s: %zu vertices, a capture period of %lld ns, a %lld ns frame\n", static_cast<int>(version.size()), version.data(),
              count, static_cast<long long>(capturePeriod), static_cast<long long>(payload.TargetFrameTime().Nanoseconds()));
#ifdef MB_CONSUMER_PACER
  const bool paced = PaceOneFrame(frameIndex);
#else
  const bool paced = true;
#endif
  return count > 0 && capturePeriod == 16'666'667 && payload.TargetFrameTime().Nanoseconds() == 16'666'667 && paced ? 0 : 1;
}
