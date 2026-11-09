// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// The marker module's comparisons: operator== and operator!= are written by hand (C++17 has no defaulted ones), so every field is
// checked to take part.
#include <mb/framepacing/core/Rectangle.hpp>
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/Options.hpp>
#include <mb/framepacing/marker/geometry/MarkerQuad.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/geometry/Vertex.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/marker/payload/SequenceId.hpp>
#include <mb/framepacing/marker/payload/StartMetadata.hpp>
#include <mb/framepacing/testing/EqualityCheck.hpp>
#include <gtest/gtest.h>
#include <array>
#include <cstdint>

namespace FP = MB::FramePacing;
namespace FM = MB::FramePacing::Marker;
namespace EC = MB::FramePacing::Testing::EqualityCheck;

namespace
{
  FP::NanosecondTimeDuration Duration(const int64_t nanoseconds)
  {
    return FP::NanosecondTimeDuration::FromNanoseconds(nanoseconds);
  }

  FP::NanosecondTickCount Time(const int64_t nanoseconds)
  {
    return FP::NanosecondTickCount::FromNanoseconds(nanoseconds);
  }

  //! A payload with every field set, and one value changed by field (0 = none).
  FM::Payload PayloadWith(const int32_t changedField)
  {
    const auto pick = [changedField](const int32_t field, const int64_t value) { return changedField == field ? value + 1 : value; };
    return {changedField == 1 ? FM::MarkerKind::SequenceEnd : FM::MarkerKind::Frame,
            static_cast<uint32_t>(pick(2, 7)),
            static_cast<uint64_t>(pick(3, 60)),
            changedField == 4 ? FM::MarkerFlags::Paced : FM::MarkerFlags::StaticAfter,
            FP::NanosecondTimeSpan(pick(5, 1'000'000'000)),
            Duration(pick(6, 16'666'667)),
            Duration(pick(7, 33'333'333)),
            Duration(pick(8, 16'666'667)),
            Time(pick(9, 2'000'000'000)),
            Time(pick(10, 1'900'000'000)),
            Duration(pick(11, 4'000'000))};
  }
}

TEST(Comparison, AQuadAndAVertexCompareEveryField)
{
  const FM::MarkerQuad quad{FP::Rectangle(1, 2, 3, 4), true};
  EXPECT_EQ(EC::FirstChangeNotSeen(
              quad, [](FM::MarkerQuad& changed) { changed.Rect = FP::Rectangle(1, 2, 3, 5); }, [](FM::MarkerQuad& changed) { changed.Dark = false; }),
            -1);
  const FM::Vertex vertex{1, 2, 255};
  EXPECT_EQ(
    EC::FirstChangeNotSeen(
      vertex, [](FM::Vertex& changed) { changed.X = 9; }, [](FM::Vertex& changed) { changed.Y = 9; }, [](FM::Vertex& changed) { changed.Luma = 0; }),
    -1);
}

TEST(Comparison, OptionsCompareEveryField)
{
  const FM::Options options(FM::Options::DefaultModuleSizePx, FM::Options::RecommendedQuietZoneModules);
  EXPECT_EQ(EC::FirstChangeNotSeen(
              options,
              [](FM::Options& changed) { changed = FM::Options(FM::Options::DefaultModuleSizePx + 1, FM::Options::RecommendedQuietZoneModules); },
              [](FM::Options& changed) { changed = FM::Options(FM::Options::DefaultModuleSizePx, FM::Options::RecommendedQuietZoneModules + 1); }),
            -1);
}

TEST(Comparison, APayloadComparesEveryField)
{
  const FM::Payload payload = PayloadWith(0);
  const auto change = [](const int32_t field) { return [field](FM::Payload& changed) { changed = PayloadWith(field); }; };
  EXPECT_EQ(EC::FirstChangeNotSeen(payload, change(1), change(2), change(3), change(4), change(5), change(6), change(7), change(8), change(9),
                                   change(10), change(11)),
            -1);
}

TEST(Comparison, TheStartMetadataComparesEveryByteOfItsId)
{
  const FM::SequenceId id;
  EXPECT_EQ(
    EC::FirstChangeNotSeen(
      id, [](FM::SequenceId& changed) { changed.Bytes[0] = 1; }, [](FM::SequenceId& changed) { changed.Bytes[FM::SequenceId::ByteCount - 1u] = 1; }),
    -1);

  FM::StartMetadata metadata;
  metadata.UtcTicks = 638'000'000'000'000'000;
  for (std::size_t i = 0; i < FM::SequenceId::ByteCount; ++i)
  {
    metadata.Id.Bytes[i] = static_cast<uint8_t>(i + 1u);
  }
  EXPECT_EQ(EC::FirstChangeNotSeen(
              metadata, [](FM::StartMetadata& changed) { changed.UtcTicks += 1; }, [](FM::StartMetadata& changed) { changed.Id.Bytes[0] = 0; },
              [](FM::StartMetadata& changed) { changed.Id.Bytes[FM::SequenceId::ByteCount - 1u] = 0; }),
            -1);
}

TEST(Comparison, AModuleMatrixComparesItsSizeAndBits)
{
  std::array<uint8_t, FM::ModuleMatrix::MaxPackedModuleByteCount> bits{};
  bits[0] = 0x80u;
  FM::ModuleMatrix matrix;
  ASSERT_TRUE(FM::ModuleMatrix::TryFromBits(FM::ModuleMatrix::SyncSize, bits, matrix));
  EXPECT_EQ(EC::FirstChangeNotSeen(
              matrix, [&bits](FM::ModuleMatrix& changed) { ASSERT_TRUE(FM::ModuleMatrix::TryFromBits(FM::ModuleMatrix::MainSize, bits, changed)); },
              [](FM::ModuleMatrix& changed)
              {
                std::array<uint8_t, FM::ModuleMatrix::MaxPackedModuleByteCount> other{};
                other[1] = 0x01u;
                ASSERT_TRUE(FM::ModuleMatrix::TryFromBits(FM::ModuleMatrix::SyncSize, other, changed));
              }),
            -1);
}
