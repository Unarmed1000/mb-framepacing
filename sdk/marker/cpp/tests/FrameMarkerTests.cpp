// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framemarker/FrameMarker.hpp>
#include <mb/framemarker/Version.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <limits>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace FM = MB::FrameMarker;

namespace MB::FrameMarker
{
  // Readable gtest failure output for the value types
  void PrintTo(const Payload& value, std::ostream* os)
  {
    *os << "{frame " << value.FrameIndex << ", ticks " << value.AnimationTicks << ", run " << value.RunId << ", kind "
        << static_cast<uint32_t>(value.Kind) << ", intended " << value.IntendedDisplayTicks << ", target " << value.TargetFrameTicks << ", cpu start "
        << value.CpuStartTicks << ", cpu busy " << value.CpuBusyTicks << "}";
  }

  void PrintTo(const SequenceId& value, std::ostream* os)
  {
    constexpr std::string_view Digits = "0123456789abcdef";
    for (const uint8_t byte : value.Bytes)
    {
      *os << Digits[byte >> 4u] << Digits[byte & 0xFu];
    }
  }

  void PrintTo(const Quad& value, std::ostream* os)
  {
    *os << "{" << value.Left << "," << value.Top << "," << value.Right << "," << value.Bottom << (value.Dark ? " dark}" : " light}");
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
  //! The payload's bytes (the header for frame, end and sync markers), through the one EncodePayload.
  std::vector<uint8_t> PayloadBytes(const FM::Payload& payload)
  {
    std::array<uint8_t, FM::MaxEncodedPayloadByteCount> buffer{};
    const std::size_t count = FM::EncodePayload(payload, {}, buffer);
    return {buffer.begin(), buffer.begin() + static_cast<std::ptrdiff_t>(count)};
  }

  //! The documented vertex order of a quad: (TL, TR, BL) (BL, TR, BR), written independently of the library.
  std::vector<FM::Vertex> ToTriangles(const std::vector<FM::Quad>& quads)
  {
    std::vector<FM::Vertex> vertices;
    for (const FM::Quad& quad : quads)
    {
      const uint8_t luma = quad.Dark ? 0u : 255u;
      vertices.insert(vertices.end(), {{quad.Left, quad.Top, luma},
                                       {quad.Right, quad.Top, luma},
                                       {quad.Left, quad.Bottom, luma},
                                       {quad.Left, quad.Bottom, luma},
                                       {quad.Right, quad.Top, luma},
                                       {quad.Right, quad.Bottom, luma}});
    }
    return vertices;
  }

  //! The documented indexed order: 4 vertices (TL, TR, BR, BL) and indices (0,1,3)(3,1,2) per quad, plus baseVertex.
  void ToIndexed(const std::vector<FM::Quad>& quads, const uint32_t baseVertex, std::vector<FM::Vertex>& rVertices, std::vector<uint32_t>& rIndices)
  {
    for (const FM::Quad& quad : quads)
    {
      const uint8_t luma = quad.Dark ? 0u : 255u;
      const auto first = baseVertex + static_cast<uint32_t>(rVertices.size());
      rVertices.insert(rVertices.end(),
                       {{quad.Left, quad.Top, luma}, {quad.Right, quad.Top, luma}, {quad.Right, quad.Bottom, luma}, {quad.Left, quad.Bottom, luma}});
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
    FM::Payload start = payload;
    start.Kind = FM::MarkerKind::SequenceStart;
    return Encode(start, metadata);
  }

  std::size_t GenerateQuads(const FM::Payload& payload, const FM::Options& options, const FM::Point origin, const std::span<FM::Quad> dst)
  {
    return FM::ModulesToQuads(Encode(payload), options, origin, dst);
  }

  std::size_t GenerateStartQuads(const FM::Payload& payload, const FM::StartMetadata& metadata, const FM::Options& options, const FM::Point origin,
                                 const std::span<FM::Quad> dst)
  {
    return FM::ModulesToQuads(EncodeStart(payload, metadata), options, origin, dst);
  }

  std::size_t GenerateTriangles(const FM::Payload& payload, const FM::Options& options, const FM::Point origin, const std::span<FM::Vertex> dst)
  {
    return FM::ModulesToTriangles(Encode(payload), options, origin, dst);
  }

  std::size_t GenerateStartTriangles(const FM::Payload& payload, const FM::StartMetadata& metadata, const FM::Options& options,
                                     const FM::Point origin, const std::span<FM::Vertex> dst)
  {
    return FM::ModulesToTriangles(EncodeStart(payload, metadata), options, origin, dst);
  }

  FM::IndexedCount GenerateIndexed(const FM::Payload& payload, const FM::Options& options, const FM::Point origin,
                                   const std::span<FM::Vertex> dstVertices, const std::span<uint32_t> dstIndices, const uint32_t baseVertex = 0)
  {
    return FM::ModulesToIndexed(Encode(payload), options, origin, dstVertices, dstIndices, baseVertex);
  }

  FM::IndexedCount GenerateStartIndexed(const FM::Payload& payload, const FM::StartMetadata& metadata, const FM::Options& options,
                                        const FM::Point origin, const std::span<FM::Vertex> dstVertices, const std::span<uint32_t> dstIndices,
                                        const uint32_t baseVertex = 0)
  {
    return FM::ModulesToIndexed(EncodeStart(payload, metadata), options, origin, dstVertices, dstIndices, baseVertex);
  }

  std::vector<FM::Quad> Generate(const FM::Payload& payload, const FM::Options& options, const FM::Point origin)
  {
    std::vector<FM::Quad> quads(FM::MaxQuadCount());
    const std::size_t count = GenerateQuads(payload, options, origin, quads);
    quads.resize(count);
    return quads;
  }

  //! Software rasterization with pixel-edge vertices, 128 = untouched. Returns false if a quad leaves the canvas.
  bool Rasterize(const std::vector<FM::Quad>& quads, const int32_t width, const int32_t height, std::vector<uint8_t>& rPixels)
  {
    rPixels.assign(static_cast<std::size_t>(width) * static_cast<std::size_t>(height), 128u);
    for (const FM::Quad& quad : quads)
    {
      if (quad.Left < 0 || quad.Top < 0 || quad.Right > width || quad.Bottom > height)
      {
        return false;
      }
      for (int32_t y = quad.Top; y < quad.Bottom; ++y)
      {
        for (int32_t x = quad.Left; x < quad.Right; ++x)
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
  const FM::Payload payload{0x0102030405060708u, 0x1112131415161718, 0x21222324u, FM::MarkerKind::SequenceEnd, 0x3132333435363738, 0x41424344u,
                            0x5152535455565758,  0x61626364u,        0x71727374u, FM::MarkerFlags::Static};
  const auto bytes = PayloadBytes(payload);
  // magic, version, kind | run id | frame index | flags | animation time | preferred, target frame time | intended display time |
  // CPU start time | CPU busy
  const std::array<uint8_t, FM::PayloadByteCount> expected{
    'M',   'F',   1u,    2u,    0x24u, 0x23u, 0x22u, 0x21u, 0x08u, 0x07u, 0x06u, 0x05u, 0x04u, 0x03u, 0x02u, 0x01u, 0x01u, 0x18u,
    0x17u, 0x16u, 0x15u, 0x14u, 0x13u, 0x12u, 0x11u, 0x74u, 0x73u, 0x72u, 0x71u, 0x44u, 0x43u, 0x42u, 0x41u, 0x38u, 0x37u, 0x36u,
    0x35u, 0x34u, 0x33u, 0x32u, 0x31u, 0x58u, 0x57u, 0x56u, 0x55u, 0x54u, 0x53u, 0x52u, 0x51u, 0x64u, 0x63u, 0x62u, 0x61u};
  EXPECT_EQ(FM::PayloadByteCount, 53u);
  EXPECT_TRUE(std::equal(bytes.begin(), bytes.end(), expected.begin(), expected.end()));
}

TEST(Payload, StartMarkerAppendsTheStartTimeAndTheSequenceIdInOrder)
{
  const FM::Payload payload{1u, 2, 3u, FM::MarkerKind::SequenceStart, 4, 5u, 6, 7u};
  FM::StartMetadata metadata{0x6162636465666768, {}};
  for (std::size_t i = 0; i < FM::SequenceId::ByteCount; ++i)
  {
    metadata.Id.Bytes[i] = static_cast<uint8_t>(0xA0u + i);
  }
  std::array<uint8_t, FM::MaxEncodedPayloadByteCount> buffer{};
  ASSERT_EQ(FM::EncodePayload(payload, metadata, buffer), FM::StartPayloadByteCount);
  EXPECT_EQ(FM::StartPayloadByteCount, 77u);
  EXPECT_EQ(FM::MaxEncodedPayloadByteCount, FM::StartPayloadByteCount);

  const auto header = PayloadBytes(payload);
  EXPECT_TRUE(std::equal(header.begin(), header.begin() + FM::PayloadByteCount, buffer.begin())) << "the header comes first";
  const std::array<uint8_t, 8> utcTicks{0x68u, 0x67u, 0x66u, 0x65u, 0x64u, 0x63u, 0x62u, 0x61u};
  EXPECT_TRUE(std::equal(utcTicks.begin(), utcTicks.end(), buffer.begin() + 53));
  for (std::size_t i = 0; i < FM::SequenceId::ByteCount; ++i)
  {
    EXPECT_EQ(buffer[61u + i], 0xA0u + i) << "byte " << (61u + i);
  }
}

TEST(Payload, NegativeTicksAreStoredAsTwosComplement)
{
  const auto bytes = PayloadBytes({0u, -1, 0u});
  for (std::size_t i = 17; i < 25; ++i)
  {
    EXPECT_EQ(bytes[i], 0xFFu) << "byte " << i;
  }
}

TEST(Payload, RoundTrips)
{
  constexpr uint32_t U32Max = std::numeric_limits<uint32_t>::max();
  const std::array<FM::Payload, 16> payloads{{
    {0u, 0, 0u, FM::MarkerKind::Frame},
    {1u, 166'667, 7u, FM::MarkerKind::Frame},
    {2u, 333'334, 7u, FM::MarkerKind::Frame, 1'234'567'890'123, 166'667u},
    {3u, 500'001, 7u, FM::MarkerKind::Frame, 1'234'568'056'790, 166'667u, 1'234'567'723'456},
    {3u, 500'001, 7u, FM::MarkerKind::Frame, 1'234'568'056'790, 166'667u, 1'234'567'723'456, 80'000u},
    {std::numeric_limits<uint64_t>::max(), std::numeric_limits<int64_t>::max(), U32Max, FM::MarkerKind::Frame, std::numeric_limits<int64_t>::min(),
     U32Max, std::numeric_limits<int64_t>::max(), U32Max},
    {8u, 1, 2u, FM::MarkerKind::Frame, 3, 4u, 5, U32Max},
    {4u, 1, 2u, FM::MarkerKind::SequenceEnd, 3, 4u, std::numeric_limits<int64_t>::min(), U32Max},
    {5u, 1, 2u, FM::MarkerKind::SequenceStart, 3, 4u, -5, 80'000u},
    {7u, std::numeric_limits<int64_t>::min(), 1u, FM::MarkerKind::SequenceStart},
    {42u, -1, 3u, FM::MarkerKind::SequenceEnd},
    {0u, 0, 0u, FM::MarkerKind::SequenceStart},
    {9u, 1, 2u, FM::MarkerKind::Frame, 3, 333'333u, 5, 6u, 166'667u},
    {10u, 1, 2u, FM::MarkerKind::Frame, 0, FM::OnDemandFrameTicks, 0, 0u, FM::OnDemandFrameTicks, FM::MarkerFlags::Static},
    {11u, 1, 2u, FM::MarkerKind::SequenceStart, 3, 4u, 5, 6u, 10'000'000u, FM::MarkerFlags::Static},
    // A reserved bit survives the round trip
    {12u, 1, 2u, FM::MarkerKind::SequenceEnd, 3, 4u, 5, 6u, 7u, static_cast<FM::MarkerFlags>(0x81u)},
  }};
  for (const FM::Payload& payload : payloads)
  {
    SCOPED_TRACE(testing::PrintToString(payload));
    std::array<uint8_t, FM::MaxEncodedPayloadByteCount> buffer{};
    const std::size_t byteCount = FM::EncodePayload(payload, {}, buffer);
    ASSERT_GT(byteCount, 0u);
    FM::Payload decoded{};
    ASSERT_TRUE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(byteCount), decoded));
    EXPECT_EQ(decoded, payload);
  }
}

TEST(Payload, MarkerFlagsCombine)
{
  constexpr auto Reserved = static_cast<FM::MarkerFlags>(0x80u);
  static_assert(FM::HasFlag(FM::MarkerFlags::Static | Reserved, FM::MarkerFlags::Static));
  static_assert(!FM::HasFlag(Reserved, FM::MarkerFlags::Static));
  static_assert((FM::MarkerFlags::Static & Reserved) == FM::MarkerFlags::None);
  static_assert(FM::OnDemandFrameTicks == std::numeric_limits<uint32_t>::max());
  EXPECT_EQ(static_cast<uint8_t>(FM::MarkerFlags::Static | Reserved), 0x81u);
}

TEST(Payload, StartMarkerNeedsItsMetadataBlock)
{
  auto header = PayloadBytes({1u, 2, 3u, FM::MarkerKind::SequenceStart});
  header.resize(FM::PayloadByteCount);
  FM::Payload decoded{};
  EXPECT_FALSE(FM::TryDecodePayload(header, decoded));
}

TEST(Payload, TryDecodeRejectsBadInput)
{
  auto bytes = PayloadBytes({1u, 2, 0u});
  FM::Payload decoded{};
  EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(bytes).first(FM::PayloadByteCount - 1), decoded));
  bytes[0] = 'X';
  EXPECT_FALSE(FM::TryDecodePayload(bytes, decoded));
  bytes[0] = 'M';
  bytes[2] = 2u;
  EXPECT_FALSE(FM::TryDecodePayload(bytes, decoded));
  bytes[2] = 1u;
  bytes[3] = FM::MaxMarkerKindValue + 1u;
  EXPECT_FALSE(FM::TryDecodePayload(bytes, decoded));
  bytes[3] = 0u;
  EXPECT_TRUE(FM::TryDecodePayload(bytes, decoded));
}

TEST(Payload, TryDecodeRejectsWrongLengths)
{
  std::array<uint8_t, FM::MaxEncodedPayloadByteCount + 1> buffer{};
  FM::Payload decoded{};
  for (const FM::MarkerKind kind : {FM::MarkerKind::Frame, FM::MarkerKind::SequenceEnd})
  {
    ASSERT_EQ(FM::EncodePayload({1u, 2, 3u, kind, 4, 5u, 6, 7u}, {}, buffer), 53u);
    EXPECT_TRUE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(53), decoded));
    EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(52), decoded));
    EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(54), decoded));
  }
  ASSERT_EQ(FM::EncodePayload({1u, 2, 3u, FM::MarkerKind::SequenceStart, 4, 5u, 6, 7u}, {9, {}}, buffer), 77u);
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
  const FM::Payload payload{10u, 20, 30u, FM::MarkerKind::SequenceStart, 40, 50u, 60, 70u};
  for (const FM::StartMetadata& expected : cases)
  {
    SCOPED_TRACE(testing::PrintToString(expected.Id));
    std::array<uint8_t, FM::MaxEncodedPayloadByteCount + 1> buffer{};
    const std::size_t byteCount = FM::EncodePayload(payload, expected, buffer);
    ASSERT_EQ(byteCount, FM::StartPayloadByteCount);

    FM::Payload decoded{};
    FM::StartMetadata metadata{1, byteId};
    ASSERT_TRUE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(byteCount), decoded, &metadata));
    EXPECT_EQ(decoded, payload);
    EXPECT_EQ(metadata, expected);

    // A start payload must be exactly StartPayloadByteCount bytes
    EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(byteCount - 1), decoded, &metadata));
    EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(byteCount + 1), decoded, &metadata));
    EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(FM::PayloadByteCount), decoded, &metadata));
  }

  // Frame and end payloads ignore the metadata and stay PayloadByteCount bytes, and decoding them resets the metadata
  std::array<uint8_t, FM::MaxEncodedPayloadByteCount> buffer{};
  EXPECT_EQ(FM::EncodePayload({1u, 2, 3u, FM::MarkerKind::Frame}, {5, textId}, buffer), FM::PayloadByteCount);
  EXPECT_EQ(FM::EncodePayload({1u, 2, 3u, FM::MarkerKind::SequenceEnd}, {5, textId}, buffer), FM::PayloadByteCount);
  FM::Payload decoded{};
  FM::StartMetadata metadata{5, textId};
  ASSERT_TRUE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(FM::PayloadByteCount), decoded, &metadata));
  EXPECT_EQ(metadata, FM::StartMetadata{});

  // The start payload does not fit a smaller destination
  std::array<uint8_t, FM::StartPayloadByteCount - 1> small{};
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

