// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framemarker/FrameMarker.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <limits>
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
        << static_cast<uint32_t>(value.Kind) << "}";
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
  std::vector<FM::Quad> Generate(const FM::Payload& payload, const FM::Options& options, const FM::Point origin)
  {
    std::vector<FM::Quad> quads(FM::MaxQuadCount());
    const std::size_t count = FM::GenerateQuads(payload, options, origin, quads);
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
  const FM::Payload payload{0x0102030405060708u, 0x1112131415161718, 0x21222324u, FM::MarkerKind::SequenceEnd};
  const auto bytes = FM::EncodePayload(payload);
  const std::array<uint8_t, FM::PayloadByteCount> expected{'M',   'F',   1u,    2u,    0x08u, 0x07u, 0x06u, 0x05u, 0x04u, 0x03u, 0x02u, 0x01u,
                                                           0x18u, 0x17u, 0x16u, 0x15u, 0x14u, 0x13u, 0x12u, 0x11u, 0x24u, 0x23u, 0x22u, 0x21u};
  EXPECT_EQ(bytes, expected);
}

TEST(Payload, NegativeTicksAreStoredAsTwosComplement)
{
  const auto bytes = FM::EncodePayload({0u, -1, 0u});
  for (std::size_t i = 12; i < 20; ++i)
  {
    EXPECT_EQ(bytes[i], 0xFFu) << "byte " << i;
  }
}

