// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/core/Rectangle.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/Options.hpp>
#include <mb/framepacing/marker/geometry/IndexedCount.hpp>
#include <mb/framepacing/marker/geometry/MarkerQuad.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/geometry/PixelFormat.hpp>
#include <mb/framepacing/marker/geometry/PixelFormatUtil.hpp>
#include <mb/framepacing/marker/geometry/Vertex.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/marker/payload/SequenceId.hpp>
#include <mb/framepacing/marker/payload/StartMetadata.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <array>
#include <limits>
#include <span>
#include <string>
#include <string_view>
#include <type_traits>
#include <vector>
#include "mb/framepacing/marker/detail/WireFormat.hpp"

namespace FP = MB::FramePacing;
namespace FM = MB::FramePacing::Marker;

namespace MB::FramePacing::Marker
{
  // Readable gtest failure output for the value types
  void PrintTo(const Payload& value, std::ostream* os)
  {
    *os << "{frame " << value.FrameIndex() << ", ticks " << value.AnimationTime().Ticks() << ", run " << value.RunId() << ", kind "
        << static_cast<uint32_t>(value.Kind()) << ", intended " << value.IntendedDisplayTime().Ticks() << ", target "
        << value.TargetFrameTime().Ticks() << ", cpu start " << value.CpuStartTime().Ticks() << ", cpu busy " << value.CpuBusy().Ticks() << "}";
  }

  void PrintTo(const SequenceId& value, std::ostream* os)
  {
    constexpr std::string_view Digits = "0123456789abcdef";
    for (const uint8_t byte : value.Bytes)
    {
      *os << Digits[byte >> 4u] << Digits[byte & 0xFu];
    }
  }

  void PrintTo(const MarkerQuad& value, std::ostream* os)
  {
    *os << "{" << value.Rect.X() << "," << value.Rect.Y() << " " << value.Rect.Width() << "x" << value.Rect.Height()
        << (value.Dark ? " dark}" : " light}");
  }

  void PrintTo(const Vertex& value, std::ostream* os)
  {
    *os << "{" << value.X << "," << value.Y << " luma " << static_cast<uint32_t>(value.Luma) << "}";
  }

  void PrintTo(const Point& value, std::ostream* os)
  {
    *os << "{" << value.X << "," << value.Y << "}";
  }
}