TEST(Payload, ToDateTimeTicksMatchesCSharpDateTimeTicks)
{
  // 2026-01-01T00:00:00Z = unix 1767225600 s; C# new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks = 639028224000000000
  const auto timePoint = std::chrono::system_clock::time_point(std::chrono::seconds(1'767'225'600));
  EXPECT_EQ(FM::ToDateTimeTicks(timePoint), 639'028'224'000'000'000);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// Symbol and geometry
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(Geometry, MarkerSize)
{
  EXPECT_EQ(FM::MarkerSizePx(FM::Options{}, FM::MarkerKind::Sync), 198);
  EXPECT_EQ(FM::MarkerSizePx(FM::Options{}), 294);
  EXPECT_EQ(FM::MarkerSizePx(FM::Options{3, 4}), 147);
  EXPECT_EQ(FM::MarkerSizePx(FM::Options{12, 4}), 588);
  EXPECT_EQ(FM::MarkerSizePx(FM::Options{1, 0}), 41);
}

TEST(Geometry, InvalidOptionsGenerateNothing)
{
  std::vector<FM::Quad> quads(FM::MaxQuadCount());
  EXPECT_EQ(GenerateQuads({}, FM::Options{0, 4}, {}, quads), 0u);
  EXPECT_EQ(GenerateQuads({}, FM::Options{6, -1}, {}, quads), 0u);
  EXPECT_EQ(GenerateQuads({}, FM::Options{6, FM::MaxQuietZoneModules + 1}, {}, quads), 0u);
}

TEST(Geometry, TooSmallDestinationGeneratesNothing)
{
  std::vector<FM::Quad> quads(10);
  EXPECT_EQ(GenerateQuads({}, FM::Options{}, {}, quads), 0u);
}

TEST(Symbol, SyncMarkersAreVersion2)
{
  FM::ModuleMatrix matrix;
  ASSERT_TRUE(FM::GenerateModules({1u, 2, 3u, FM::MarkerKind::Sync, 4, 5u}, matrix));
  EXPECT_EQ(matrix.Size(), FM::SyncQrModuleCount);
  EXPECT_EQ(matrix.Size(), 25);

  const FM::Options options{3, 4};
  std::vector<FM::Quad> quads(FM::MaxQuadCount());
  const std::size_t count = GenerateQuads({7u, 0, 0u, FM::MarkerKind::Sync}, options, {10, 20}, quads);
  ASSERT_GT(count, 0u);
  EXPECT_EQ(quads.front(), (FM::Quad{10, 20, 10 + 99, 20 + 99, false}));
}

TEST(Payload, SyncMarkerCarriesOnlyTheRunIdAndTheFrameIndex)
{
  std::array<uint8_t, FM::MaxEncodedPayloadByteCount> buffer{};
  const FM::Payload payload{0x0102030405060708u, 123, 4u, FM::MarkerKind::Sync, 5, 6u, 7, 8u};
  const std::size_t byteCount = FM::EncodePayload(payload, {}, buffer);
  ASSERT_EQ(byteCount, FM::SyncPayloadByteCount);
  const std::array<uint8_t, FM::SyncPayloadByteCount> expected{'M',   'F',   1u,    3u,    0x04u, 0x00u, 0x00u, 0x00u,
                                                               0x08u, 0x07u, 0x06u, 0x05u, 0x04u, 0x03u, 0x02u, 0x01u};
  EXPECT_TRUE(std::equal(expected.begin(), expected.end(), buffer.begin()));

  FM::Payload decoded{};
  ASSERT_TRUE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(byteCount), decoded));
  EXPECT_EQ(decoded, (FM::Payload{payload.FrameIndex, 0, payload.RunId, FM::MarkerKind::Sync}));
  EXPECT_EQ(decoded.CpuStartTicks, 0);
  EXPECT_EQ(decoded.CpuBusyTicks, 0u);
  EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(byteCount + 1), decoded));
}

