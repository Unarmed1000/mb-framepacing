// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The frame record (sdk/doc/trace-frame-record.md, which is the reference): its strings, the name of a frame's slice, and the
// record a marker's payload gives, rule by rule: which markers get one, where the slice starts and how long it is, and which of
// the marker's times are annotations.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/trace/FrameRecord.hpp>
#include <mb/framepacing/trace/FrameRecordAnnotation.hpp>
#include <mb/framepacing/trace/FrameRecordName.hpp>
#include <mb/framepacing/trace/FrameRecordText.hpp>
#include <mb/framepacing/trace/FrameRecordUtil.hpp>
#include <gtest/gtest.h>
#include <array>
#include <cstdint>
#include <cstring>
#include <limits>
#include <span>
#include <string_view>

namespace FP = MB::FramePacing;
namespace FM = MB::FramePacing::Marker;
namespace TR = MB::FramePacing::Trace;

namespace
{
  constexpr FP::NanosecondTimeDuration FrameTime60 = FP::NanosecondTimeDuration::FromNanoseconds(16'666'667);
  constexpr FP::NanosecondTimeDuration FrameTime30 = FP::NanosecondTimeDuration::FromNanoseconds(33'333'333);

  //! A frame marker's payload with every time known: frame 200 of run 7
  constexpr FM::Payload FullPayload(const FM::MarkerKind kind = FM::MarkerKind::Frame)
  {
    return {kind,
            7u,
            200u,
            FM::MarkerFlags::NoFlags,
            FP::NanosecondTimeSpan(833'277'800),
            FrameTime60,
            FrameTime30,
            FP::NanosecondTickCount(5'000'689'390'831),
            FP::NanosecondTickCount(5'000'685'872'400),
            FP::NanosecondTimeDuration::FromNanoseconds(77'600)};
  }

  //! A payload with a CPU start time and nothing else that is optional
  constexpr FM::Payload PayloadStartingAt(const int64_t cpuStartNanoseconds)
  {
    return {FM::MarkerKind::Frame,
            7u,
            201u,
            FM::MarkerFlags::NoFlags,
            FP::NanosecondTimeSpan(10),
            {},
            {},
            {},
            FP::NanosecondTickCount(cpuStartNanoseconds)};
  }

  //! A text that can be given to a function that takes a C string: something, and a zero after it
  void ExpectEndsWithAZero(const std::string_view text)
  {
    EXPECT_FALSE(text.empty());
    // A string literal: read as a C string it ends where the view does, which is what this checks
    // NOLINTNEXTLINE(bugprone-suspicious-stringview-data-usage)
    EXPECT_EQ(std::strlen(text.data()), text.size()) << text;
  }

  void ExpectEmpty(const TR::FrameRecord& record)
  {
    EXPECT_TRUE(record.Name.View().empty());
    EXPECT_EQ(record.Name, TR::FrameRecordName());
    EXPECT_EQ(record.StartTime, FP::NanosecondTickCount());
    EXPECT_EQ(record.Duration, FP::NanosecondTimeDuration());
    EXPECT_EQ(record.AnnotationCount, 0u);
    EXPECT_TRUE(record.Annotations().empty());
    for (const TR::FrameRecordAnnotation& annotation : record.AnnotationBuffer)
    {
      EXPECT_EQ(annotation, TR::FrameRecordAnnotation());
    }
  }
}

TEST(FrameRecordText, TheStringsAreTheDocuments)
{
  EXPECT_EQ(TR::FrameRecordText::Category, "mbfp");
  EXPECT_EQ(TR::FrameRecordText::TrackName, "mbfp frames");
  EXPECT_EQ(TR::FrameRecordText::NamePrefix, "mbfp frame ");
  EXPECT_EQ(TR::FrameRecordText::AnimationNs, "animationNs");
  EXPECT_EQ(TR::FrameRecordText::MarkerPreferredNs, "markerPreferredNs");
  EXPECT_EQ(TR::FrameRecordText::MarkerTargetNs, "markerTargetNs");
  EXPECT_EQ(TR::FrameRecordText::IntendedDisplayNs, "intendedDisplayNs");
  // Perfetto's built-in clock for CLOCK_MONOTONIC
  EXPECT_EQ(TR::FrameRecordText::ClockId, 3u);
}

TEST(FrameRecordText, EveryTextEndsWithAZero)
{
  // Each can be given to a function that takes a C string
  constexpr std::array<std::string_view, 7> Texts = {
    TR::FrameRecordText::Category,          TR::FrameRecordText::TrackName,         TR::FrameRecordText::NamePrefix,
    TR::FrameRecordText::AnimationNs,       TR::FrameRecordText::MarkerPreferredNs, TR::FrameRecordText::MarkerTargetNs,
    TR::FrameRecordText::IntendedDisplayNs,
  };
  for (const std::string_view text : Texts)
  {
    ExpectEndsWithAZero(text);
  }
}

TEST(FrameRecordName, ADefaultNameIsEmpty)
{
  const TR::FrameRecordName name;
  EXPECT_TRUE(name.View().empty());
  EXPECT_STREQ(name.CStr(), "");
}

TEST(FrameRecordName, TheSmallestIds)
{
  const TR::FrameRecordName name(0u, 0u);
  EXPECT_EQ(name.View(), "mbfp frame 0 0");
  EXPECT_STREQ(name.CStr(), "mbfp frame 0 0");
}

TEST(FrameRecordName, TheLargestIdsGiveTheLongestName)
{
  const TR::FrameRecordName name(std::numeric_limits<uint32_t>::max(), std::numeric_limits<uint64_t>::max());
  EXPECT_EQ(name.View(), "mbfp frame 4294967295 18446744073709551615");
  EXPECT_EQ(name.View().size(), TR::FrameRecordName::MaxLength);
  EXPECT_EQ(TR::FrameRecordName::MaxLength, 42u);
  // The zero that ends it is the last of the name's characters: one more than the longest name
  EXPECT_EQ(std::strlen(name.CStr()), TR::FrameRecordName::MaxLength);
  EXPECT_STREQ(name.CStr(), "mbfp frame 4294967295 18446744073709551615");
}

TEST(FrameRecordName, TheIdsAreDecimalWithoutLeadingZeros)
{
  EXPECT_EQ(TR::FrameRecordName(1u, 388u).View(), "mbfp frame 1 388");
  EXPECT_EQ(TR::FrameRecordName(9u, 10u).View(), "mbfp frame 9 10");
  EXPECT_EQ(TR::FrameRecordName(10u, 9u).View(), "mbfp frame 10 9");
  EXPECT_EQ(TR::FrameRecordName(100u, 1'000u).View(), "mbfp frame 100 1000");
  EXPECT_EQ(TR::FrameRecordName(1'234'567'890u, 9'876'543'210'123'456'789u).View(), "mbfp frame 1234567890 9876543210123456789");
  // Past 32 bits: the frame index is 64 bits wide
  EXPECT_EQ(TR::FrameRecordName(4'000'000'000u, 4'294'967'296u).View(), "mbfp frame 4000000000 4294967296");
}

TEST(FrameRecordName, TheCStringIsTheView)
{
  const TR::FrameRecordName name(7u, 200u);
  EXPECT_EQ(name.CStr(), name.View().data());
  EXPECT_EQ(std::strlen(name.CStr()), name.View().size());
  EXPECT_EQ(std::string_view(name.CStr()), name.View());
}

TEST(FrameRecordName, IsMadeAtCompileTime)
{
  static_assert(TR::FrameRecordName().View().empty());
  static_assert(TR::FrameRecordName(7u, 200u).View() == "mbfp frame 7 200");
  static_assert(TR::FrameRecordName(7u, 200u) == TR::FrameRecordName(7u, 200u));
  constexpr TR::FrameRecordName Longest(4'294'967'295u, 18'446'744'073'709'551'615u);
  static_assert(Longest.View().size() == TR::FrameRecordName::MaxLength);
  EXPECT_STREQ(Longest.CStr(), "mbfp frame 4294967295 18446744073709551615");
}

TEST(FrameRecordName, NamesAreEqualWhenTheirTextsAre)
{
  const TR::FrameRecordName name(7u, 200u);
  EXPECT_TRUE(name == TR::FrameRecordName(7u, 200u));
  EXPECT_FALSE(name != TR::FrameRecordName(7u, 200u));
  EXPECT_FALSE(name == TR::FrameRecordName(7u, 201u));
  EXPECT_FALSE(name == TR::FrameRecordName(8u, 200u));
  EXPECT_TRUE(name != TR::FrameRecordName(8u, 200u));
  // The same digits, split between the ids another way
  EXPECT_FALSE(TR::FrameRecordName(1u, 23u) == TR::FrameRecordName(12u, 3u));
  EXPECT_FALSE(name == TR::FrameRecordName());
  EXPECT_TRUE(TR::FrameRecordName() == TR::FrameRecordName());
}

TEST(FrameRecordAnnotation, IsEqualByNameAndValue)
{
  const TR::FrameRecordAnnotation annotation{TR::FrameRecordText::AnimationNs, 5};
  EXPECT_TRUE(annotation == (TR::FrameRecordAnnotation{TR::FrameRecordText::AnimationNs, 5}));
  EXPECT_FALSE(annotation == (TR::FrameRecordAnnotation{TR::FrameRecordText::AnimationNs, 6}));
  EXPECT_FALSE(annotation == (TR::FrameRecordAnnotation{TR::FrameRecordText::MarkerTargetNs, 5}));
  const TR::FrameRecordAnnotation empty;
  EXPECT_TRUE(empty.Name.empty());
  EXPECT_EQ(empty.Value, 0);
}

TEST(FrameRecord, ADefaultRecordIsEmpty)
{
  const TR::FrameRecord record;
  ExpectEmpty(record);
  EXPECT_EQ(record.EndTime(), FP::NanosecondTickCount());
  EXPECT_EQ(TR::FrameRecord::MaxAnnotationCount, 4u);
  EXPECT_EQ(record.AnnotationBuffer.size(), TR::FrameRecord::MaxAnnotationCount);
}

TEST(FrameRecord, AnnotationsAreTheFirstOfTheBuffer)
{
  TR::FrameRecord record;
  record.AnnotationBuffer[0] = {TR::FrameRecordText::AnimationNs, 1};
  record.AnnotationBuffer[1] = {TR::FrameRecordText::MarkerTargetNs, 2};
  record.AnnotationBuffer[2] = {TR::FrameRecordText::IntendedDisplayNs, 3};
  record.AnnotationCount = 2;
  const std::span<const TR::FrameRecordAnnotation> annotations = record.Annotations();
  ASSERT_EQ(annotations.size(), 2u);
  EXPECT_EQ(annotations.data(), record.AnnotationBuffer.data());
  EXPECT_EQ(annotations[0], (TR::FrameRecordAnnotation{TR::FrameRecordText::AnimationNs, 1}));
  EXPECT_EQ(annotations[1], (TR::FrameRecordAnnotation{TR::FrameRecordText::MarkerTargetNs, 2}));

  record.AnnotationCount = static_cast<uint32_t>(TR::FrameRecord::MaxAnnotationCount);
  EXPECT_EQ(record.Annotations().size(), TR::FrameRecord::MaxAnnotationCount);
}

TEST(FrameRecord, TheEndIsTheStartAndTheDuration)
{
  TR::FrameRecord record;
  record.StartTime = FP::NanosecondTickCount(100);
  record.Duration = FP::NanosecondTimeDuration::FromNanoseconds(50);
  EXPECT_EQ(record.EndTime(), FP::NanosecondTickCount(150));

  // A slice without length ends where it starts
  record.Duration = FP::NanosecondTimeDuration();
  EXPECT_EQ(record.EndTime(), record.StartTime);

  // The count wraps around, as every point on a clock does, and the end is still after the start
  record.StartTime = FP::NanosecondTickCount(std::numeric_limits<int64_t>::max());
  record.Duration = FP::NanosecondTimeDuration::FromNanoseconds(1);
  EXPECT_EQ(record.EndTime().Nanoseconds(), std::numeric_limits<int64_t>::min());
  EXPECT_GT(record.EndTime(), record.StartTime);
  EXPECT_EQ(record.EndTime() - record.StartTime, FP::NanosecondTimeSpan(1));
}

TEST(FrameRecordUtil, AFrameMarkerGivesItsRecord)
{
  TR::FrameRecord record;
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(FullPayload(), record));
  EXPECT_EQ(record.Name.View(), "mbfp frame 7 200");
  EXPECT_EQ(record.StartTime, FP::NanosecondTickCount(5'000'685'872'400));
  EXPECT_EQ(record.Duration, FP::NanosecondTimeDuration::FromNanoseconds(77'600));
  EXPECT_EQ(record.EndTime(), FP::NanosecondTickCount(5'000'685'950'000));
  // Every annotation, in the order of the marker's fields
  const std::span<const TR::FrameRecordAnnotation> annotations = record.Annotations();
  ASSERT_EQ(annotations.size(), 4u);
  EXPECT_EQ(annotations[0], (TR::FrameRecordAnnotation{"animationNs", 833'277'800}));
  EXPECT_EQ(annotations[1], (TR::FrameRecordAnnotation{"markerPreferredNs", 16'666'667}));
  EXPECT_EQ(annotations[2], (TR::FrameRecordAnnotation{"markerTargetNs", 33'333'333}));
  EXPECT_EQ(annotations[3], (TR::FrameRecordAnnotation{"intendedDisplayNs", 5'000'689'390'831}));
  // The names are the module's own literals, so each ends with a zero
  for (const TR::FrameRecordAnnotation& annotation : annotations)
  {
    ExpectEndsWithAZero(annotation.Name);
  }
}

TEST(FrameRecordUtil, AStartAndAnEndMarkerGiveTheRecordOfTheFrameThatShowsThem)
{
  TR::FrameRecord frame;
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(FullPayload(), frame));
  for (const FM::MarkerKind kind : {FM::MarkerKind::SequenceStart, FM::MarkerKind::SequenceEnd})
  {
    TR::FrameRecord record;
    ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(FullPayload(kind), record));
    EXPECT_EQ(record.Name, frame.Name);
    EXPECT_EQ(record.StartTime, frame.StartTime);
    EXPECT_EQ(record.Duration, frame.Duration);
    EXPECT_EQ(record.AnnotationCount, frame.AnnotationCount);
    EXPECT_EQ(record.AnnotationBuffer, frame.AnnotationBuffer);
  }
}

TEST(FrameRecordUtil, ASyncMarkerGivesNoRecord)
{
  // The sync marker of a frame that gets a record: the same values with another kind
  TR::FrameRecord record;
  EXPECT_FALSE(TR::FrameRecordUtil::TryCreate(FullPayload().WithKind(FM::MarkerKind::Sync), record));
  ExpectEmpty(record);
}

#ifdef NDEBUG
// A build with asserts stops in the payload's constructor: its kind must be one of MarkerKind's
TEST(FrameRecordUtil, AKindThatIsNoMarkersGivesNoRecord)
{
  for (const uint8_t kind : {uint8_t{4}, uint8_t{5}, uint8_t{200}, uint8_t{255}})
  {
    TR::FrameRecord record;
    ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(FullPayload(), record));
    EXPECT_FALSE(TR::FrameRecordUtil::TryCreate(FullPayload(static_cast<FM::MarkerKind>(kind)), record)) << static_cast<uint32_t>(kind);
    ExpectEmpty(record);
  }
}
#endif

TEST(FrameRecordUtil, ACpuStartTimeThatIsNotAfterZeroGivesNoRecord)
{
  for (const int64_t cpuStart : {int64_t{0}, int64_t{-1}, int64_t{-5'000'000'000}, std::numeric_limits<int64_t>::min()})
  {
    TR::FrameRecord record;
    EXPECT_FALSE(TR::FrameRecordUtil::TryCreate(PayloadStartingAt(cpuStart), record)) << cpuStart;
    ExpectEmpty(record);
  }
  // A payload without the optional fields has no CPU start time
  TR::FrameRecord record;
  EXPECT_FALSE(
    TR::FrameRecordUtil::TryCreate(FM::Payload(FM::MarkerKind::Frame, 7u, 1u, FM::MarkerFlags::NoFlags, FP::NanosecondTimeSpan(10)), record));
  EXPECT_FALSE(TR::FrameRecordUtil::TryCreate(FM::Payload(), record));
  ExpectEmpty(record);
}

TEST(FrameRecordUtil, TheFirstNanosecondAndTheLastGiveARecord)
{
  TR::FrameRecord record;
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(PayloadStartingAt(1), record));
  EXPECT_EQ(record.StartTime.Nanoseconds(), 1);
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(PayloadStartingAt(std::numeric_limits<int64_t>::max()), record));
  EXPECT_EQ(record.StartTime.Nanoseconds(), std::numeric_limits<int64_t>::max());
}

TEST(FrameRecordUtil, AFailedCallEmptiesARecordThatHeldValues)
{
  TR::FrameRecord record;
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(FullPayload(), record));
  ASSERT_EQ(record.AnnotationCount, 4u);
  EXPECT_FALSE(TR::FrameRecordUtil::TryCreate(PayloadStartingAt(0), record));
  ExpectEmpty(record);

  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(FullPayload(), record));
  EXPECT_FALSE(TR::FrameRecordUtil::TryCreate(FullPayload().WithKind(FM::MarkerKind::Sync), record));
  ExpectEmpty(record);
}

TEST(FrameRecordUtil, ARecordWithFewerAnnotationsKeepsNothingOfTheRecordBefore)
{
  TR::FrameRecord record;
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(FullPayload(), record));
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(PayloadStartingAt(1'000), record));
  EXPECT_EQ(record.Name.View(), "mbfp frame 7 201");
  ASSERT_EQ(record.AnnotationCount, 1u);
  EXPECT_EQ(record.AnnotationBuffer[0], (TR::FrameRecordAnnotation{"animationNs", 10}));
  for (std::size_t index = 1; index < record.AnnotationBuffer.size(); ++index)
  {
    EXPECT_EQ(record.AnnotationBuffer[index], TR::FrameRecordAnnotation()) << index;
  }
}

TEST(FrameRecordUtil, TheAnimationTimeIsAlwaysAnAnnotation)
{
  for (const int64_t animation : {int64_t{0}, int64_t{-5}, int64_t{833'277'800}, std::numeric_limits<int64_t>::max()})
  {
    const FM::Payload payload(FM::MarkerKind::Frame, 1u, 2u, FM::MarkerFlags::NoFlags, FP::NanosecondTimeSpan(animation), {}, {}, {},
                              FP::NanosecondTickCount(1'000));
    TR::FrameRecord record;
    ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(payload, record));
    ASSERT_EQ(record.Annotations().size(), 1u);
    EXPECT_EQ(record.Annotations()[0], (TR::FrameRecordAnnotation{"animationNs", animation}));
  }
}

TEST(FrameRecordUtil, ATimeThatIsNotKnownIsNoAnnotation)
{
  const FP::NanosecondTickCount cpuStart(1'000);
  const FP::NanosecondTimeSpan animation(10);
  TR::FrameRecord record;

  // Only the preferred frame time
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(
    FM::Payload(FM::MarkerKind::Frame, 1u, 2u, FM::MarkerFlags::NoFlags, animation, FrameTime60, {}, {}, cpuStart), record));
  ASSERT_EQ(record.Annotations().size(), 2u);
  EXPECT_EQ(record.Annotations()[1], (TR::FrameRecordAnnotation{"markerPreferredNs", 16'666'667}));

  // Only the target frame time
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(
    FM::Payload(FM::MarkerKind::Frame, 1u, 2u, FM::MarkerFlags::NoFlags, animation, {}, FrameTime30, {}, cpuStart), record));
  ASSERT_EQ(record.Annotations().size(), 2u);
  EXPECT_EQ(record.Annotations()[1], (TR::FrameRecordAnnotation{"markerTargetNs", 33'333'333}));

  // Only the intended display time
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(
    FM::Payload(FM::MarkerKind::Frame, 1u, 2u, FM::MarkerFlags::NoFlags, animation, {}, {}, FP::NanosecondTickCount(2'000), cpuStart), record));
  ASSERT_EQ(record.Annotations().size(), 2u);
  EXPECT_EQ(record.Annotations()[1], (TR::FrameRecordAnnotation{"intendedDisplayNs", 2'000}));

  // The first and the last of the three: the order stays the order of the marker's fields
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(
    FM::Payload(FM::MarkerKind::Frame, 1u, 2u, FM::MarkerFlags::NoFlags, animation, FrameTime60, {}, FP::NanosecondTickCount(2'000), cpuStart),
    record));
  ASSERT_EQ(record.Annotations().size(), 3u);
  EXPECT_EQ(record.Annotations()[0], (TR::FrameRecordAnnotation{"animationNs", 10}));
  EXPECT_EQ(record.Annotations()[1], (TR::FrameRecordAnnotation{"markerPreferredNs", 16'666'667}));
  EXPECT_EQ(record.Annotations()[2], (TR::FrameRecordAnnotation{"intendedDisplayNs", 2'000}));
}

TEST(FrameRecordUtil, AnIntendedDisplayTimeBeforeTheClocksStartIsAnAnnotation)
{
  // Only 0 is "not known": the count's signed view is what is written
  const FM::Payload payload(FM::MarkerKind::Frame, 1u, 2u, FM::MarkerFlags::NoFlags, FP::NanosecondTimeSpan(10), {}, {}, FP::NanosecondTickCount(-3),
                            FP::NanosecondTickCount(1));
  TR::FrameRecord record;
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(payload, record));
  ASSERT_EQ(record.Annotations().size(), 2u);
  EXPECT_EQ(record.Annotations()[1], (TR::FrameRecordAnnotation{"intendedDisplayNs", -3}));
}

TEST(FrameRecordUtil, TheValuesAreThePayloadsAfterItsCaps)
{
  // Five seconds are more than a marker's four bytes of nanoseconds hold: the payload caps them where it is made
  const FP::NanosecondTimeDuration fiveSeconds = FP::NanosecondTimeDuration::FromNanoseconds(5'000'000'000);
  const FM::Payload payload(FM::MarkerKind::Frame, 7u, 203u, FM::MarkerFlags::NoFlags, FP::NanosecondTimeSpan(10), fiveSeconds, fiveSeconds, {},
                            FP::NanosecondTickCount(1), fiveSeconds);
  TR::FrameRecord record;
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(payload, record));
  EXPECT_EQ(record.Duration, FM::Payload::MaxCpuBusy);
  EXPECT_EQ(record.Duration.Nanoseconds(), 4'294'967'295);
  ASSERT_EQ(record.Annotations().size(), 3u);
  EXPECT_EQ(record.Annotations()[1], (TR::FrameRecordAnnotation{"markerPreferredNs", 4'294'967'294}));
  EXPECT_EQ(record.Annotations()[2], (TR::FrameRecordAnnotation{"markerTargetNs", 4'294'967'294}));
}

TEST(FrameRecordUtil, AFrameOnDemandIsAnnotatedAsTheMarkerHasIt)
{
  const FM::Payload payload(FM::MarkerKind::Frame, 7u, 204u, FM::MarkerFlags::NoFlags, FP::NanosecondTimeSpan(10), FM::Payload::OnDemandFrameTime,
                            FM::Payload::OnDemandFrameTime, {}, FP::NanosecondTickCount(2));
  TR::FrameRecord record;
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(payload, record));
  ASSERT_EQ(record.Annotations().size(), 3u);
  EXPECT_EQ(record.Annotations()[1], (TR::FrameRecordAnnotation{"markerPreferredNs", 4'294'967'295}));
  EXPECT_EQ(record.Annotations()[2], (TR::FrameRecordAnnotation{"markerTargetNs", 4'294'967'295}));
}

TEST(FrameRecordUtil, ACpuBusyTimeOfZeroGivesASliceWithoutLength)
{
  TR::FrameRecord record;
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(PayloadStartingAt(1'000), record));
  EXPECT_EQ(record.Duration, FP::NanosecondTimeDuration());
  EXPECT_EQ(record.EndTime(), record.StartTime);
}

TEST(FrameRecordUtil, TheFlagsDoNotChangeTheRecord)
{
  const FM::Payload payload(FM::MarkerKind::Frame, 7u, 200u, FM::MarkerFlags::StaticAfter | FM::MarkerFlags::StaticBefore,
                            FP::NanosecondTimeSpan(833'277'800), FrameTime60, FrameTime30, FP::NanosecondTickCount(5'000'689'390'831),
                            FP::NanosecondTickCount(5'000'685'872'400), FP::NanosecondTimeDuration::FromNanoseconds(77'600));
  TR::FrameRecord record;
  TR::FrameRecord expected;
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(payload, record));
  ASSERT_TRUE(TR::FrameRecordUtil::TryCreate(FullPayload(), expected));
  EXPECT_EQ(record.Name, expected.Name);
  EXPECT_EQ(record.StartTime, expected.StartTime);
  EXPECT_EQ(record.Duration, expected.Duration);
  EXPECT_EQ(record.AnnotationCount, expected.AnnotationCount);
  EXPECT_EQ(record.AnnotationBuffer, expected.AnnotationBuffer);
}