namespace
{
  // Typed payload values the tests share (every time is in 100 ns ticks)
  constexpr FP::TimeSpan32 UnknownFrameTime{};
  constexpr FP::TickCount64 UnknownTime{};
  constexpr FP::TimeSpan32 FrameTime60{166'667u};
  constexpr FP::TimeSpan32 FrameTime30{333'333u};
  constexpr FP::TimeSpan32 MaxFrameTime = FP::TimeSpan32::MaxValue();
  constexpr FP::TimeSpan MinAnimation = FP::TimeSpan::MinValue();
  constexpr FP::TimeSpan MaxAnimation = FP::TimeSpan::MaxValue();
  constexpr FP::TickCount64 MinClock{std::numeric_limits<int64_t>::min()};
  constexpr FP::TickCount64 MaxClock{std::numeric_limits<int64_t>::max()};

  //! The payload's bytes (the header for frame, end and sync markers), through the one EncodePayload.
  std::vector<uint8_t> PayloadBytes(const FM::Payload& payload)
  {
    std::array<uint8_t, FM::Payload::MaxEncodedByteCount> buffer{};
    const std::size_t count = FM::EncodePayload(payload, {}, buffer);
    return {buffer.begin(), buffer.begin() + static_cast<std::ptrdiff_t>(count)};
  }

  //! The documented vertex order of a quad: (TL, TR, BL) (BL, TR, BR), written independently of the library.
  std::vector<FM::Vertex> ToTriangles(const std::vector<FM::MarkerQuad>& quads)
  {
    std::vector<FM::Vertex> vertices;
    for (const FM::MarkerQuad& quad : quads)
    {
      const uint8_t luma = quad.Dark ? 0u : 255u;
      vertices.insert(vertices.end(), {{quad.Rect.Left(), quad.Rect.Top(), luma},
                                       {quad.Rect.Right(), quad.Rect.Top(), luma},
                                       {quad.Rect.Left(), quad.Rect.Bottom(), luma},
                                       {quad.Rect.Left(), quad.Rect.Bottom(), luma},
                                       {quad.Rect.Right(), quad.Rect.Top(), luma},
                                       {quad.Rect.Right(), quad.Rect.Bottom(), luma}});
    }
    return vertices;
  }

  //! The documented indexed order: 4 vertices (TL, TR, BR, BL) and indices (0,1,3)(3,1,2) per quad, plus baseVertex.
  void ToIndexed(const std::vector<FM::MarkerQuad>& quads, const uint32_t baseVertex, std::vector<FM::Vertex>& rVertices,
                 std::vector<uint32_t>& rIndices)
  {
    for (const FM::MarkerQuad& quad : quads)
    {
      const uint8_t luma = quad.Dark ? 0u : 255u;
      const auto first = baseVertex + static_cast<uint32_t>(rVertices.size());
      rVertices.insert(rVertices.end(), {{quad.Rect.Left(), quad.Rect.Top(), luma},
                                         {quad.Rect.Right(), quad.Rect.Top(), luma},
                                         {quad.Rect.Right(), quad.Rect.Bottom(), luma},
                                         {quad.Rect.Left(), quad.Rect.Bottom(), luma}});
      rIndices.insert(rIndices.end(), {first, first + 1u, first + 3u, first + 3u, first + 1u, first + 2u});
    }
  }

  //! Encode a marker (a start marker when the payload's kind says so). The tests below draw from it.
  FM::ModuleMatrix Encode(const FM::Payload& payload, const FM::StartMetadata& metadata = {})
  {
    FM::ModuleMatrix matrix;
    EXPECT_TRUE(FM::GenerateModules(payload, matrix, metadata));
    return matrix;
  }

  FM::ModuleMatrix EncodeStart(const FM::Payload& payload, const FM::StartMetadata& metadata)
  {
    return Encode(payload.WithKind(FM::MarkerKind::SequenceStart), metadata);
  }

  std::size_t GenerateQuads(const FM::Payload& payload, const FM::Options& options, const FP::Point origin, const std::span<FM::MarkerQuad> dst)
  {
    return FM::ModulesToQuads(Encode(payload), options, origin, dst);
  }

  std::size_t GenerateStartQuads(const FM::Payload& payload, const FM::StartMetadata& metadata, const FM::Options& options, const FP::Point origin,
                                 const std::span<FM::MarkerQuad> dst)
  {
    return FM::ModulesToQuads(EncodeStart(payload, metadata), options, origin, dst);
  }

  std::size_t GenerateTriangles(const FM::Payload& payload, const FM::Options& options, const FP::Point origin, const std::span<FM::Vertex> dst)
  {
    return FM::ModulesToTriangles(Encode(payload), options, origin, dst);
  }

  std::size_t GenerateStartTriangles(const FM::Payload& payload, const FM::StartMetadata& metadata, const FM::Options& options,
                                     const FP::Point origin, const std::span<FM::Vertex> dst)
  {
    return FM::ModulesToTriangles(EncodeStart(payload, metadata), options, origin, dst);
  }

  FM::IndexedCount GenerateIndexed(const FM::Payload& payload, const FM::Options& options, const FP::Point origin,
                                   const std::span<FM::Vertex> dstVertices, const std::span<uint32_t> dstIndices, const uint32_t baseVertex = 0)
  {
    return FM::ModulesToIndexed(Encode(payload), options, origin, dstVertices, dstIndices, baseVertex);
  }

  FM::IndexedCount GenerateStartIndexed(const FM::Payload& payload, const FM::StartMetadata& metadata, const FM::Options& options,
                                        const FP::Point origin, const std::span<FM::Vertex> dstVertices, const std::span<uint32_t> dstIndices,
                                        const uint32_t baseVertex = 0)
  {
    return FM::ModulesToIndexed(EncodeStart(payload, metadata), options, origin, dstVertices, dstIndices, baseVertex);
  }

  std::vector<FM::MarkerQuad> Generate(const FM::Payload& payload, const FM::Options& options, const FP::Point origin)
  {
    std::vector<FM::MarkerQuad> quads(FM::MaxQuadCount());
    const std::size_t count = GenerateQuads(payload, options, origin, quads);
    quads.resize(count);
    return quads;
  }

  //! Software rasterization with pixel-edge vertices, 128 = untouched. Returns false if a quad leaves the canvas.
  bool Rasterize(const std::vector<FM::MarkerQuad>& quads, const int32_t width, const int32_t height, std::vector<uint8_t>& rPixels)
  {
    rPixels.assign(static_cast<std::size_t>(width) * static_cast<std::size_t>(height), 128u);
    for (const FM::MarkerQuad& quad : quads)
    {
      if (quad.Rect.Left() < 0 || quad.Rect.Top() < 0 || quad.Rect.Right() > width || quad.Rect.Bottom() > height)
      {
        return false;
      }
      for (int32_t y = quad.Rect.Top(); y < quad.Rect.Bottom(); ++y)
      {
        for (int32_t x = quad.Rect.Left(); x < quad.Rect.Right(); ++x)
        {
          rPixels[(static_cast<std::size_t>(y) * static_cast<std::size_t>(width)) + static_cast<std::size_t>(x)] = quad.Dark ? 0u : 255u;
        }
      }
    }
    return true;
  }
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// Payload
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(Payload, EncodeProducesTheDocumentedLittleEndianLayout)
{
  const FM::Payload payload{FM::MarkerKind::SequenceEnd,
                            0x21222324u,
                            0x0102030405060708u,
                            FM::MarkerFlags::StaticAfter,
                            FP::TimeSpan{0x1112131415161718},
                            FP::TimeSpan32{0x71727374u},
                            FP::TimeSpan32{0x41424344u},
                            FP::TickCount64{0x3132333435363738},
                            FP::TickCount64{0x5152535455565758},
                            FP::TimeSpan32{0x61626364u}};
  const auto bytes = PayloadBytes(payload);
  // magic, version, kind | run id | frame index | flags | animation time | preferred, target frame time | intended display time |
  // CPU start time | CPU busy
  const std::array<uint8_t, FM::WireFormat::PayloadByteCount> expected{
    'M',   'F',   1u,    2u,    0x24u, 0x23u, 0x22u, 0x21u, 0x08u, 0x07u, 0x06u, 0x05u, 0x04u, 0x03u, 0x02u, 0x01u, 0x01u, 0x18u,
    0x17u, 0x16u, 0x15u, 0x14u, 0x13u, 0x12u, 0x11u, 0x74u, 0x73u, 0x72u, 0x71u, 0x44u, 0x43u, 0x42u, 0x41u, 0x38u, 0x37u, 0x36u,
    0x35u, 0x34u, 0x33u, 0x32u, 0x31u, 0x58u, 0x57u, 0x56u, 0x55u, 0x54u, 0x53u, 0x52u, 0x51u, 0x64u, 0x63u, 0x62u, 0x61u};
  EXPECT_EQ(FM::WireFormat::PayloadByteCount, 53u);
  EXPECT_TRUE(std::equal(bytes.begin(), bytes.end(), expected.begin(), expected.end()));
}

TEST(Payload, StartMarkerAppendsTheStartTimeAndTheSequenceIdInOrder)
{
  const FM::Payload payload{FM::MarkerKind::SequenceStart,
                            3u,
                            1u,
                            FM::MarkerFlags::None,
                            FP::TimeSpan{2},
                            UnknownFrameTime,
                            FP::TimeSpan32{5u},
                            FP::TickCount64{4},
                            FP::TickCount64{6},
                            FP::TimeSpan32{7u}};
  FM::StartMetadata metadata{0x6162636465666768, {}};
  for (std::size_t i = 0; i < FM::SequenceId::ByteCount; ++i)
  {
    metadata.Id.Bytes[i] = static_cast<uint8_t>(0xA0u + i);
  }
  std::array<uint8_t, FM::Payload::MaxEncodedByteCount> buffer{};
  ASSERT_EQ(FM::EncodePayload(payload, metadata, buffer), FM::WireFormat::StartPayloadByteCount);
  EXPECT_EQ(FM::WireFormat::StartPayloadByteCount, 77u);
  EXPECT_EQ(FM::Payload::MaxEncodedByteCount, FM::WireFormat::StartPayloadByteCount);

  const auto header = PayloadBytes(payload);
  EXPECT_TRUE(std::equal(header.begin(), header.begin() + FM::WireFormat::PayloadByteCount, buffer.begin())) << "the header comes first";
  const std::array<uint8_t, 8> utcTicks{0x68u, 0x67u, 0x66u, 0x65u, 0x64u, 0x63u, 0x62u, 0x61u};
  EXPECT_TRUE(std::equal(utcTicks.begin(), utcTicks.end(), buffer.begin() + 53));
  for (std::size_t i = 0; i < FM::SequenceId::ByteCount; ++i)
  {
    EXPECT_EQ(buffer[61u + i], 0xA0u + i) << "byte " << (61u + i);
  }
}

TEST(Payload, NegativeTicksAreStoredAsTwosComplement)
{
  const auto bytes = PayloadBytes({FM::MarkerKind::Frame, 0u, 0u, FM::MarkerFlags::None, FP::TimeSpan{-1}});
  for (std::size_t i = 17; i < 25; ++i)
  {
    EXPECT_EQ(bytes[i], 0xFFu) << "byte " << i;
  }
}

TEST(Payload, RoundTrips)
{
  constexpr uint32_t U32Max = std::numeric_limits<uint32_t>::max();
  const std::array<FM::Payload, 18> payloads{{
    {FM::MarkerKind::Frame, 0u, 0u, FM::MarkerFlags::None, FP::TimeSpan{0}},
    {FM::MarkerKind::Frame, 7u, 1u, FM::MarkerFlags::None, FP::TimeSpan{166'667}},
    {FM::MarkerKind::Frame, 7u, 2u, FM::MarkerFlags::None, FP::TimeSpan{333'334}, UnknownFrameTime, FrameTime60, FP::TickCount64{1'234'567'890'123}},
    {FM::MarkerKind::Frame, 7u, 3u, FM::MarkerFlags::None, FP::TimeSpan{500'001}, UnknownFrameTime, FrameTime60, FP::TickCount64{1'234'568'056'790},
     FP::TickCount64{1'234'567'723'456}},
    {FM::MarkerKind::Frame, 7u, 3u, FM::MarkerFlags::None, FP::TimeSpan{500'001}, UnknownFrameTime, FrameTime60, FP::TickCount64{1'234'568'056'790},
     FP::TickCount64{1'234'567'723'456}, FP::TimeSpan32{80'000u}},
    {FM::MarkerKind::Frame, U32Max, std::numeric_limits<uint64_t>::max(), FM::MarkerFlags::None, MaxAnimation, UnknownFrameTime, MaxFrameTime,
     MinClock, MaxClock, MaxFrameTime},
    {FM::MarkerKind::Frame, 2u, 8u, FM::MarkerFlags::None, FP::TimeSpan{1}, UnknownFrameTime, FP::TimeSpan32{4u}, FP::TickCount64{3},
     FP::TickCount64{5}, MaxFrameTime},
    {FM::MarkerKind::SequenceEnd, 2u, 4u, FM::MarkerFlags::None, FP::TimeSpan{1}, UnknownFrameTime, FP::TimeSpan32{4u}, FP::TickCount64{3}, MinClock,
     MaxFrameTime},
    {FM::MarkerKind::SequenceStart, 2u, 5u, FM::MarkerFlags::None, FP::TimeSpan{1}, UnknownFrameTime, FP::TimeSpan32{4u}, FP::TickCount64{3},
     FP::TickCount64{-5}, FP::TimeSpan32{80'000u}},
    {FM::MarkerKind::SequenceStart, 1u, 7u, FM::MarkerFlags::None, MinAnimation},
    {FM::MarkerKind::SequenceEnd, 3u, 42u, FM::MarkerFlags::None, FP::TimeSpan{-1}},
    {FM::MarkerKind::SequenceStart, 0u, 0u, FM::MarkerFlags::None, FP::TimeSpan{0}},
    {FM::MarkerKind::Frame, 2u, 9u, FM::MarkerFlags::None, FP::TimeSpan{1}, FrameTime60, FrameTime30, FP::TickCount64{3}, FP::TickCount64{5},
     FP::TimeSpan32{6u}},
    {FM::MarkerKind::Frame, 2u, 10u, FM::MarkerFlags::StaticAfter, FP::TimeSpan{1}, FM::Payload::OnDemandFrameTime, FM::Payload::OnDemandFrameTime,
     UnknownTime, UnknownTime, UnknownFrameTime},
    {FM::MarkerKind::SequenceStart, 2u, 11u, FM::MarkerFlags::StaticAfter, FP::TimeSpan{1}, FP::TimeSpan32{10'000'000u}, FP::TimeSpan32{4u},
     FP::TickCount64{3}, FP::TickCount64{5}, FP::TimeSpan32{6u}},
    {FM::MarkerKind::Frame, 2u, 13u, FM::MarkerFlags::StaticBefore, FP::TimeSpan{1}, FP::TimeSpan32{7u}, FP::TimeSpan32{4u}, FP::TickCount64{3},
     FP::TickCount64{5}, FP::TimeSpan32{6u}},
    {FM::MarkerKind::SequenceEnd, 2u, 14u, FM::MarkerFlags::StaticAfter | FM::MarkerFlags::StaticBefore, FP::TimeSpan{1}, FP::TimeSpan32{7u},
     FP::TimeSpan32{4u}, FP::TickCount64{3}, FP::TickCount64{5}, FP::TimeSpan32{6u}},
    // A reserved bit survives the round trip
    {FM::MarkerKind::SequenceEnd, 2u, 12u, static_cast<FM::MarkerFlags>(0x81u), FP::TimeSpan{1}, FP::TimeSpan32{7u}, FP::TimeSpan32{4u},
     FP::TickCount64{3}, FP::TickCount64{5}, FP::TimeSpan32{6u}},
  }};
  for (const FM::Payload& payload : payloads)
  {
    SCOPED_TRACE(testing::PrintToString(payload));
    std::array<uint8_t, FM::Payload::MaxEncodedByteCount> buffer{};
    const std::size_t byteCount = FM::EncodePayload(payload, {}, buffer);
    ASSERT_GT(byteCount, 0u);
    FM::Payload decoded{};
    ASSERT_TRUE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(byteCount), decoded));
    EXPECT_EQ(decoded, payload);
  }
}

TEST(Payload, ConstructorsKeepItPlainData)
{
  static_assert(std::is_trivially_copyable_v<FM::Payload>);
  static_assert(std::is_standard_layout_v<FM::Payload>);
  constexpr FM::Payload Empty;
  static_assert(Empty.Kind() == FM::MarkerKind::Frame && Empty.FrameIndex() == 0u && Empty.Flags() == FM::MarkerFlags::None);
  // The fields in the order of the wire format; the timing fields default to 0 (unknown)
  constexpr FM::Payload Required(FM::MarkerKind::SequenceEnd, 1u, 2u, FM::MarkerFlags::StaticBefore, FP::TimeSpan{3});
  static_assert(Required == FM::Payload(FM::MarkerKind::SequenceEnd, 1u, 2u, FM::MarkerFlags::StaticBefore, FP::TimeSpan{3}, UnknownFrameTime,
                                        UnknownFrameTime, UnknownTime, UnknownTime, UnknownFrameTime));
  constexpr FM::Payload All(FM::MarkerKind::Frame, 1u, 2u, FM::MarkerFlags::StaticAfter, FP::TimeSpan{3}, FP::TimeSpan32{4u}, FP::TimeSpan32{5u},
                            FP::TickCount64{6}, FP::TickCount64{7}, FP::TimeSpan32{8u});
  static_assert(All.PreferredFrameTime().Ticks() == 4u && All.TargetFrameTime().Ticks() == 5u && All.IntendedDisplayTime().Ticks() == 6 &&
                All.CpuStartTime().Ticks() == 7 && All.CpuBusy().Ticks() == 8u);
  EXPECT_EQ(All.RunId(), 1u);
}

TEST(Payload, WithKindKeepsEveryOtherValue)
{
  constexpr FM::Payload Frame(FM::MarkerKind::Frame, 1u, 2u, FM::MarkerFlags::StaticAfter, FP::TimeSpan{3}, FP::TimeSpan32{4u}, FP::TimeSpan32{5u},
                              FP::TickCount64{6}, FP::TickCount64{7}, FP::TimeSpan32{8u});
  static_assert(Frame.WithKind(FM::MarkerKind::SequenceEnd) == FM::Payload(FM::MarkerKind::SequenceEnd, 1u, 2u, FM::MarkerFlags::StaticAfter,
                                                                           FP::TimeSpan{3}, FP::TimeSpan32{4u}, FP::TimeSpan32{5u},
                                                                           FP::TickCount64{6}, FP::TickCount64{7}, FP::TimeSpan32{8u}));
  static_assert(Frame.WithKind(FM::MarkerKind::Frame) == Frame);
}

TEST(Payload, AnUnknownKindIsAsserted)
{
  constexpr auto Unknown = static_cast<FM::MarkerKind>(4u);
#ifdef NDEBUG
  // Without asserts nothing encodes it
  const FM::Payload payload(Unknown, 1u, 2u, FM::MarkerFlags::None, FP::TimeSpan{3});
  std::array<uint8_t, FM::Payload::MaxEncodedByteCount> buffer{};
  EXPECT_EQ(FM::EncodePayload(payload, {}, buffer), 0u);
  FM::ModuleMatrix matrix;
  EXPECT_FALSE(FM::GenerateModules(payload, matrix));
  EXPECT_EQ(FM::EncodePayload(FM::Payload().WithKind(Unknown), {}, buffer), 0u);
#elif GTEST_HAS_DEATH_TEST
  EXPECT_DEATH(static_cast<void>(FM::Payload(Unknown, 1u, 2u, FM::MarkerFlags::None, FP::TimeSpan{3})), "");
  EXPECT_DEATH(static_cast<void>(FM::Payload().WithKind(Unknown)), "");
#else
  GTEST_SKIP() << "asserts are on and death tests are not available";
#endif
}

TEST(Payload, MarkerFlagsCombine)
{
  constexpr auto Reserved = static_cast<FM::MarkerFlags>(0x80u);
  static_assert(FM::HasFlag(FM::MarkerFlags::StaticAfter | Reserved, FM::MarkerFlags::StaticAfter));
  static_assert(!FM::HasFlag(Reserved, FM::MarkerFlags::StaticAfter));
  static_assert((FM::MarkerFlags::StaticAfter & Reserved) == FM::MarkerFlags::None);
  static_assert(!FM::HasFlag(FM::MarkerFlags::StaticAfter, FM::MarkerFlags::StaticBefore));
  static_assert(FM::HasFlag(FM::MarkerFlags::StaticAfter | FM::MarkerFlags::StaticBefore, FM::MarkerFlags::StaticBefore));
  EXPECT_EQ(static_cast<uint8_t>(FM::MarkerFlags::StaticBefore), 0x02u);
  static_assert(FM::Payload::OnDemandFrameTime.Ticks() == std::numeric_limits<uint32_t>::max());
  EXPECT_EQ(static_cast<uint8_t>(FM::MarkerFlags::StaticAfter | Reserved), 0x81u);
  // At run time too
  const FM::MarkerFlags both = FM::MarkerFlags::StaticAfter | FM::MarkerFlags::StaticBefore;
  EXPECT_TRUE(FM::HasFlag(both, FM::MarkerFlags::StaticBefore));
  EXPECT_FALSE(FM::HasFlag(Reserved, FM::MarkerFlags::StaticAfter));
  EXPECT_EQ(static_cast<uint8_t>(both & Reserved), 0u);
}

TEST(Payload, StartMarkerNeedsItsMetadataBlock)
{
  auto header = PayloadBytes({FM::MarkerKind::SequenceStart, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}});
  header.resize(FM::WireFormat::PayloadByteCount);
  FM::Payload decoded{};
  EXPECT_FALSE(FM::TryDecodePayload(header, decoded));
}

TEST(Payload, TryDecodeRejectsBadInput)
{
  auto bytes = PayloadBytes({FM::MarkerKind::Frame, 0u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}});
  FM::Payload decoded{};
  EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(bytes).first(FM::WireFormat::PayloadByteCount - 1), decoded));
  EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(bytes).first(FM::WireFormat::SyncPayloadByteCount - 1), decoded));
  bytes[0] = 'X';
  EXPECT_FALSE(FM::TryDecodePayload(bytes, decoded));
  bytes[0] = 'M';
  bytes[1] = 'X';
  EXPECT_FALSE(FM::TryDecodePayload(bytes, decoded));
  bytes[1] = 'F';
  bytes[2] = 2u;
  EXPECT_FALSE(FM::TryDecodePayload(bytes, decoded));
  bytes[2] = 1u;
  bytes[3] = FM::WireFormat::MaxMarkerKindValue + 1u;
  EXPECT_FALSE(FM::TryDecodePayload(bytes, decoded));
  bytes[3] = 0u;
  EXPECT_TRUE(FM::TryDecodePayload(bytes, decoded));
}

TEST(Payload, DecodingASyncMarkerResetsTheMetadata)
{
  const std::vector<uint8_t> bytes = PayloadBytes({FM::MarkerKind::Sync, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{}});
  FM::Payload decoded{};
  FM::StartMetadata metadata{9, FM::SequenceId{{1u, 2u, 3u}}};
  ASSERT_TRUE(FM::TryDecodePayload(bytes, decoded, &metadata));
  EXPECT_EQ(metadata.UtcTicks, 0);
  EXPECT_EQ(metadata.Id, FM::SequenceId{});
}

TEST(Payload, TryDecodeRejectsWrongLengths)
{
  std::array<uint8_t, FM::Payload::MaxEncodedByteCount + 1> buffer{};
  FM::Payload decoded{};
  for (const FM::MarkerKind kind : {FM::MarkerKind::Frame, FM::MarkerKind::SequenceEnd})
  {
    ASSERT_EQ(FM::EncodePayload({kind, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}, UnknownFrameTime, FP::TimeSpan32{5u}, FP::TickCount64{4},
                                 FP::TickCount64{6}, FP::TimeSpan32{7u}},
                                {}, buffer),
              53u);
    EXPECT_TRUE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(53), decoded));
    EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(52), decoded));
    EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(54), decoded));
  }
  ASSERT_EQ(FM::EncodePayload({FM::MarkerKind::SequenceStart, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}, UnknownFrameTime, FP::TimeSpan32{5u},
                               FP::TickCount64{4}, FP::TickCount64{6}, FP::TimeSpan32{7u}},
                              {9, {}}, buffer),
            77u);
  EXPECT_TRUE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(77), decoded));
  EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(76), decoded));
  EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(78), decoded));
}