TEST(Symbol, EveryMarkerIsVersion6)
{
  FM::ModuleMatrix matrix;
  ASSERT_TRUE(FM::GenerateModules({1u, 2, 3u, FM::MarkerKind::Frame}, matrix));
  EXPECT_EQ(matrix.Size(), 41);
  ASSERT_TRUE(FM::GenerateModules({1u, 2, 3u, FM::MarkerKind::SequenceEnd}, matrix));
  EXPECT_EQ(matrix.Size(), 41);
  ASSERT_TRUE(FM::GenerateModules({1u, 2, 3u, FM::MarkerKind::SequenceStart}, matrix, {}));
  EXPECT_EQ(matrix.Size(), 41);

  FM::SequenceId id;
  ASSERT_TRUE(FM::SequenceId::TryFromText("0123456789abcdef", id));
  ASSERT_TRUE(FM::GenerateModules({1u, 2, 3u, FM::MarkerKind::SequenceStart, 4, 5u, 6, 7u}, matrix, {123, id}));
  EXPECT_EQ(matrix.Size(), FM::QrModuleCount);

  // Version 6-M holds 106 bytes: the start marker leaves room for future fields
  EXPECT_EQ(FM::MaxEncodedPayloadByteCount, 77u);
  EXPECT_LT(FM::MaxEncodedPayloadByteCount, FM::QrCapacityBytes);
}

