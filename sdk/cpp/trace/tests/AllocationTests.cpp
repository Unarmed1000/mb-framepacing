// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// An application makes a frame's record every frame, so the module must never allocate. This test binary links the counting global
// operator new/delete (mb_framepacing_test_support) and checks that making a name and making a record stay at zero allocations.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/testing/AllocationCounter.hpp>
#include <mb/framepacing/trace/FrameRecord.hpp>
#include <mb/framepacing/trace/FrameRecordAnnotation.hpp>
#include <mb/framepacing/trace/FrameRecordName.hpp>
#include <mb/framepacing/trace/FrameRecordText.hpp>
#include <mb/framepacing/trace/FrameRecordUtil.hpp>
#include <gtest/gtest.h>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <new>

namespace FP = MB::FramePacing;
namespace FT = MB::FramePacing::Testing;
namespace FM = MB::FramePacing::Marker;
namespace TR = MB::FramePacing::Trace;

namespace
{
  constexpr FP::NanosecondTimeDuration FrameTime60 = FP::NanosecondTimeDuration::FromNanoseconds(16'666'667);
}

TEST(Allocations, CountingWorks)
{
  const FT::AllocationCounter counter;
  // An explicit call: compilers may elide a new-expression pair like 'delete new int(1)' (clang does), but not this.
  void* const memory = ::operator new(sizeof(int));
  ::operator delete(memory);
  EXPECT_EQ(FT::AllocationCounter::Count(), 1u);
}

TEST(Allocations, MakingANameDoesNotAllocate)
{
  std::size_t written = 0;
  {
    const FT::AllocationCounter counter;
    for (uint64_t frame = 0; frame < 200u; ++frame)
    {
      const TR::FrameRecordName name(static_cast<uint32_t>(frame) + 1u, frame * 1'000'000'007u);
      const TR::FrameRecordName longest(std::numeric_limits<uint32_t>::max(), std::numeric_limits<uint64_t>::max() - frame);
      const TR::FrameRecordName empty;
      written += name.View().size() + longest.View().size() + empty.View().size();
      written += name == longest ? 1u : 0u;
      written += name.CStr()[0] == TR::FrameRecordText::NamePrefix[0] ? 1u : 0u;
    }
    EXPECT_EQ(FT::AllocationCounter::Count(), 0u);
  }
  EXPECT_GT(written, 0u) << "the calls must actually have produced output";
}

TEST(Allocations, MakingARecordDoesNotAllocate)
{
  std::size_t written = 0;
  int64_t sum = 0;
  {
    const FT::AllocationCounter counter;
    for (uint64_t frame = 0; frame < 200u; ++frame)
    {
      const auto time = (static_cast<int64_t>(frame) + 1) * (FP::NanosecondTimeSpan::NanosecondsPerSecond / 60);
      // What an application does every frame: the payload of the frame's marker, and the record of that payload
      const FM::Payload framePayload{FM::MarkerKind::Frame,
                                     7u,
                                     frame,
                                     FM::MarkerFlags::NoFlags,
                                     FP::NanosecondTimeSpan{time},
                                     FrameTime60,
                                     FrameTime60,
                                     FP::NanosecondTickCount{time + 5'000'000},
                                     FP::NanosecondTickCount{time},
                                     FP::NanosecondTimeDuration::FromNanoseconds(8'000'000)};
      TR::FrameRecord record;
      written += TR::FrameRecordUtil::TryCreate(framePayload, record) ? 1u : 0u;
      written += record.Name.View().size();
      for (const TR::FrameRecordAnnotation& annotation : record.Annotations())
      {
        written += annotation.Name.size();
        sum += annotation.Value;
      }
      sum += record.StartTime.Nanoseconds() + record.Duration.Nanoseconds() + record.EndTime().Nanoseconds();

      // The markers that get no record, and a start marker
      written += TR::FrameRecordUtil::TryCreate(framePayload.WithKind(FM::MarkerKind::Sync), record) ? 1u : 0u;
      written += TR::FrameRecordUtil::TryCreate(FM::Payload(), record) ? 1u : 0u;
      written += TR::FrameRecordUtil::TryCreate(framePayload.WithKind(FM::MarkerKind::SequenceStart), record) ? 1u : 0u;
      written += record.AnnotationCount;
    }
    EXPECT_EQ(FT::AllocationCounter::Count(), 0u);
  }
  // Each frame: two records, a name of at least 14 characters and four annotations twice
  EXPECT_GE(written, std::size_t{200} * (2u + 14u + 4u));
  EXPECT_GT(sum, 0) << "the calls must actually have produced output";
}