TEST(Payload, StartMetadataRoundTrips)
{
  FM::SequenceId textId;
  ASSERT_TRUE(FM::SequenceId::TryFromText("Benchmark run 42", textId));
  FM::SequenceId byteId;
  for (std::size_t i = 0; i < FM::SequenceId::ByteCount; ++i)
  {
    byteId.Bytes[i] = static_cast<uint8_t>(0xFFu - (i * 17u));
  }
  const std::array<FM::StartMetadata, 4> cases{{
    {638'000'000'000'000'000, textId},
    {-1, byteId},
    {0, {}},
    {std::numeric_limits<int64_t>::max(), byteId},
  }};
  const FM::Payload payload{FM::MarkerKind::SequenceStart,
                            30u,
                            10u,
                            FM::MarkerFlags::None,
                            FP::TimeSpan{20},
                            UnknownFrameTime,
                            FP::TimeSpan32{50u},
                            FP::TickCount64{40},
                            FP::TickCount64{60},
                            FP::TimeSpan32{70u}};
  for (const FM::StartMetadata& expected : cases)
  {
    SCOPED_TRACE(testing::PrintToString(expected.Id));
    std::array<uint8_t, FM::Payload::MaxEncodedByteCount + 1> buffer{};
    const std::size_t byteCount = FM::EncodePayload(payload, expected, buffer);
    ASSERT_EQ(byteCount, FM::WireFormat::StartPayloadByteCount);

    FM::Payload decoded{};
    FM::StartMetadata metadata{1, byteId};
    ASSERT_TRUE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(byteCount), decoded, &metadata));
    EXPECT_EQ(decoded, payload);
    EXPECT_EQ(metadata, expected);

    // A start payload must be exactly StartPayloadByteCount bytes
    EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(byteCount - 1), decoded, &metadata));
    EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(byteCount + 1), decoded, &metadata));
    EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(FM::WireFormat::PayloadByteCount), decoded, &metadata));
  }

  // Frame and end payloads ignore the metadata and stay PayloadByteCount bytes, and decoding them resets the metadata
  std::array<uint8_t, FM::Payload::MaxEncodedByteCount> buffer{};
  EXPECT_EQ(FM::EncodePayload({FM::MarkerKind::Frame, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}}, {5, textId}, buffer),
            FM::WireFormat::PayloadByteCount);
  EXPECT_EQ(FM::EncodePayload({FM::MarkerKind::SequenceEnd, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}}, {5, textId}, buffer),
            FM::WireFormat::PayloadByteCount);
  FM::Payload decoded{};
  FM::StartMetadata metadata{5, textId};
  ASSERT_TRUE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(FM::WireFormat::PayloadByteCount), decoded, &metadata));
  EXPECT_EQ(metadata, FM::StartMetadata{});

  // The start payload does not fit a smaller destination
  std::array<uint8_t, FM::WireFormat::StartPayloadByteCount - 1> small{};
  EXPECT_EQ(FM::EncodePayload(payload, {5, textId}, small), 0u);
}