TEST(Geometry, StartQuadsStayWithinTheMarkerSizeAndMaxQuadCount)
{
  FM::SequenceId id;
  id.Bytes.fill(0xFFu);
  const FM::Options options{};
  const FM::Point origin{32, 32};
  std::vector<FM::Quad> quads(FM::MaxQuadCount());
  const std::size_t count = GenerateStartQuads({5u, 6, 7u, FM::MarkerKind::Frame}, {99, id}, options, origin, quads);
  ASSERT_GT(count, 0u);
  ASSERT_LE(count, FM::MaxQuadCount());
  const FM::Quad& background = quads.front();
  EXPECT_EQ(background.Left, origin.X);
  EXPECT_EQ(background.Top, origin.Y);
  EXPECT_EQ(background.Right - background.Left, FM::MarkerSizePx(options));
  for (std::size_t i = 1; i < count; ++i)
  {
    EXPECT_LE(quads[i].Right, background.Right) << "quad " << i;
    EXPECT_LE(quads[i].Bottom, background.Bottom) << "quad " << i;
  }
}

TEST(Geometry, QuadsArePixelAlignedAndReproduceTheModuleMatrix)
{
  const FM::Payload payload{123'456'789u, 36'000'000'000, 0u};
  for (const int32_t moduleSize : {1, 2, 3, 6})
  {
    for (const int32_t quiet : {0, 1, 4})
    {
      SCOPED_TRACE("moduleSize " + std::to_string(moduleSize) + ", quiet " + std::to_string(quiet));
      const FM::Options options{moduleSize, quiet};
      const FM::Point origin{5, 7};
      const auto quads = Generate(payload, options, origin);
      const int32_t size = FM::MarkerSizePx(options);

      ASSERT_FALSE(quads.empty());
      ASSERT_LE(quads.size(), FM::MaxQuadCount());
      EXPECT_EQ(quads.front(), (FM::Quad{origin.X, origin.Y, origin.X + size, origin.Y + size, false}));

      for (std::size_t i = 1; i < quads.size(); ++i)
      {
        const FM::Quad& quad = quads[i];
        EXPECT_TRUE(quad.Dark) << "quad " << i;
        EXPECT_LT(quad.Left, quad.Right) << "quad " << i;
        EXPECT_EQ(quad.Bottom - quad.Top, moduleSize) << "quad " << i;
        EXPECT_EQ((quad.Left - origin.X) % moduleSize, 0) << "quad " << i;
        EXPECT_EQ((quad.Right - origin.X) % moduleSize, 0) << "quad " << i;
        EXPECT_EQ((quad.Top - origin.Y) % moduleSize, 0) << "quad " << i;
        // Dark quads never overlap: runs within a row are separated, rows are disjoint.
        for (std::size_t j = i + 1; j < quads.size(); ++j)
        {
          const FM::Quad& other = quads[j];
          const bool overlap = quad.Left < other.Right && other.Left < quad.Right && quad.Top < other.Bottom && other.Top < quad.Bottom;
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
  ASSERT_TRUE(FM::GenerateModules({1u, 0, 0u}, a));
  ASSERT_TRUE(FM::GenerateModules({2u, 0, 0u}, b));
  EXPECT_NE(a, b);
}

TEST(Symbol, FinderPatternsArePresent)
{
  FM::ModuleMatrix matrix;
  ASSERT_TRUE(FM::GenerateModules({99u, 99, 0u}, matrix));
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
  EXPECT_TRUE(matrix.IsDark(FM::QrModuleCount - 1, 0));
  EXPECT_TRUE(matrix.IsDark(0, FM::QrModuleCount - 1));
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// Vertex order
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(Vertices, TheBackgroundQuadIsDrawnInTheDocumentedOrder)
{
  const FM::ModuleMatrix matrix = Encode({1u, 2, 3u});
  const FM::Options options{2, 4};
  const int32_t size = FM::MarkerSizePx(options);
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
    FM::Point Origin;
  };

  std::vector<TriangleCase> TriangleCases()
  {
    std::vector<TriangleCase> cases;
    for (const int32_t moduleSize : {1, 2, 3, 6})
    {
      for (const int32_t quietZone : {0, 4})
      {
        const FM::Options options{moduleSize, quietZone};
        cases.push_back({{42u, 1'234'567, 3u, FM::MarkerKind::Frame, 987'654'321, 166'667u, 987'487'654}, {}, options, {5, 7}});
        cases.push_back({{43u, 1'400'234, 3u, FM::MarkerKind::SequenceEnd}, {}, options, {0, 0}});
        FM::SequenceId textId;
        FM::SequenceId::TryFromText("triangle-case", textId);
        FM::SequenceId fullId;
        fullId.Bytes.fill(0xFFu);
        for (const FM::SequenceId& id : {FM::SequenceId{}, textId, fullId})
        {
          cases.push_back({{41u, 1'067'890, 3u, FM::MarkerKind::SequenceStart}, id, options, {32, 64}});
        }
      }
    }
    return cases;
  }

  std::vector<FM::Quad> GenerateCase(const TriangleCase& testCase)
  {
    std::vector<FM::Quad> quads(FM::MaxQuadCount());
    const std::size_t count = testCase.Payload.Kind == FM::MarkerKind::SequenceStart
                                ? GenerateStartQuads(testCase.Payload, {1, testCase.StartId}, testCase.Options, testCase.Origin, quads)
                                : GenerateQuads(testCase.Payload, testCase.Options, testCase.Origin, quads);
    quads.resize(count);
    return quads;
  }

  std::vector<FM::Vertex> GenerateCaseTriangles(const TriangleCase& testCase)
  {
    std::vector<FM::Vertex> vertices(FM::MaxTriangleVertexCount());
    const std::size_t count = testCase.Payload.Kind == FM::MarkerKind::SequenceStart
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
    SCOPED_TRACE(testing::Message() << "kind " << static_cast<uint32_t>(testCase.Payload.Kind) << ", module " << testCase.Options.ModuleSizePx
                                    << ", quiet " << testCase.Options.QuietZoneModules << ", id " << testing::PrintToString(testCase.StartId));
    const std::vector<FM::Quad> quads = GenerateCase(testCase);
    ASSERT_FALSE(quads.empty());

    EXPECT_EQ(GenerateCaseTriangles(testCase), ToTriangles(quads));

    constexpr uint32_t BaseVertex = 1000u;
    std::vector<FM::Vertex> convertedVertices;
    std::vector<uint32_t> convertedIndices;
    ToIndexed(quads, BaseVertex, convertedVertices, convertedIndices);

    std::vector<FM::Vertex> vertices(FM::MaxIndexedVertexCount());
    std::vector<uint32_t> indices(FM::MaxIndexCount());
    const FM::IndexedCount count =
      testCase.Payload.Kind == FM::MarkerKind::SequenceStart
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
    SCOPED_TRACE(testing::Message() << "kind " << static_cast<uint32_t>(testCase.Payload.Kind) << ", module " << testCase.Options.ModuleSizePx
                                    << ", quiet " << testCase.Options.QuietZoneModules);
    const int32_t size = testCase.Origin.Y + FM::MarkerSizePx(testCase.Options) + 8;
    std::vector<uint8_t> fromQuads;
    ASSERT_TRUE(Rasterize(GenerateCase(testCase), size, size, fromQuads));

    std::vector<uint8_t> fromTriangles;
    ASSERT_TRUE(RasterizeTriangles(GenerateCaseTriangles(testCase), size, size, fromTriangles));
    EXPECT_EQ(fromTriangles, fromQuads) << "the triangle list must cover exactly the pixels of the quads";

    std::vector<FM::Vertex> vertices(FM::MaxIndexedVertexCount());
    std::vector<uint32_t> indices(FM::MaxIndexCount());
    const FM::IndexedCount count =
      testCase.Payload.Kind == FM::MarkerKind::SequenceStart
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
    const FM::Payload payload{frame * 7919u, static_cast<int64_t>(frame) * 166'667, 9u, FM::MarkerKind::Frame};
    EXPECT_GT(GenerateTriangles(payload, {}, {0, 0}, vertices), 0u) << "frame " << frame;
    EXPECT_GT(GenerateIndexed(payload, {}, {0, 0}, indexedVertices, indices).IndexCount, 0u) << "frame " << frame;
  }
}

TEST(Triangles, InvalidOptionsOrSmallBuffersGenerateNothing)
{
  const FM::Payload payload{1u, 2, 3u};
  std::vector<FM::Vertex> vertices(FM::MaxTriangleVertexCount());
  std::vector<uint32_t> indices(FM::MaxIndexCount());
  EXPECT_EQ(GenerateTriangles(payload, {0, 4}, {0, 0}, vertices), 0u);
  EXPECT_EQ(GenerateIndexed(payload, {6, -1}, {0, 0}, vertices, indices).VertexCount, 0u);

  std::vector<FM::Vertex> tooFew(12);
  EXPECT_EQ(GenerateTriangles(payload, {}, {0, 0}, tooFew), 0u);
  std::vector<uint32_t> tooFewIndices(12);
  const FM::IndexedCount failed = GenerateIndexed(payload, {}, {0, 0}, vertices, tooFewIndices);
  EXPECT_EQ(failed.VertexCount, 0u);
  EXPECT_EQ(failed.IndexCount, 0u);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// Sizing and placement (doc/marker-format.md "Sizing" and "Location")
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(Sizing, ModuleSizeRecommendationsMatchTheDocumentation)
{
  // 1:1
  EXPECT_EQ(FM::MinimumModuleSizePx(1080, 1080), 2);
  EXPECT_EQ(FM::RecommendModuleSizePx(1080, 1080), 3);
  EXPECT_EQ(FM::RecommendModuleSizePx(1080, 1080, true), 4);
  // 1440p -> 1080p
  EXPECT_EQ(FM::MinimumModuleSizePx(1440, 1080), 3);
  EXPECT_EQ(FM::RecommendModuleSizePx(1440, 1080), 4);
  // 1080p -> 540p and 2160p -> 1080p
  EXPECT_EQ(FM::MinimumModuleSizePx(1080, 540), 4);
  EXPECT_EQ(FM::RecommendModuleSizePx(1080, 540), 6);
  EXPECT_EQ(FM::RecommendModuleSizePx(2160, 1080), 6);
  EXPECT_EQ(FM::RecommendModuleSizePx(1080, 540, true), 8);
  // 1080p -> 360p
  EXPECT_EQ(FM::MinimumModuleSizePx(1080, 360), 6);
  EXPECT_EQ(FM::RecommendModuleSizePx(1080, 360), 9);
  // 2160p -> 540p
  EXPECT_EQ(FM::MinimumModuleSizePx(2160, 540), 8);
  EXPECT_EQ(FM::RecommendModuleSizePx(2160, 540), 12);
  // Upscaling never goes below the stored pixel count
  EXPECT_EQ(FM::RecommendModuleSizePx(540, 1080), 3);
  // Invalid heights fall back to 1:1
  EXPECT_EQ(FM::RecommendModuleSizePx(0, 1080), 3);
}

TEST(Sizing, RecommendedOrigins)
{
  const FM::Options options{};
  // The main marker top-left, the sync marker bottom-left
  EXPECT_EQ(FM::RecommendedOrigin(FM::MarkerKind::Frame, 1920, 1080, options), (FM::Point{32, 32}));
  EXPECT_EQ(FM::RecommendedOrigin(FM::MarkerKind::SequenceStart, 1920, 1080, options), (FM::Point{32, 32}));
  EXPECT_EQ(FM::RecommendedOrigin(FM::MarkerKind::Sync, 1920, 1080, options), (FM::Point{32, 1080 - 32 - 198}));
  // Aligned to a 3:1 downscale ratio
  EXPECT_EQ(FM::RecommendedOrigin(FM::MarkerKind::Frame, 1920, 1080, options, 3), (FM::Point{33, 33}));
  EXPECT_EQ(FM::RecommendedOrigin(FM::MarkerKind::Sync, 1920, 1080, options, 3), (FM::Point{33, 849}));
  static_assert(FM::RecommendedOrigin(FM::MarkerKind::Frame, 1920, 1080, FM::Options{}, 4) == FM::Point{32, 32});
}

TEST(Version, MatchesTheVersionFile)
{
  EXPECT_EQ(FM::VersionString, std::string_view(MB_FRAMEMARKER_EXPECTED_VERSION));
  EXPECT_EQ(std::to_string(FM::VersionMajor) + "." + std::to_string(FM::VersionMinor) + "." + std::to_string(FM::VersionPatch),
            std::string(FM::VersionString));
}

TEST(Version, TheLinkedLibraryHasTheVersionOfTheHeader)
{
  const FM::LibraryVersion version = FM::GetLibraryVersion();
  EXPECT_EQ(version.Text, FM::VersionString);
  EXPECT_EQ(version.Major, FM::VersionMajor);
  EXPECT_EQ(version.Minor, FM::VersionMinor);
  EXPECT_EQ(version.Patch, FM::VersionPatch);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// The encoded marker (ModuleMatrix) and the bitmap drawn from it
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(ModuleMatrix, BitsArePackedRowMajorMostSignificantBitFirst)
{
  for (const FM::MarkerKind kind : {FM::MarkerKind::Frame, FM::MarkerKind::Sync})
  {
    const FM::ModuleMatrix matrix = Encode({12345u, 678, 9u, kind});
    const int32_t size = matrix.Size();
    const auto bits = matrix.Bits();
    ASSERT_EQ(bits.size(), FM::PackedModuleByteCount(size));
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
  EXPECT_EQ(FM::PackedModuleByteCount(FM::QrModuleCount), 211u);
  EXPECT_EQ(FM::PackedModuleByteCount(FM::SyncQrModuleCount), 79u);
}

TEST(ModuleMatrix, TryFromBitsTakesQrSizesAndIgnoresThePadding)
{
  const FM::ModuleMatrix matrix = Encode({1u, 2, 3u, FM::MarkerKind::Sync});
  std::array<uint8_t, FM::MaxPackedModuleByteCount> bits{};
  std::copy(matrix.Bits().begin(), matrix.Bits().end(), bits.begin());
  bits[FM::PackedModuleByteCount(25) - 1u] |= 0x7Fu;    // 625 modules: the last byte uses 1 bit

  FM::ModuleMatrix copy;
  ASSERT_TRUE(FM::ModuleMatrix::TryFromBits(25, bits, copy));
  EXPECT_EQ(copy, matrix);
  EXPECT_FALSE(FM::ModuleMatrix::TryFromBits(24, bits, copy));
  EXPECT_FALSE(FM::ModuleMatrix::TryFromBits(45, bits, copy));
  EXPECT_FALSE(FM::ModuleMatrix::TryFromBits(25, std::span<const uint8_t>(bits).first(78), copy));
  EXPECT_EQ(copy, matrix) << "a refused call leaves the matrix unchanged";
}

TEST(ModuleMatrix, AnEmptyMatrixDrawsNothing)
{
  const FM::ModuleMatrix empty;
  std::array<FM::Quad, 4> quads{};
  std::array<uint8_t, 16> pixels{};
  EXPECT_EQ(empty.Size(), 0);
  EXPECT_EQ(FM::ModulesToQuads(empty, {}, {}, quads), 0u);
  EXPECT_FALSE(FM::ModulesToBitmap(empty, {1, 0}, {}, pixels, 4, 4, FM::PixelFormat::Gray8));
}

namespace
{
  struct BitmapCase
  {
    FM::Payload Payload;
    FM::Options Options;
    FM::Point Origin;
  };

  const std::array<BitmapCase, 5> g_bitmapCases{{
    {{1u, 2, 3u, FM::MarkerKind::Frame}, {3, 4}, {5, 7}},
    {{99u, -5, 1u, FM::MarkerKind::SequenceEnd}, {1, 0}, {0, 0}},
    {{7u, 0, 0u, FM::MarkerKind::Sync}, {2, 4}, {3, 1}},
    {{0xFFFFFFFFFFFFFFFFu, 1, 2u, FM::MarkerKind::Frame, 3, 4u, 5, 6u}, {2, 1}, {-9, -4}},
    {{5u, 6, 7u, FM::MarkerKind::Frame}, {4, 2}, {100, 60}},
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
    std::vector<FM::Quad> quads(FM::MaxQuadCount());
    quads.resize(FM::ModulesToQuads(matrix, testCase.Options, testCase.Origin, quads));

    // The expected pixels: every quad clipped to the canvas, over untouched pixels of 128
    std::vector<uint8_t> expected(static_cast<std::size_t>(Width) * static_cast<std::size_t>(Height), 128u);
    for (const FM::Quad& quad : quads)
    {
      for (int32_t y = std::max(quad.Top, 0); y < std::min(quad.Bottom, Height); ++y)
      {
        for (int32_t x = std::max(quad.Left, 0); x < std::min(quad.Right, Width); ++x)
        {
          expected[(static_cast<std::size_t>(y) * Width) + static_cast<std::size_t>(x)] = quad.Dark ? 0u : 255u;
        }
      }
    }

    for (const FM::PixelFormat format : {FM::PixelFormat::Gray8, FM::PixelFormat::Rgb24, FM::PixelFormat::Rgba32})
    {
      const auto bytesPerPixel = static_cast<std::size_t>(FM::BytesPerPixel(format));
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
  const FM::ModuleMatrix matrix = Encode({31u, 41, 59u, FM::MarkerKind::Frame});
  constexpr int32_t ModuleSize = 3;
  const FM::Options small{1, 4};
  const FM::Options large{ModuleSize, 4};
  const int32_t smallSize = FM::MarkerSizePx(small);
  const int32_t largeSize = FM::MarkerSizePx(large);
  std::vector<uint8_t> modules(static_cast<std::size_t>(smallSize) * static_cast<std::size_t>(smallSize));
  std::vector<uint8_t> pixels(static_cast<std::size_t>(largeSize) * static_cast<std::size_t>(largeSize));
  ASSERT_TRUE(FM::ModulesToBitmap(matrix, small, {0, 0}, modules, smallSize, smallSize, FM::PixelFormat::Gray8));
  ASSERT_TRUE(FM::ModulesToBitmap(matrix, large, {0, 0}, pixels, largeSize, largeSize, FM::PixelFormat::Gray8));
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
  const FM::ModuleMatrix matrix = Encode({1u, 2, 3u});
  std::vector<uint8_t> pixels(64u * 64u * 4u, 128u);
  EXPECT_FALSE(FM::ModulesToBitmap(matrix, {0, 4}, {}, pixels, 64, 64, FM::PixelFormat::Gray8)) << "invalid options";
  EXPECT_FALSE(FM::ModulesToBitmap(matrix, {1, 4}, {}, pixels, 64, 64, FM::PixelFormat::Rgb24, 64u * 3u - 1u)) << "short stride";
  EXPECT_FALSE(FM::ModulesToBitmap(matrix, {1, 4}, {}, std::span<uint8_t>(pixels).first((64u * 64u * 4u) - 1u), 64, 64, FM::PixelFormat::Rgba32))
    << "too small";
  EXPECT_FALSE(FM::ModulesToBitmap(matrix, {1, 4}, {}, pixels, -1, 64, FM::PixelFormat::Gray8)) << "negative width";
  EXPECT_TRUE(std::all_of(pixels.begin(), pixels.end(), [](const uint8_t value) { return value == 128u; }));
  EXPECT_TRUE(FM::ModulesToBitmap(matrix, {1, 4}, {500, 500}, pixels, 64, 64, FM::PixelFormat::Gray8)) << "outside the buffer: nothing to draw";
  EXPECT_TRUE(std::all_of(pixels.begin(), pixels.end(), [](const uint8_t value) { return value == 128u; }));
}

TEST(Bitmap, BytesPerPixel)
{
  EXPECT_EQ(FM::BytesPerPixel(FM::PixelFormat::Gray8), 1);
  EXPECT_EQ(FM::BytesPerPixel(FM::PixelFormat::Rgb24), 3);
  EXPECT_EQ(FM::BytesPerPixel(FM::PixelFormat::Rgba32), 4);
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
    {1u, 2, 3u, FM::MarkerKind::Frame},
    {99u, -5, 1u, FM::MarkerKind::SequenceEnd},
    {7u, 0, 0u, FM::MarkerKind::Sync},
    {0xFFFFFFFFFFFFFFFFu, 1, 2u, FM::MarkerKind::Frame, 3, 4u, 5, 6u},
    {5u, 6, 7u, FM::MarkerKind::SequenceStart},
  }};
  for (const FM::Payload& payload : payloads)
  {
    for (const FM::Options options : {FM::Options{1, 0}, FM::Options{3, 4}, FM::Options{6, 2}})
    {
      SCOPED_TRACE(testing::Message() << testing::PrintToString(payload) << ", module " << options.ModuleSizePx);
      const FM::Point origin{17, 23};
      const FM::ModuleMatrix matrix = Encode(payload);
      std::vector<FM::Vertex> grid(FM::MaxGridVertexCount());
      ASSERT_EQ(FM::GridVertices(payload.Kind, options, origin, grid), FM::GridVertexCount(payload.Kind));
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

TEST(Grid, InvalidOptionsOrSmallBuffersGiveNothing)
{
  std::vector<FM::Vertex> grid(FM::MaxGridVertexCount());
  EXPECT_EQ(FM::GridVertices(FM::MarkerKind::Frame, {0, 4}, {}, grid), 0u);
  EXPECT_EQ(FM::GridVertices(FM::MarkerKind::Frame, {}, {}, std::span<FM::Vertex>(grid).first(1767)), 0u);
  EXPECT_EQ(FM::GridVertices(FM::MarkerKind::Sync, {}, {}, std::span<FM::Vertex>(grid).first(680)), 680u);
  std::array<uint32_t, 12> indices{};
  EXPECT_EQ(FM::ModulesToGridIndices(Encode({1u, 2, 3u}), indices), 0u);
  EXPECT_EQ(FM::ModulesToGridIndices(FM::ModuleMatrix{}, indices), 0u);
}