TEST(Payload, RoundTrips)
{
  const std::array<FM::Payload, 6> payloads{{
    {0u, 0, 0u, FM::MarkerKind::Frame},
    {1u, 166'667, 7u, FM::MarkerKind::Frame},
    {std::numeric_limits<uint64_t>::max(), std::numeric_limits<int64_t>::max(), std::numeric_limits<uint32_t>::max(), FM::MarkerKind::Frame},
    {7u, std::numeric_limits<int64_t>::min(), 1u, FM::MarkerKind::SequenceStart},
    {42u, -1, 3u, FM::MarkerKind::SequenceEnd},
    {0u, 0, 0u, FM::MarkerKind::SequenceStart},
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

TEST(Payload, StartMarkerNeedsItsMetadataBlock)
{
  const auto header = FM::EncodePayload({1u, 2, 3u, FM::MarkerKind::SequenceStart});
  FM::Payload decoded{};
  EXPECT_FALSE(FM::TryDecodePayload(header, decoded));
}

TEST(Payload, TryDecodeRejectsBadInput)
{
  auto bytes = FM::EncodePayload({1u, 2, 0u});
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

TEST(Payload, StartMetadataRoundTrips)
{
  std::array<uint8_t, FM::MaxEncodedPayloadByteCount> buffer{};
  const std::string_view name = "Benchmark \xC3\xA6\xC3\xB8\xC3\xA5 run";
  const FM::Payload payload{10u, 20, 30u, FM::MarkerKind::SequenceStart};
  const std::size_t byteCount = FM::EncodePayload(payload, {638'000'000'000'000'000, name}, buffer);
  ASSERT_EQ(byteCount, FM::StartPayloadFixedByteCount + name.size());

  FM::Payload decoded{};
  FM::StartMetadata metadata{};
  ASSERT_TRUE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(byteCount), decoded, &metadata));
  EXPECT_EQ(decoded, payload);
  EXPECT_EQ(metadata.UtcTicks, 638'000'000'000'000'000);
  EXPECT_EQ(metadata.Name, name);

  // A start payload with a truncated name is rejected
  EXPECT_FALSE(FM::TryDecodePayload(std::span<const uint8_t>(buffer).first(byteCount - 1), decoded, &metadata));

  // Frame payloads ignore the metadata and stay 24 bytes
  EXPECT_EQ(FM::EncodePayload({1u, 2, 3u, FM::MarkerKind::Frame}, {5, name}, buffer), FM::PayloadByteCount);
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
  EXPECT_EQ(FM::MarkerSizePx(FM::Options{}), 198);
  EXPECT_EQ(FM::MarkerSizePx(FM::Options{3, 4}), 99);
  EXPECT_EQ(FM::MarkerSizePx(FM::Options{12, 4}), 396);
  EXPECT_EQ(FM::MarkerSizePx(FM::Options{1, 0}), 25);
}

TEST(Geometry, InvalidOptionsGenerateNothing)
{
  std::vector<FM::Quad> quads(FM::MaxQuadCount());
  EXPECT_EQ(FM::GenerateQuads({}, FM::Options{0, 4}, {}, quads), 0u);
  EXPECT_EQ(FM::GenerateQuads({}, FM::Options{6, -1}, {}, quads), 0u);
  EXPECT_EQ(FM::GenerateQuads({}, FM::Options{6, FM::MaxQuietZoneModules + 1}, {}, quads), 0u);
}

TEST(Geometry, TooSmallDestinationGeneratesNothing)
{
  std::vector<FM::Quad> quads(10);
  EXPECT_EQ(FM::GenerateQuads({}, FM::Options{}, {}, quads), 0u);
}

TEST(Symbol, FrameAndEndMarkersAreVersion2StartMarkersGrowWithTheName)
{
  FM::ModuleMatrix matrix;
  ASSERT_TRUE(FM::GenerateModules({1u, 2, 3u, FM::MarkerKind::Frame}, matrix));
  EXPECT_EQ(matrix.Size, 25);
  ASSERT_TRUE(FM::GenerateModules({1u, 2, 3u, FM::MarkerKind::SequenceEnd}, matrix));
  EXPECT_EQ(matrix.Size, 25);

  // A 33 byte payload does not fit version 2-M (26 bytes) -> version 3 (29 modules)
  ASSERT_TRUE(FM::GenerateModules({1u, 2, 3u, FM::MarkerKind::SequenceStart}, matrix, {}));
  EXPECT_EQ(matrix.Size, 29);

  const std::string maxName(FM::MaxStartNameBytes, 'x');
  ASSERT_TRUE(FM::GenerateModules({1u, 2, 3u, FM::MarkerKind::SequenceStart}, matrix, {123, maxName}));
  EXPECT_LE(matrix.Size, FM::MaxQrModuleCount);

  const std::string tooLong(FM::MaxStartNameBytes + 1, 'x');
  EXPECT_FALSE(FM::GenerateModules({1u, 2, 3u, FM::MarkerKind::SequenceStart}, matrix, {123, tooLong}));
}

TEST(Geometry, StartQuadsStayWithinMaxMarkerSizeAndMaxQuadCount)
{
  const std::string maxName(FM::MaxStartNameBytes, 'y');
  const FM::Options options{};
  const FM::Point origin{32, 32};
  std::vector<FM::Quad> quads(FM::MaxQuadCount());
  const std::size_t count = FM::GenerateStartQuads({5u, 6, 7u, FM::MarkerKind::Frame}, {99, maxName}, options, origin, quads);
  ASSERT_GT(count, 0u);
  ASSERT_LE(count, FM::MaxQuadCount());
  const FM::Quad& background = quads.front();
  EXPECT_EQ(background.Left, origin.X);
  EXPECT_EQ(background.Top, origin.Y);
  EXPECT_LE(background.Right - background.Left, FM::MaxMarkerSizePx(options));
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
      ASSERT_LE(quads.size(), FM::MaxFrameQuadCount());
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
            const bool inSymbol = moduleX >= 0 && moduleY >= 0 && moduleX < matrix.Size && moduleY < matrix.Size;
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
  EXPECT_NE(a.Modules, b.Modules);
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
  EXPECT_TRUE(matrix.IsDark(FM::FrameQrModuleCount - 1, 0));
  EXPECT_TRUE(matrix.IsDark(0, FM::FrameQrModuleCount - 1));
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// Vertex conversion
// ---------------------------------------------------------------------------------------------------------------------------------------------

TEST(Vertices, QuadsToTriangles)
{
  const std::array<FM::Quad, 2> quads{{{0, 0, 10, 10, false}, {2, 3, 4, 5, true}}};
  std::array<FM::Vertex, 12> vertices{};
  ASSERT_EQ(FM::QuadsToTriangles(quads, vertices), 12u);
  EXPECT_EQ(vertices[0], (FM::Vertex{0, 0, 255u}));
  EXPECT_EQ(vertices[1], (FM::Vertex{10, 0, 255u}));
  EXPECT_EQ(vertices[2], (FM::Vertex{0, 10, 255u}));
  EXPECT_EQ(vertices[3], (FM::Vertex{0, 10, 255u}));
  EXPECT_EQ(vertices[4], (FM::Vertex{10, 0, 255u}));
  EXPECT_EQ(vertices[5], (FM::Vertex{10, 10, 255u}));
  EXPECT_EQ(vertices[6], (FM::Vertex{2, 3, 0u}));
  EXPECT_EQ(vertices[11], (FM::Vertex{4, 5, 0u}));

  std::array<FM::Vertex, 11> tooSmall{};
  EXPECT_EQ(FM::QuadsToTriangles(quads, tooSmall), 0u);
}

TEST(Vertices, QuadsToIndexed)
{
  const std::array<FM::Quad, 2> quads{{{0, 0, 10, 10, false}, {2, 3, 4, 5, true}}};
  std::array<FM::Vertex, 8> vertices{};
  std::array<uint32_t, 12> indices{};
  const FM::IndexedCount count = FM::QuadsToIndexed(quads, vertices, indices, 100u);
  EXPECT_EQ(count.VertexCount, 8u);
  EXPECT_EQ(count.IndexCount, 12u);
  EXPECT_EQ(vertices[4], (FM::Vertex{2, 3, 0u}));
  EXPECT_EQ(vertices[5], (FM::Vertex{4, 3, 0u}));
  EXPECT_EQ(vertices[6], (FM::Vertex{4, 5, 0u}));
  EXPECT_EQ(vertices[7], (FM::Vertex{2, 5, 0u}));
  const std::array<uint32_t, 12> expectedIndices{100u, 101u, 103u, 103u, 101u, 102u, 104u, 105u, 107u, 107u, 105u, 106u};
  EXPECT_EQ(indices, expectedIndices);

  std::array<uint32_t, 11> tooFewIndices{};
  const FM::IndexedCount failed = FM::QuadsToIndexed(quads, vertices, tooFewIndices);
  EXPECT_EQ(failed.VertexCount, 0u);
  EXPECT_EQ(failed.IndexCount, 0u);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------
// Direct triangle output (GenerateTriangles / GenerateIndexed)
// ---------------------------------------------------------------------------------------------------------------------------------------------

namespace
{
  //! One marker to generate: a frame, end or start marker (the start marker with metadata).
  struct TriangleCase
  {
    FM::Payload Payload;
    std::string StartName;
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
        cases.push_back({{42u, 1'234'567, 3u, FM::MarkerKind::Frame}, {}, options, {5, 7}});
        cases.push_back({{43u, 1'400'234, 3u, FM::MarkerKind::SequenceEnd}, {}, options, {0, 0}});
        for (const std::size_t nameLength : {std::size_t{0}, std::size_t{17}, FM::MaxStartNameBytes})
        {
          cases.push_back({{41u, 1'067'890, 3u, FM::MarkerKind::SequenceStart}, std::string(nameLength, 'n'), options, {32, 64}});
        }
      }
    }
    return cases;
  }

  std::vector<FM::Quad> GenerateCase(const TriangleCase& testCase)
  {
    std::vector<FM::Quad> quads(FM::MaxQuadCount());
    const std::size_t count = testCase.Payload.Kind == FM::MarkerKind::SequenceStart
                                ? FM::GenerateStartQuads(testCase.Payload, {1, testCase.StartName}, testCase.Options, testCase.Origin, quads)
                                : FM::GenerateQuads(testCase.Payload, testCase.Options, testCase.Origin, quads);
    quads.resize(count);
    return quads;
  }

  std::vector<FM::Vertex> GenerateCaseTriangles(const TriangleCase& testCase)
  {
    std::vector<FM::Vertex> vertices(FM::MaxTriangleVertexCount());
    const std::size_t count = testCase.Payload.Kind == FM::MarkerKind::SequenceStart
                                ? FM::GenerateStartTriangles(testCase.Payload, {1, testCase.StartName}, testCase.Options, testCase.Origin, vertices)
                                : FM::GenerateTriangles(testCase.Payload, testCase.Options, testCase.Origin, vertices);
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
                                    << ", quiet " << testCase.Options.QuietZoneModules << ", name " << testCase.StartName.size());
    const std::vector<FM::Quad> quads = GenerateCase(testCase);
    ASSERT_FALSE(quads.empty());

    std::vector<FM::Vertex> converted(quads.size() * 6u);
    ASSERT_EQ(FM::QuadsToTriangles(quads, converted), converted.size());
    EXPECT_EQ(GenerateCaseTriangles(testCase), converted);

    constexpr uint32_t BaseVertex = 1000u;
    std::vector<FM::Vertex> convertedVertices(quads.size() * 4u);
    std::vector<uint32_t> convertedIndices(quads.size() * 6u);
    const FM::IndexedCount convertedCount = FM::QuadsToIndexed(quads, convertedVertices, convertedIndices, BaseVertex);
    ASSERT_EQ(convertedCount.IndexCount, convertedIndices.size());

    std::vector<FM::Vertex> vertices(FM::MaxIndexedVertexCount());
    std::vector<uint32_t> indices(FM::MaxIndexCount());
    const FM::IndexedCount count =
      testCase.Payload.Kind == FM::MarkerKind::SequenceStart
        ? FM::GenerateStartIndexed(testCase.Payload, {1, testCase.StartName}, testCase.Options, testCase.Origin, vertices, indices, BaseVertex)
        : FM::GenerateIndexed(testCase.Payload, testCase.Options, testCase.Origin, vertices, indices, BaseVertex);
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
    const int32_t size = testCase.Origin.Y + FM::MaxMarkerSizePx(testCase.Options) + 8;
    std::vector<uint8_t> fromQuads;
    ASSERT_TRUE(Rasterize(GenerateCase(testCase), size, size, fromQuads));

    std::vector<uint8_t> fromTriangles;
    ASSERT_TRUE(RasterizeTriangles(GenerateCaseTriangles(testCase), size, size, fromTriangles));
    EXPECT_EQ(fromTriangles, fromQuads) << "the triangle list must cover exactly the pixels of the quads";

    std::vector<FM::Vertex> vertices(FM::MaxIndexedVertexCount());
    std::vector<uint32_t> indices(FM::MaxIndexCount());
    const FM::IndexedCount count =
      testCase.Payload.Kind == FM::MarkerKind::SequenceStart
        ? FM::GenerateStartIndexed(testCase.Payload, {1, testCase.StartName}, testCase.Options, testCase.Origin, vertices, indices)
        : FM::GenerateIndexed(testCase.Payload, testCase.Options, testCase.Origin, vertices, indices);
    vertices.resize(count.VertexCount);
    indices.resize(count.IndexCount);
    std::vector<uint8_t> fromIndexed;
    ASSERT_TRUE(RasterizeTriangles(ExpandIndexed(vertices, indices, 0u), size, size, fromIndexed));
    EXPECT_EQ(fromIndexed, fromQuads) << "the indexed triangle list must cover exactly the pixels of the quads";
  }
}

TEST(Triangles, FrameMarkersFitTheFrameBufferSizes)
{
  std::vector<FM::Vertex> vertices(FM::MaxFrameTriangleVertexCount());
  std::vector<FM::Vertex> indexedVertices(FM::MaxFrameIndexedVertexCount());
  std::vector<uint32_t> indices(FM::MaxFrameIndexCount());
  for (uint64_t frame = 0; frame < 500u; ++frame)
  {
    const FM::Payload payload{frame * 7919u, static_cast<int64_t>(frame) * 166'667, 9u, FM::MarkerKind::Frame};
    EXPECT_GT(FM::GenerateTriangles(payload, {}, {0, 0}, vertices), 0u) << "frame " << frame;
    EXPECT_GT(FM::GenerateIndexed(payload, {}, {0, 0}, indexedVertices, indices).IndexCount, 0u) << "frame " << frame;
  }
}

TEST(Triangles, InvalidOptionsOrSmallBuffersGenerateNothing)
{
  const FM::Payload payload{1u, 2, 3u};
  std::vector<FM::Vertex> vertices(FM::MaxTriangleVertexCount());
  std::vector<uint32_t> indices(FM::MaxIndexCount());
  EXPECT_EQ(FM::GenerateTriangles(payload, {0, 4}, {0, 0}, vertices), 0u);
  EXPECT_EQ(FM::GenerateIndexed(payload, {6, -1}, {0, 0}, vertices, indices).VertexCount, 0u);

  std::vector<FM::Vertex> tooFew(12);
  EXPECT_EQ(FM::GenerateTriangles(payload, {}, {0, 0}, tooFew), 0u);
  std::vector<uint32_t> tooFewIndices(12);
  const FM::IndexedCount failed = FM::GenerateIndexed(payload, {}, {0, 0}, vertices, tooFewIndices);
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
  EXPECT_EQ(FM::RecommendedOrigin(FM::MarkerSlot::TopLeft, 1920, 1080, options), (FM::Point{32, 32}));
  EXPECT_EQ(FM::RecommendedOrigin(FM::MarkerSlot::MiddleLeft, 1920, 1080, options), (FM::Point{32, 441}));
  EXPECT_EQ(FM::RecommendedOrigin(FM::MarkerSlot::BottomLeft, 1920, 1080, options), (FM::Point{32, 1080 - 32 - 198}));
  // Aligned to a 3:1 downscale ratio
  EXPECT_EQ(FM::RecommendedOrigin(FM::MarkerSlot::TopLeft, 1920, 1080, options, 3), (FM::Point{33, 33}));
  EXPECT_EQ(FM::RecommendedOrigin(FM::MarkerSlot::MiddleLeft, 1920, 1080, options, 3), (FM::Point{33, 441}));
  EXPECT_EQ(FM::RecommendedOrigin(FM::MarkerSlot::BottomLeft, 1920, 1080, options, 3), (FM::Point{33, 849}));
  static_assert(FM::RecommendedOrigin(FM::MarkerSlot::TopLeft, 1920, 1080, FM::Options{}, 4) == FM::Point{32, 32});
}

TEST(Version, MatchesTheVersionFile)
{
  EXPECT_EQ(FM::VersionString, std::string_view(MB_FRAMEMARKER_EXPECTED_VERSION));
  EXPECT_EQ(std::to_string(FM::VersionMajor) + "." + std::to_string(FM::VersionMinor) + "." + std::to_string(FM::VersionPatch),
            std::string(FM::VersionString));
}