TEST(SequenceId, TryFromTextPadsWithZeros)
{
  FM::SequenceId id;
  ASSERT_TRUE(FM::SequenceId::TryFromText("golden-run", id));
  const std::array<uint8_t, FM::SequenceId::ByteCount> expected{'g', 'o', 'l', 'd', 'e', 'n', '-', 'r', 'u', 'n', 0u, 0u, 0u, 0u, 0u, 0u};
  EXPECT_EQ(id.Bytes, expected);
  EXPECT_FALSE(id.IsEmpty());
  EXPECT_TRUE(FM::SequenceId{}.IsEmpty());

  // 1 to 16 printable ASCII characters, space and tilde included
  ASSERT_TRUE(FM::SequenceId::TryFromText("x", id));
  EXPECT_EQ(id.Bytes[0], 'x');
  EXPECT_EQ(id.Bytes[1], 0u);
  ASSERT_TRUE(FM::SequenceId::TryFromText(" ~0123456789abcd", id));
  EXPECT_EQ(id.Bytes[0], ' ');
  EXPECT_EQ(id.Bytes[1], '~');
  EXPECT_EQ(id.Bytes[15], 'd');

  // Constant evaluation works too
  static_assert(
    []
    {
      FM::SequenceId value;
      return FM::SequenceId::TryFromText("constexpr", value) && value.Bytes[8] == 'r' && value.Bytes[9] == 0u;
    }());
}

TEST(SequenceId, TryFromTextRejectsOtherText)
{
  FM::SequenceId id;
  ASSERT_TRUE(FM::SequenceId::TryFromText("keep", id));
  const FM::SequenceId kept = id;
  EXPECT_FALSE(FM::SequenceId::TryFromText("", id));
  EXPECT_FALSE(FM::SequenceId::TryFromText("0123456789abcdefg", id));
  EXPECT_FALSE(FM::SequenceId::TryFromText("tab\there", id));
  EXPECT_FALSE(FM::SequenceId::TryFromText("del\x7F", id));
  EXPECT_FALSE(FM::SequenceId::TryFromText("\xC3\xA6", id));
  EXPECT_FALSE(FM::SequenceId::TryFromText(std::string_view("nul\0", 4), id));
  EXPECT_EQ(id, kept);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// Symbol and geometry
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(Geometry, MarkerSize)
{
  EXPECT_EQ(FM::Options{}.MarkerSizePx(FM::MarkerKind::Sync), 198);
  EXPECT_EQ(FM::Options{}.MarkerSizePx(), 294);
  EXPECT_EQ(FM::Options(3, 4).MarkerSizePx(), 147);
  EXPECT_EQ(FM::Options(12, 4).MarkerSizePx(), 588);
  EXPECT_EQ(FM::Options(1, 0).MarkerSizePx(), 41);
}

TEST(Options, TheDefaultIsSixPixelModulesAndTheRecommendedQuietZone)
{
  static_assert(FM::Options{}.ModuleSizePx() == FM::Options::DefaultModuleSizePx && FM::Options::DefaultModuleSizePx == 6);
  static_assert(FM::Options{}.QuietZoneModules() == FM::Options::RecommendedQuietZoneModules);
  static_assert(FM::Options{} == FM::Options(6, 4));
  static_assert(FM::Options(3).QuietZoneModules() == FM::Options::RecommendedQuietZoneModules);
  static_assert(FM::Options(3, 4).QuietZonePx() == 12);
  // The limits themselves are valid
  static_assert(FM::Options(FM::Options::MinModuleSizePx, 0).ModuleSizePx() == FM::Options::MinModuleSizePx);
  static_assert(FM::Options(FM::Options::MaxModuleSizePx, FM::Options::MaxQuietZoneModules).QuietZoneModules() == FM::Options::MaxQuietZoneModules);
}

TEST(Options, AValueOutsideItsRangeIsAsserted)
{
#ifdef NDEBUG
  // Without asserts it is clamped into its range: Options are always valid
  EXPECT_EQ(FM::Options(0, 4), FM::Options(FM::Options::MinModuleSizePx, 4));
  EXPECT_EQ(FM::Options(FM::Options::MaxModuleSizePx + 1, 4), FM::Options(FM::Options::MaxModuleSizePx, 4));
  EXPECT_EQ(FM::Options(6, -1), FM::Options(6, 0));
  EXPECT_EQ(FM::Options(6, FM::Options::MaxQuietZoneModules + 1), FM::Options(6, FM::Options::MaxQuietZoneModules));
#elif GTEST_HAS_DEATH_TEST
  EXPECT_DEATH(static_cast<void>(FM::Options(0, 4)), "");
  EXPECT_DEATH(static_cast<void>(FM::Options(FM::Options::MaxModuleSizePx + 1, 4)), "");
  EXPECT_DEATH(static_cast<void>(FM::Options(6, -1)), "");
  EXPECT_DEATH(static_cast<void>(FM::Options(6, FM::Options::MaxQuietZoneModules + 1)), "");
#else
  GTEST_SKIP() << "asserts are on and death tests are not available";
#endif
}

TEST(Geometry, TooSmallDestinationGeneratesNothing)
{
  std::vector<FM::MarkerQuad> quads(10);
  EXPECT_EQ(GenerateQuads({}, FM::Options{}, {}, quads), 0u);
  EXPECT_EQ(GenerateQuads({}, FM::Options{}, {}, {}), 0u) << "not even the background";
}

TEST(Symbol, SyncMarkersAreVersion2)
{
  FM::ModuleMatrix matrix;
  ASSERT_TRUE(FM::GenerateModules(
    {FM::MarkerKind::Sync, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}, UnknownFrameTime, FP::TimeSpan32{5u}, FP::TickCount64{4}}, matrix));
  EXPECT_EQ(matrix.Size(), FM::ModuleMatrix::SyncSize);
  EXPECT_EQ(matrix.Size(), 25);

  const FM::Options options{3, 4};
  std::vector<FM::MarkerQuad> quads(FM::MaxQuadCount());
  const std::size_t count = GenerateQuads({FM::MarkerKind::Sync, 0u, 7u, FM::MarkerFlags::None, FP::TimeSpan{0}}, options, {10, 20}, quads);
  ASSERT_GT(count, 0u);
  EXPECT_EQ(quads.front(), (FM::MarkerQuad{FP::Rectangle(10, 20, 99, 99), false}));
}

TEST(Payload, SyncMarkerCarriesOnlyTheRunIdAndTheFrameIndex)
{
  std::array<uint8_t, FM::Payload::MaxEncodedByteCount> buffer{};
  const FM::Payload payload{FM::MarkerKind::Sync, 4u,
                            0x0102030405060708u,  FM::MarkerFlags::None,
                            FP::TimeSpan{123},    UnknownFrameTime,
                            FP::TimeSpan32{6u},   FP::TickCount64{5},
                            FP::TickCount64{7},   FP::TimeSpan32{8u}};
  const std::size_t byteCount = FM::EncodePayload(payload, {}, buffer);
  ASSERT_EQ(byteCount, FM::WireFormat::SyncPayloadByteCount);
  const std::array<uint8_t, FM::WireFormat::SyncPayloadByteCount> expected{'M',   'F',   1u,    3u,    0x04u, 0x00u, 0x00u, 0x00u,
                                                                           0x08u, 0x07u, 0x06u, 0x05u, 0x04u, 0x03u, 0x02u, 0x01u};
  EXPECT_TRUE(std::equal(expected.begin(), expected.end(), buffer.begin()));

  FM::Payload decoded{};
  ASSERT_TRUE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(byteCount), decoded));
  EXPECT_EQ(decoded, (FM::Payload{FM::MarkerKind::Sync, payload.RunId(), payload.FrameIndex(), FM::MarkerFlags::None, FP::TimeSpan{0}}));
  EXPECT_EQ(decoded.CpuStartTime().Ticks(), 0);
  EXPECT_EQ(decoded.CpuBusy().Ticks(), 0u);
  EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(byteCount + 1), decoded));
}

TEST(Symbol, EveryMarkerIsVersion6)
{
  FM::ModuleMatrix matrix;
  ASSERT_TRUE(FM::GenerateModules({FM::MarkerKind::Frame, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}}, matrix));
  EXPECT_EQ(matrix.Size(), 41);
  ASSERT_TRUE(FM::GenerateModules({FM::MarkerKind::SequenceEnd, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}}, matrix));
  EXPECT_EQ(matrix.Size(), 41);
  ASSERT_TRUE(FM::GenerateModules({FM::MarkerKind::SequenceStart, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}}, matrix, {}));
  EXPECT_EQ(matrix.Size(), 41);

  FM::SequenceId id;
  ASSERT_TRUE(FM::SequenceId::TryFromText("0123456789abcdef", id));
  ASSERT_TRUE(FM::GenerateModules({FM::MarkerKind::SequenceStart, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}, UnknownFrameTime,
                                   FP::TimeSpan32{5u}, FP::TickCount64{4}, FP::TickCount64{6}, FP::TimeSpan32{7u}},
                                  matrix, {123, id}));
  EXPECT_EQ(matrix.Size(), FM::ModuleMatrix::MainSize);

  // Version 6-M holds 106 bytes: the start marker leaves room for future fields
  EXPECT_EQ(FM::Payload::MaxEncodedByteCount, 77u);
  EXPECT_LT(FM::Payload::MaxEncodedByteCount, FM::WireFormat::QrCapacityBytes);
}

TEST(Geometry, StartQuadsStayWithinTheMarkerSizeAndMaxQuadCount)
{
  FM::SequenceId id;
  id.Bytes.fill(0xFFu);
  const FM::Options options{};
  const FP::Point origin{32, 32};
  std::vector<FM::MarkerQuad> quads(FM::MaxQuadCount());
  const std::size_t count =
    GenerateStartQuads({FM::MarkerKind::Frame, 7u, 5u, FM::MarkerFlags::None, FP::TimeSpan{6}}, {99, id}, options, origin, quads);
  ASSERT_GT(count, 0u);
  ASSERT_LE(count, FM::MaxQuadCount());
  const FM::MarkerQuad& background = quads.front();
  EXPECT_EQ(background.Rect.Left(), origin.X);
  EXPECT_EQ(background.Rect.Top(), origin.Y);
  EXPECT_EQ(background.Rect.Right() - background.Rect.Left(), options.MarkerSizePx());
  for (std::size_t i = 1; i < count; ++i)
  {
    EXPECT_LE(quads[i].Rect.Right(), background.Rect.Right()) << "quad " << i;
    EXPECT_LE(quads[i].Rect.Bottom(), background.Rect.Bottom()) << "quad " << i;
  }
}

TEST(Geometry, QuadsArePixelAlignedAndReproduceTheModuleMatrix)
{
  const FM::Payload payload{FM::MarkerKind::Frame, 0u, 123'456'789u, FM::MarkerFlags::None, FP::TimeSpan{36'000'000'000}};
  for (const int32_t moduleSize : {1, 2, 3, 6})
  {
    for (const int32_t quiet : {0, 1, 4})
    {
      SCOPED_TRACE("moduleSize " + std::to_string(moduleSize) + ", quiet " + std::to_string(quiet));
      const FM::Options options{moduleSize, quiet};
      const FP::Point origin{5, 7};
      const auto quads = Generate(payload, options, origin);
      const int32_t size = options.MarkerSizePx();

      ASSERT_FALSE(quads.empty());
      ASSERT_LE(quads.size(), FM::MaxQuadCount());
      EXPECT_EQ(quads.front(), (FM::MarkerQuad{FP::Rectangle(origin.X, origin.Y, size, size), false}));

      for (std::size_t i = 1; i < quads.size(); ++i)
      {
        const FM::MarkerQuad& quad = quads[i];
        EXPECT_TRUE(quad.Dark) << "quad " << i;
        EXPECT_LT(quad.Rect.Left(), quad.Rect.Right()) << "quad " << i;
        EXPECT_EQ(quad.Rect.Bottom() - quad.Rect.Top(), moduleSize) << "quad " << i;
        EXPECT_EQ((quad.Rect.Left() - origin.X) % moduleSize, 0) << "quad " << i;
        EXPECT_EQ((quad.Rect.Right() - origin.X) % moduleSize, 0) << "quad " << i;
        EXPECT_EQ((quad.Rect.Top() - origin.Y) % moduleSize, 0) << "quad " << i;
        // Dark quads never overlap: runs within a row are separated, rows are disjoint.
        for (std::size_t j = i + 1; j < quads.size(); ++j)
        {
          const FM::MarkerQuad& other = quads[j];
          const bool overlap = quad.Rect.Left() < other.Rect.Right() && other.Rect.Left() < quad.Rect.Right() &&
                               quad.Rect.Top() < other.Rect.Bottom() && other.Rect.Top() < quad.Rect.Bottom();
          EXPECT_FALSE(overlap) << "quads " << i << " and " << j;
        }
      }

      FM::ModuleMatrix matrix;
      ASSERT_TRUE(FM::GenerateModules(payload, matrix));
      const int32_t width = origin.X + size + 3;
      const int32_t height = origin.Y + size + 3;
      std::vector<uint8_t> pixels;
      ASSERT_TRUE(Rasterize(quads, width, height, pixels)) << "a quad lies outside the expected marker area";

      // Compare every pixel, but report a single failure with the first mismatch instead of one per pixel
      std::size_t mismatches = 0;
      std::string firstMismatch;
      for (int32_t y = 0; y < height; ++y)
      {
        for (int32_t x = 0; x < width; ++x)
        {
          const uint8_t pixel = pixels[(static_cast<std::size_t>(y) * static_cast<std::size_t>(width)) + static_cast<std::size_t>(x)];
          const int32_t mx = x - origin.X;
          const int32_t my = y - origin.Y;
          uint8_t expected = 128u;
          if (mx >= 0 && my >= 0 && mx < size && my < size)
          {
            const int32_t moduleX = (mx / moduleSize) - quiet;
            const int32_t moduleY = (my / moduleSize) - quiet;
            const bool inSymbol = moduleX >= 0 && moduleY >= 0 && moduleX < matrix.Size() && moduleY < matrix.Size();
            expected = inSymbol && matrix.IsDark(moduleX, moduleY) ? 0u : 255u;
          }
          if (pixel != expected)
          {
            if (mismatches == 0)
            {
              firstMismatch =
                "pixel (" + std::to_string(x) + "," + std::to_string(y) + ") is " + std::to_string(pixel) + ", expected " + std::to_string(expected);
            }
            ++mismatches;
          }
        }
      }
      EXPECT_EQ(mismatches, 0u) << firstMismatch;
    }
  }
}

TEST(Symbol, DifferentPayloadsGiveDifferentSymbols)
{
  FM::ModuleMatrix a;
  FM::ModuleMatrix b;
  ASSERT_TRUE(FM::GenerateModules({FM::MarkerKind::Frame, 0u, 1u, FM::MarkerFlags::None, FP::TimeSpan{0}}, a));
  ASSERT_TRUE(FM::GenerateModules({FM::MarkerKind::Frame, 0u, 2u, FM::MarkerFlags::None, FP::TimeSpan{0}}, b));
  EXPECT_NE(a, b);
}

TEST(Symbol, FinderPatternsArePresent)
{
  FM::ModuleMatrix matrix;
  ASSERT_TRUE(FM::GenerateModules({FM::MarkerKind::Frame, 0u, 99u, FM::MarkerFlags::None, FP::TimeSpan{99}}, matrix));
  // Top-left finder: 7x7 dark ring with a dark 3x3 centre.
  for (int32_t i = 0; i < 7; ++i)
  {
    EXPECT_TRUE(matrix.IsDark(i, 0)) << i;
    EXPECT_TRUE(matrix.IsDark(i, 6)) << i;
    EXPECT_TRUE(matrix.IsDark(0, i)) << i;
    EXPECT_TRUE(matrix.IsDark(6, i)) << i;
  }
  EXPECT_FALSE(matrix.IsDark(1, 1));
  EXPECT_TRUE(matrix.IsDark(3, 3));
  EXPECT_TRUE(matrix.IsDark(FM::ModuleMatrix::MainSize - 1, 0));
  EXPECT_TRUE(matrix.IsDark(0, FM::ModuleMatrix::MainSize - 1));
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// Vertex order
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(Vertices, TheBackgroundQuadIsDrawnInTheDocumentedOrder)
{
  const FM::ModuleMatrix matrix = Encode({FM::MarkerKind::Frame, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}});
  const FM::Options options{2, 4};
  const int32_t size = options.MarkerSizePx();
  std::vector<FM::Vertex> vertices(FM::MaxTriangleVertexCount());
  ASSERT_GT(FM::ModulesToTriangles(matrix, options, {10, 20}, vertices), 6u);
  const std::array<FM::Vertex, 6> expected{
    {{10, 20, 255u}, {10 + size, 20, 255u}, {10, 20 + size, 255u}, {10, 20 + size, 255u}, {10 + size, 20, 255u}, {10 + size, 20 + size, 255u}}};
  EXPECT_TRUE(std::equal(expected.begin(), expected.end(), vertices.begin()));

  std::vector<FM::Vertex> indexedVertices(FM::MaxIndexedVertexCount());
  std::vector<uint32_t> indices(FM::MaxIndexCount());
  ASSERT_GT(FM::ModulesToIndexed(matrix, options, {10, 20}, indexedVertices, indices, 100u).IndexCount, 6u);
  const std::array<FM::Vertex, 4> corners{{{10, 20, 255u}, {10 + size, 20, 255u}, {10 + size, 20 + size, 255u}, {10, 20 + size, 255u}}};
  EXPECT_TRUE(std::equal(corners.begin(), corners.end(), indexedVertices.begin()));
  const std::array<uint32_t, 12> expectedIndices{100u, 101u, 103u, 103u, 101u, 102u, 104u, 105u, 107u, 107u, 105u, 106u};
  EXPECT_TRUE(std::equal(expectedIndices.begin(), expectedIndices.end(), indices.begin()));
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// Triangle output (ModulesToTriangles / ModulesToIndexed)
// ---------------------------------------------------------------------------------------------------------------------------------------------

namespace
{
  //! One marker to generate: a frame, end or start marker (the start marker with metadata).
  struct TriangleCase
  {
    FM::Payload Payload;
    FM::SequenceId StartId;
    FM::Options Options;
    FP::Point Origin;
  };

  std::vector<TriangleCase> TriangleCases()
  {
    std::vector<TriangleCase> cases;
    for (const int32_t moduleSize : {1, 2, 3, 6})
    {
      for (const int32_t quietZone : {0, 4})
      {
        const FM::Options options{moduleSize, quietZone};
        cases.push_back({{FM::MarkerKind::Frame, 3u, 42u, FM::MarkerFlags::None, FP::TimeSpan{1'234'567}, UnknownFrameTime, FrameTime60,
                          FP::TickCount64{987'654'321}, FP::TickCount64{987'487'654}},
                         {},
                         options,
                         {5, 7}});
        cases.push_back({{FM::MarkerKind::SequenceEnd, 3u, 43u, FM::MarkerFlags::None, FP::TimeSpan{1'400'234}}, {}, options, {0, 0}});
        FM::SequenceId textId;
        FM::SequenceId::TryFromText("triangle-case", textId);
        FM::SequenceId fullId;
        fullId.Bytes.fill(0xFFu);
        for (const FM::SequenceId& id : {FM::SequenceId{}, textId, fullId})
        {
          cases.push_back({{FM::MarkerKind::SequenceStart, 3u, 41u, FM::MarkerFlags::None, FP::TimeSpan{1'067'890}}, id, options, {32, 64}});
        }
      }
    }
    return cases;
  }

  std::vector<FM::MarkerQuad> GenerateCase(const TriangleCase& testCase)
  {
    std::vector<FM::MarkerQuad> quads(FM::MaxQuadCount());
    const std::size_t count = testCase.Payload.Kind() == FM::MarkerKind::SequenceStart
                                ? GenerateStartQuads(testCase.Payload, {1, testCase.StartId}, testCase.Options, testCase.Origin, quads)
                                : GenerateQuads(testCase.Payload, testCase.Options, testCase.Origin, quads);
    quads.resize(count);
    return quads;
  }

  std::vector<FM::Vertex> GenerateCaseTriangles(const TriangleCase& testCase)
  {
    std::vector<FM::Vertex> vertices(FM::MaxTriangleVertexCount());
    const std::size_t count = testCase.Payload.Kind() == FM::MarkerKind::SequenceStart
                                ? GenerateStartTriangles(testCase.Payload, {1, testCase.StartId}, testCase.Options, testCase.Origin, vertices)
                                : GenerateTriangles(testCase.Payload, testCase.Options, testCase.Origin, vertices);
    vertices.resize(count);
    return vertices;
  }

  //! Rasterize a triangle list like a GPU samples pixel centres: pixel (x,y) is covered when its centre (x+0.5, y+0.5) lies inside the
  //! triangle or on one of its edges. With vertices on pixel corners no centre lies on an axis aligned edge; centres on the diagonal are
  //! shared by the two same coloured triangles of one quad, so the tie rule cannot change the image. Triangles are drawn in order.
  //! Returns false if a vertex lies outside the canvas.
  bool RasterizeTriangles(const std::vector<FM::Vertex>& vertices, const int32_t width, const int32_t height, std::vector<uint8_t>& rPixels)
  {
    rPixels.assign(static_cast<std::size_t>(width) * static_cast<std::size_t>(height), 128u);
    // Doubled coordinates keep the pixel centres integral
    const auto edge = [](const FM::Vertex& a, const FM::Vertex& b, const int64_t px, const int64_t py)
    {
      return ((2 * static_cast<int64_t>(b.X - a.X)) * (py - (2 * static_cast<int64_t>(a.Y)))) -
             ((2 * static_cast<int64_t>(b.Y - a.Y)) * (px - (2 * static_cast<int64_t>(a.X))));
    };
    for (std::size_t i = 0; i + 2 < vertices.size(); i += 3)
    {
      const FM::Vertex& v0 = vertices[i];
      const FM::Vertex& v1 = vertices[i + 1];
      const FM::Vertex& v2 = vertices[i + 2];
      for (const FM::Vertex& v : {v0, v1, v2})
      {
        if (v.X < 0 || v.Y < 0 || v.X > width || v.Y > height)
        {
          return false;
        }
      }
      const int32_t minX = std::min({v0.X, v1.X, v2.X});
      const int32_t maxX = std::max({v0.X, v1.X, v2.X});
      const int32_t minY = std::min({v0.Y, v1.Y, v2.Y});
      const int32_t maxY = std::max({v0.Y, v1.Y, v2.Y});
      for (int32_t y = minY; y < maxY; ++y)
      {
        for (int32_t x = minX; x < maxX; ++x)
        {
          const int64_t px = (2 * static_cast<int64_t>(x)) + 1;
          const int64_t py = (2 * static_cast<int64_t>(y)) + 1;
          const int64_t e0 = edge(v0, v1, px, py);
          const int64_t e1 = edge(v1, v2, px, py);
          const int64_t e2 = edge(v2, v0, px, py);
          if ((e0 >= 0 && e1 >= 0 && e2 >= 0) || (e0 <= 0 && e1 <= 0 && e2 <= 0))
          {
            rPixels[(static_cast<std::size_t>(y) * static_cast<std::size_t>(width)) + static_cast<std::size_t>(x)] = v0.Luma;
          }
        }
      }
    }
    return true;
  }

  std::vector<FM::Vertex> ExpandIndexed(const std::vector<FM::Vertex>& vertices, const std::vector<uint32_t>& indices, const uint32_t baseVertex)
  {
    std::vector<FM::Vertex> expanded;
    expanded.reserve(indices.size());
    for (const uint32_t index : indices)
    {
      expanded.push_back(vertices.at(index - baseVertex));
    }
    return expanded;
  }
}

TEST(Triangles, DirectOutputEqualsTheConvertedQuads)
{
  for (const TriangleCase& testCase : TriangleCases())
  {
    SCOPED_TRACE(testing::Message() << "kind " << static_cast<uint32_t>(testCase.Payload.Kind()) << ", module " << testCase.Options.ModuleSizePx()
                                    << ", quiet " << testCase.Options.QuietZoneModules() << ", id " << testing::PrintToString(testCase.StartId));
    const std::vector<FM::MarkerQuad> quads = GenerateCase(testCase);
    ASSERT_FALSE(quads.empty());

    EXPECT_EQ(GenerateCaseTriangles(testCase), ToTriangles(quads));

    constexpr uint32_t BaseVertex = 1000u;
    std::vector<FM::Vertex> convertedVertices;
    std::vector<uint32_t> convertedIndices;
    ToIndexed(quads, BaseVertex, convertedVertices, convertedIndices);

    std::vector<FM::Vertex> vertices(FM::MaxIndexedVertexCount());
    std::vector<uint32_t> indices(FM::MaxIndexCount());
    const FM::IndexedCount count =
      testCase.Payload.Kind() == FM::MarkerKind::SequenceStart
        ? GenerateStartIndexed(testCase.Payload, {1, testCase.StartId}, testCase.Options, testCase.Origin, vertices, indices, BaseVertex)
        : GenerateIndexed(testCase.Payload, testCase.Options, testCase.Origin, vertices, indices, BaseVertex);
    vertices.resize(count.VertexCount);
    indices.resize(count.IndexCount);
    EXPECT_EQ(vertices, convertedVertices);
    EXPECT_EQ(indices, convertedIndices);
  }
}

TEST(Triangles, RasterizedTrianglesReproduceTheQuads)
{
  for (const TriangleCase& testCase : TriangleCases())
  {
    SCOPED_TRACE(testing::Message() << "kind " << static_cast<uint32_t>(testCase.Payload.Kind()) << ", module " << testCase.Options.ModuleSizePx()
                                    << ", quiet " << testCase.Options.QuietZoneModules());
    const int32_t size = testCase.Origin.Y + testCase.Options.MarkerSizePx() + 8;
    std::vector<uint8_t> fromQuads;
    ASSERT_TRUE(Rasterize(GenerateCase(testCase), size, size, fromQuads));

    std::vector<uint8_t> fromTriangles;
    ASSERT_TRUE(RasterizeTriangles(GenerateCaseTriangles(testCase), size, size, fromTriangles));
    EXPECT_EQ(fromTriangles, fromQuads) << "the triangle list must cover exactly the pixels of the quads";

    std::vector<FM::Vertex> vertices(FM::MaxIndexedVertexCount());
    std::vector<uint32_t> indices(FM::MaxIndexCount());
    const FM::IndexedCount count =
      testCase.Payload.Kind() == FM::MarkerKind::SequenceStart
        ? GenerateStartIndexed(testCase.Payload, {1, testCase.StartId}, testCase.Options, testCase.Origin, vertices, indices)
        : GenerateIndexed(testCase.Payload, testCase.Options, testCase.Origin, vertices, indices);
    vertices.resize(count.VertexCount);
    indices.resize(count.IndexCount);
    std::vector<uint8_t> fromIndexed;
    ASSERT_TRUE(RasterizeTriangles(ExpandIndexed(vertices, indices, 0u), size, size, fromIndexed));
    EXPECT_EQ(fromIndexed, fromQuads) << "the indexed triangle list must cover exactly the pixels of the quads";
  }
}

TEST(Triangles, FrameMarkersFitTheBufferSizes)
{
  std::vector<FM::Vertex> vertices(FM::MaxTriangleVertexCount());
  std::vector<FM::Vertex> indexedVertices(FM::MaxIndexedVertexCount());
  std::vector<uint32_t> indices(FM::MaxIndexCount());
  for (uint64_t frame = 0; frame < 500u; ++frame)
  {
    const FM::Payload payload{FM::MarkerKind::Frame, 9u, frame * 7919u, FM::MarkerFlags::None, FP::TimeSpan{static_cast<int64_t>(frame) * 166'667}};
    EXPECT_GT(GenerateTriangles(payload, {}, {0, 0}, vertices), 0u) << "frame " << frame;
    EXPECT_GT(GenerateIndexed(payload, {}, {0, 0}, indexedVertices, indices).IndexCount, 0u) << "frame " << frame;
  }
}

TEST(Triangles, SmallBuffersGenerateNothing)
{
  const FM::Payload payload{FM::MarkerKind::Frame, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}};
  std::vector<FM::Vertex> vertices(FM::MaxTriangleVertexCount());
  std::vector<FM::Vertex> tooFew(12);
  EXPECT_EQ(GenerateTriangles(payload, {}, {0, 0}, tooFew), 0u);
  std::vector<uint32_t> tooFewIndices(12);
  const FM::IndexedCount failed = GenerateIndexed(payload, {}, {0, 0}, vertices, tooFewIndices);
  EXPECT_EQ(failed.VertexCount, 0u);
  EXPECT_EQ(failed.IndexCount, 0u);
  // Not even the background
  std::vector<FM::Vertex> fiveVertices(5);
  EXPECT_EQ(GenerateTriangles(payload, {}, {0, 0}, fiveVertices), 0u);
  std::vector<FM::Vertex> threeVertices(3);
  std::vector<uint32_t> indices(FM::MaxIndexCount());
  EXPECT_EQ(GenerateIndexed(payload, {}, {0, 0}, threeVertices, indices).VertexCount, 0u);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// Sizing and placement (doc/marker-format.md "Sizing" and "Location")
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(Sizing, ModuleSizeRecommendationsMatchTheDocumentation)
{
  // 1:1
  EXPECT_EQ(FM::Options::Minimum(1080, 1080).ModuleSizePx(), 2);
  EXPECT_EQ(FM::Options::Recommended(1080, 1080).ModuleSizePx(), 3);
  EXPECT_EQ(FM::Options::Recommended(1080, 1080, true).ModuleSizePx(), 4);
  // 1440p -> 1080p
  EXPECT_EQ(FM::Options::Minimum(1440, 1080).ModuleSizePx(), 3);
  EXPECT_EQ(FM::Options::Recommended(1440, 1080).ModuleSizePx(), 4);
  // 1080p -> 540p and 2160p -> 1080p
  EXPECT_EQ(FM::Options::Minimum(1080, 540).ModuleSizePx(), 4);
  EXPECT_EQ(FM::Options::Recommended(1080, 540).ModuleSizePx(), 6);
  EXPECT_EQ(FM::Options::Recommended(2160, 1080).ModuleSizePx(), 6);
  EXPECT_EQ(FM::Options::Recommended(1080, 540, true).ModuleSizePx(), 8);
  // 1080p -> 360p
  EXPECT_EQ(FM::Options::Minimum(1080, 360).ModuleSizePx(), 6);
  EXPECT_EQ(FM::Options::Recommended(1080, 360).ModuleSizePx(), 9);
  // 2160p -> 540p
  EXPECT_EQ(FM::Options::Minimum(2160, 540).ModuleSizePx(), 8);
  EXPECT_EQ(FM::Options::Recommended(2160, 540).ModuleSizePx(), 12);
  // Upscaling never goes below the stored pixel count
  EXPECT_EQ(FM::Options::Recommended(540, 1080).ModuleSizePx(), 3);
  // Invalid heights fall back to 1:1
  EXPECT_EQ(FM::Options::Recommended(0, 1080).ModuleSizePx(), 3);
  EXPECT_EQ(FM::Options::Recommended(1080, 0).ModuleSizePx(), 3);
  // A downscale beyond the largest module size gives the largest, and the recommended quiet zone
  EXPECT_EQ(FM::Options::Recommended(1'000'000, 10).ModuleSizePx(), FM::Options::MaxModuleSizePx);
  EXPECT_EQ(FM::Options::Minimum(1080, 540), FM::Options(4, FM::Options::RecommendedQuietZoneModules));
  static_assert(FM::Options::Recommended(1080, 540) == FM::Options(6, 4));
}

TEST(Sizing, RecommendedOrigins)
{
  const FM::Options options{};
  // The main marker top-left, the sync marker bottom-left
  EXPECT_EQ(options.RecommendedOrigin(FM::MarkerKind::Frame, 1080), (FP::Point{32, 32}));
  EXPECT_EQ(options.RecommendedOrigin(FM::MarkerKind::SequenceStart, 1080), (FP::Point{32, 32}));
  EXPECT_EQ(options.RecommendedOrigin(FM::MarkerKind::Sync, 1080), (FP::Point{32, 1080 - 32 - 198}));
  // Aligned to a 3:1 downscale ratio
  EXPECT_EQ(options.RecommendedOrigin(FM::MarkerKind::Frame, 1080, 3), (FP::Point{33, 33}));
  EXPECT_EQ(options.RecommendedOrigin(FM::MarkerKind::Sync, 1080, 3), (FP::Point{33, 849}));
  static_assert(FM::Options{}.RecommendedOrigin(FM::MarkerKind::Frame, 1080, 4) == FP::Point{32, 32});
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// The encoded marker (ModuleMatrix) and the bitmap drawn from it
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(ModuleMatrix, BitsArePackedRowMajorMostSignificantBitFirst)
{
  for (const FM::MarkerKind kind : {FM::MarkerKind::Frame, FM::MarkerKind::Sync})
  {
    const FM::ModuleMatrix matrix = Encode({kind, 9u, 12345u, FM::MarkerFlags::None, FP::TimeSpan{678}});
    const int32_t size = matrix.Size();
    const auto bits = matrix.Bits();
    ASSERT_EQ(bits.size(), FM::ModuleMatrix::PackedModuleByteCount(size));
    for (int32_t y = 0; y < size; ++y)
    {
      for (int32_t x = 0; x < size; ++x)
      {
        const auto index = (static_cast<std::size_t>(y) * static_cast<std::size_t>(size)) + static_cast<std::size_t>(x);
        const bool bit = ((static_cast<uint32_t>(bits[index / 8u]) >> (7u - (index % 8u))) & 1u) != 0u;
        ASSERT_EQ(matrix.IsDark(x, y), bit) << x << "," << y;
      }
    }
  }
  EXPECT_EQ(FM::ModuleMatrix::PackedModuleByteCount(FM::ModuleMatrix::MainSize), 211u);
  EXPECT_EQ(FM::ModuleMatrix::PackedModuleByteCount(FM::ModuleMatrix::SyncSize), 79u);
  EXPECT_EQ(FM::ModuleMatrix::PackedModuleByteCount(0), 0u);
  EXPECT_EQ(FM::ModuleMatrix::PackedModuleByteCount(-1), 0u);
}

TEST(ModuleMatrix, TryFromBitsTakesTheMarkerSizesAndIgnoresThePadding)
{
  const FM::ModuleMatrix matrix = Encode({FM::MarkerKind::Sync, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}});
  std::array<uint8_t, FM::ModuleMatrix::MaxPackedModuleByteCount> bits{};
  std::copy(matrix.Bits().begin(), matrix.Bits().end(), bits.begin());
  bits[FM::ModuleMatrix::PackedModuleByteCount(25) - 1u] |= 0x7Fu;    // 625 modules: the last byte uses 1 bit

  FM::ModuleMatrix copy;
  ASSERT_TRUE(FM::ModuleMatrix::TryFromBits(25, bits, copy));
  EXPECT_EQ(copy, matrix);
  EXPECT_FALSE(FM::ModuleMatrix::TryFromBits(24, bits, copy));
  EXPECT_FALSE(FM::ModuleMatrix::TryFromBits(17, bits, copy));
  EXPECT_FALSE(FM::ModuleMatrix::TryFromBits(0, bits, copy));
  EXPECT_FALSE(FM::ModuleMatrix::TryFromBits(-25, bits, copy));
  EXPECT_FALSE(FM::ModuleMatrix::TryFromBits(45, bits, copy));
  EXPECT_FALSE(FM::ModuleMatrix::TryFromBits(25, std::span<const uint8_t>(bits).first(78), copy));
  // The other QR sizes are not markers: nothing draws their grid
  for (const int32_t size : {21, 29, 33, 37})
  {
    EXPECT_FALSE(FM::ModuleMatrix::TryFromBits(size, bits, copy)) << size;
  }
  EXPECT_EQ(copy, matrix) << "a refused call leaves the matrix unchanged";
  EXPECT_NE(copy, FM::ModuleMatrix{});

  const FM::ModuleMatrix main = Encode({FM::MarkerKind::Frame, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}});
  ASSERT_TRUE(FM::ModuleMatrix::TryFromBits(FM::ModuleMatrix::MainSize, main.Bits(), copy));
  EXPECT_EQ(copy, main);
  EXPECT_FALSE(FM::ModuleMatrix::TryFromBits(FM::ModuleMatrix::MainSize, main.Bits().first(210), copy));
}

TEST(ModuleMatrix, AnEmptyMatrixDrawsNothing)
{
  const FM::ModuleMatrix empty;
  std::array<FM::MarkerQuad, 4> quads{};
  std::array<uint8_t, 16> pixels{};
  EXPECT_EQ(empty.Size(), 0);
  EXPECT_EQ(FM::ModulesToQuads(empty, {}, {}, quads), 0u);
  std::array<FM::Vertex, 8> vertices{};
  std::array<uint32_t, 12> indices{};
  EXPECT_EQ(FM::ModulesToTriangles(empty, {}, {}, vertices), 0u);
  EXPECT_EQ(FM::ModulesToIndexed(empty, {}, {}, vertices, indices).VertexCount, 0u);
  EXPECT_FALSE(FM::ModulesToBitmap(empty, FM::Options(1, 0), {}, pixels, 4, 4, FM::PixelFormat::R8));
}

namespace
{
  struct BitmapCase
  {
    FM::Payload Payload;
    FM::Options Options;
    FP::Point Origin;
  };

  const std::array<BitmapCase, 5> g_bitmapCases{{
    {{FM::MarkerKind::Frame, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}}, FM::Options(3, 4), {5, 7}},
    {{FM::MarkerKind::SequenceEnd, 1u, 99u, FM::MarkerFlags::None, FP::TimeSpan{-5}}, FM::Options(1, 0), {0, 0}},
    {{FM::MarkerKind::Sync, 0u, 7u, FM::MarkerFlags::None, FP::TimeSpan{0}}, FM::Options(2, 4), {3, 1}},
    {{FM::MarkerKind::Frame, 2u, 0xFFFFFFFFFFFFFFFFu, FM::MarkerFlags::None, FP::TimeSpan{1}, UnknownFrameTime, FP::TimeSpan32{4u},
      FP::TickCount64{3}, FP::TickCount64{5}, FP::TimeSpan32{6u}},
     FM::Options(2, 1),
     {-9, -4}},
    {{FM::MarkerKind::Frame, 7u, 5u, FM::MarkerFlags::None, FP::TimeSpan{6}}, FM::Options(4, 2), {100, 60}},
  }};
}

TEST(Bitmap, EqualsTheRasterizedQuadsInEveryPixelFormat)
{
  constexpr int32_t Width = 173;
  constexpr int32_t Height = 131;
  for (const BitmapCase& testCase : g_bitmapCases)
  {
    SCOPED_TRACE(testing::PrintToString(testCase.Payload));
    const FM::ModuleMatrix matrix = Encode(testCase.Payload);
    std::vector<FM::MarkerQuad> quads(FM::MaxQuadCount());
    quads.resize(FM::ModulesToQuads(matrix, testCase.Options, testCase.Origin, quads));

    // The expected pixels: every quad clipped to the canvas, over untouched pixels of 128
    std::vector<uint8_t> expected(static_cast<std::size_t>(Width) * static_cast<std::size_t>(Height), 128u);
    for (const FM::MarkerQuad& quad : quads)
    {
      for (int32_t y = std::max(quad.Rect.Top(), 0); y < std::min(quad.Rect.Bottom(), Height); ++y)
      {
        for (int32_t x = std::max(quad.Rect.Left(), 0); x < std::min(quad.Rect.Right(), Width); ++x)
        {
          expected[(static_cast<std::size_t>(y) * Width) + static_cast<std::size_t>(x)] = quad.Dark ? 0u : 255u;
        }
      }
    }

    for (const FM::PixelFormat format : {FM::PixelFormat::R8, FM::PixelFormat::R8G8B8, FM::PixelFormat::R8G8B8A8})
    {
      const auto bytesPerPixel = static_cast<std::size_t>(FM::PixelFormatUtil::BytesPerPixel(format));
      const std::size_t stride = (static_cast<std::size_t>(Width) * bytesPerPixel) + 5u;    // padded rows
      std::vector<uint8_t> pixels(stride * static_cast<std::size_t>(Height), 128u);
      ASSERT_TRUE(FM::ModulesToBitmap(matrix, testCase.Options, testCase.Origin, pixels, Width, Height, format, stride));
      for (int32_t y = 0; y < Height; ++y)
      {
        for (int32_t x = 0; x < Width; ++x)
        {
          const uint8_t luma = expected[(static_cast<std::size_t>(y) * Width) + static_cast<std::size_t>(x)];
          const std::size_t at = (static_cast<std::size_t>(y) * stride) + (static_cast<std::size_t>(x) * bytesPerPixel);
          for (std::size_t channel = 0; channel < bytesPerPixel; ++channel)
          {
            const uint8_t wanted = channel == 3u && luma != 128u ? 255u : luma;
            ASSERT_EQ(pixels[at + channel], wanted) << "pixel " << x << "," << y << " channel " << channel;
          }
        }
        // The row padding is never written
        for (std::size_t pad = static_cast<std::size_t>(Width) * bytesPerPixel; pad < stride; ++pad)
        {
          ASSERT_EQ(pixels[(static_cast<std::size_t>(y) * stride) + pad], 128u);
        }
      }
    }
  }
}

TEST(Bitmap, AModuleResolutionImageScaledUpEqualsTheFullSizeOne)
{
  const FM::ModuleMatrix matrix = Encode({FM::MarkerKind::Frame, 59u, 31u, FM::MarkerFlags::None, FP::TimeSpan{41}});
  constexpr int32_t ModuleSize = 3;
  const FM::Options small{1, 4};
  const FM::Options large{ModuleSize, 4};
  const int32_t smallSize = small.MarkerSizePx();
  const int32_t largeSize = large.MarkerSizePx();
  std::vector<uint8_t> modules(static_cast<std::size_t>(smallSize) * static_cast<std::size_t>(smallSize));
  std::vector<uint8_t> pixels(static_cast<std::size_t>(largeSize) * static_cast<std::size_t>(largeSize));
  ASSERT_TRUE(FM::ModulesToBitmap(matrix, small, {0, 0}, modules, smallSize, smallSize, FM::PixelFormat::R8));
  ASSERT_TRUE(FM::ModulesToBitmap(matrix, large, {0, 0}, pixels, largeSize, largeSize, FM::PixelFormat::R8));
  for (int32_t y = 0; y < largeSize; ++y)
  {
    for (int32_t x = 0; x < largeSize; ++x)
    {
      ASSERT_EQ(pixels[(static_cast<std::size_t>(y) * largeSize) + static_cast<std::size_t>(x)],
                modules[(static_cast<std::size_t>(y / ModuleSize) * smallSize) + static_cast<std::size_t>(x / ModuleSize)])
        << x << "," << y;
    }
  }
}

TEST(Bitmap, RefusesInvalidArgumentsWithoutWriting)
{
  const FM::ModuleMatrix matrix = Encode({FM::MarkerKind::Frame, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}});
  std::vector<uint8_t> pixels(std::size_t{64} * 64u * 4u, 128u);
  EXPECT_FALSE(FM::ModulesToBitmap(matrix, FM::Options(1, 4), {}, pixels, 64, 64, FM::PixelFormat::R8G8B8, 64u * 3u - 1u)) << "short stride";
  EXPECT_FALSE(
    FM::ModulesToBitmap(matrix, FM::Options(1, 4), {}, std::span<uint8_t>(pixels).first((64u * 64u * 4u) - 1u), 64, 64, FM::PixelFormat::R8G8B8A8))
    << "too small";
  EXPECT_FALSE(FM::ModulesToBitmap(matrix, FM::Options(1, 4), {}, pixels, -1, 64, FM::PixelFormat::R8)) << "negative width";
  EXPECT_FALSE(FM::ModulesToBitmap(matrix, FM::Options(1, 4), {}, pixels, 64, -1, FM::PixelFormat::R8)) << "negative height";
  EXPECT_TRUE(FM::ModulesToBitmap(matrix, FM::Options(1, 4), {}, pixels, 64, 0, FM::PixelFormat::R8)) << "no rows: nothing to draw";
  EXPECT_TRUE(std::all_of(pixels.begin(), pixels.end(), [](const uint8_t value) { return value == 128u; }));
  EXPECT_TRUE(FM::ModulesToBitmap(matrix, FM::Options(1, 4), {500, 500}, pixels, 64, 64, FM::PixelFormat::R8))
    << "outside the buffer: nothing to draw";
  EXPECT_TRUE(std::all_of(pixels.begin(), pixels.end(), [](const uint8_t value) { return value == 128u; }));
  // A stride longer than the whole buffer cannot be right with more than one row, and is refused before it is multiplied by the rows
  // (63 of these wrap a size_t; the origin is outside, so a wrong 'true' writes nothing)
  constexpr std::size_t HugeStride = (std::numeric_limits<std::size_t>::max() / 63u) + 1u;
  EXPECT_FALSE(FM::ModulesToBitmap(matrix, FM::Options(1, 4), {500, 500}, pixels, 64, 64, FM::PixelFormat::R8, HugeStride));
  EXPECT_FALSE(FM::ModulesToBitmap(matrix, FM::Options(1, 4), {500, 500}, pixels, 64, 64, FM::PixelFormat::R8, pixels.size() + 1u));
  EXPECT_TRUE(FM::ModulesToBitmap(matrix, FM::Options(1, 4), {500, 500}, pixels, 64, 1, FM::PixelFormat::R8, HugeStride))
    << "one row: the stride is never stepped";
  EXPECT_TRUE(FM::ModulesToBitmap(matrix, FM::Options(1, 4), {500, 500}, pixels, 64, 2, FM::PixelFormat::R8, pixels.size() - 64u))
    << "two rows that just fit";
}

TEST(Bitmap, BytesPerPixel)
{
  EXPECT_EQ(FM::PixelFormatUtil::BytesPerPixel(FM::PixelFormat::R8), 1);
  EXPECT_EQ(FM::PixelFormatUtil::BytesPerPixel(FM::PixelFormat::R8G8B8), 3);
  EXPECT_EQ(FM::PixelFormatUtil::BytesPerPixel(FM::PixelFormat::R8G8B8A8), 4);
  EXPECT_EQ(FM::PixelFormatUtil::BytesPerPixel(static_cast<FM::PixelFormat>(0xFFu)), 1) << "a value without a name counts as R8";
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// The static grid and the per-frame grid indices
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(Grid, VertexCountsFit16BitIndices)
{
  EXPECT_EQ(FM::GridVertexCount(FM::MarkerKind::Frame), 1768u);
  EXPECT_EQ(FM::GridVertexCount(FM::MarkerKind::SequenceStart), 1768u);
  EXPECT_EQ(FM::GridVertexCount(FM::MarkerKind::Sync), 680u);
  EXPECT_EQ(FM::MaxGridVertexCount(), 1768u);
  EXPECT_LT(FM::MaxGridVertexCount(), 65536u);
}

TEST(Grid, ResolvedIndicesEqualTheIndexedTrianglesTriangleByTriangle)
{
  constexpr uint32_t BaseVertex = 100u;
  const std::array<FM::Payload, 5> payloads{{
    {FM::MarkerKind::Frame, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}},
    {FM::MarkerKind::SequenceEnd, 1u, 99u, FM::MarkerFlags::None, FP::TimeSpan{-5}},
    {FM::MarkerKind::Sync, 0u, 7u, FM::MarkerFlags::None, FP::TimeSpan{0}},
    {FM::MarkerKind::Frame, 2u, 0xFFFFFFFFFFFFFFFFu, FM::MarkerFlags::None, FP::TimeSpan{1}, UnknownFrameTime, FP::TimeSpan32{4u}, FP::TickCount64{3},
     FP::TickCount64{5}, FP::TimeSpan32{6u}},
    {FM::MarkerKind::SequenceStart, 7u, 5u, FM::MarkerFlags::None, FP::TimeSpan{6}},
  }};
  for (const FM::Payload& payload : payloads)
  {
    for (const FM::Options options : {FM::Options(1, 0), FM::Options(3, 4), FM::Options(6, 2)})
    {
      SCOPED_TRACE(testing::Message() << testing::PrintToString(payload) << ", module " << options.ModuleSizePx());
      const FP::Point origin{17, 23};
      const FM::ModuleMatrix matrix = Encode(payload);
      std::vector<FM::Vertex> grid(FM::MaxGridVertexCount());
      ASSERT_EQ(FM::GridVertices(payload.Kind(), options, origin, grid), FM::GridVertexCount(payload.Kind()));
      std::vector<uint32_t> gridIndices(FM::MaxIndexCount());
      gridIndices.resize(FM::ModulesToGridIndices(matrix, gridIndices, BaseVertex));
      ASSERT_FALSE(gridIndices.empty());

      std::vector<FM::Vertex> vertices(FM::MaxIndexedVertexCount());
      std::vector<uint32_t> indices(FM::MaxIndexCount());
      const FM::IndexedCount count = FM::ModulesToIndexed(matrix, options, origin, vertices, indices, BaseVertex);
      indices.resize(count.IndexCount);
      ASSERT_EQ(gridIndices.size(), indices.size());
      for (std::size_t i = 0; i < indices.size(); ++i)
      {
        ASSERT_EQ(grid[gridIndices[i] - BaseVertex], vertices[indices[i] - BaseVertex]) << "index " << i;
      }
    }
  }
}

TEST(Grid, SmallBuffersGiveNothing)
{
  std::vector<FM::Vertex> grid(FM::MaxGridVertexCount());
  EXPECT_EQ(FM::GridVertices(FM::MarkerKind::Frame, {}, {}, std::span<FM::Vertex>(grid).first(1767)), 0u);
  EXPECT_EQ(FM::GridVertices(FM::MarkerKind::Sync, {}, {}, std::span<FM::Vertex>(grid).first(680)), 680u);
  std::array<uint32_t, 12> indices{};
  EXPECT_EQ(FM::ModulesToGridIndices(Encode({FM::MarkerKind::Frame, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}}), indices), 0u);
  EXPECT_EQ(FM::ModulesToGridIndices(FM::ModuleMatrix{}, indices), 0u);
  EXPECT_EQ(
    FM::ModulesToGridIndices(Encode({FM::MarkerKind::Sync, 3u, 1u, FM::MarkerFlags::None, FP::TimeSpan{2}}), std::span<uint32_t>(indices).first(5)),
    0u)
    << "not even the background";
}
