// SPDX-License-Identifier: BSD-3-Clause
//
// marker-render: payload -> quads -> software rasterizer -> binary PGM (P5).
// Used to produce the golden images the C# decoder tests consume (test-data/markers).
//
//   marker-render --frame <u64> --ticks <i64> [--run <u32>] [--kind frame|start|end] [--name <utf8>] [--utc-ticks <i64>]
//                 [--module <px>] [--quiet <modules>] [--canvas <W>x<H>] [--origin <X>,<Y>] [--background <0-255>] -o <file.pgm>
//   marker-render --golden <directory>
#include <mb/framemarker/FrameMarker.hpp>
#include <algorithm>
#include <array>
#include <charconv>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <string_view>
#include <tuple>
#include <utility>
#include <vector>

namespace FM = MB::FrameMarker;

namespace
{
  struct RenderRequest
  {
    FM::Payload Payload;
    std::string StartName;
    int64_t StartUtcTicks{0};
    FM::Options Options;
    int32_t CanvasWidth{0};
    int32_t CanvasHeight{0};
    FM::Point Origin{FM::RecommendedInsetPx, FM::RecommendedInsetPx};
    uint8_t Background{128};
  };

  struct Image
  {
    int32_t Width{0};
    int32_t Height{0};
    std::vector<uint8_t> Pixels;
  };

  Image Render(const RenderRequest& request)
  {
    const int32_t markerSize = FM::MaxMarkerSizePx(request.Options);
    Image image;
    image.Width = request.CanvasWidth > 0 ? request.CanvasWidth : request.Origin.X + markerSize + FM::RecommendedInsetPx;
    image.Height = request.CanvasHeight > 0 ? request.CanvasHeight : request.Origin.Y + markerSize + FM::RecommendedInsetPx;
    image.Pixels.assign(static_cast<std::size_t>(image.Width) * static_cast<std::size_t>(image.Height), request.Background);

    std::vector<FM::Quad> quads(FM::MaxQuadCount());
    const std::size_t quadCount =
      request.Payload.Kind == FM::MarkerKind::SequenceStart
        ? FM::GenerateStartQuads(request.Payload, {request.StartUtcTicks, request.StartName}, request.Options, request.Origin, quads)
        : FM::GenerateQuads(request.Payload, request.Options, request.Origin, quads);
    if (quadCount == 0)
    {
      throw std::runtime_error("GenerateQuads failed (invalid options?)");
    }

    // Rasterize exactly like a GPU with top-left fill rules and pixel-edge vertices: pixel (x,y) is covered when Left <= x < Right.
    for (std::size_t i = 0; i < quadCount; ++i)
    {
      const FM::Quad& quad = quads[i];
      const uint8_t luma = quad.Dark ? 0u : 255u;
      for (int32_t y = std::max(quad.Top, 0); y < std::min(quad.Bottom, image.Height); ++y)
      {
        for (int32_t x = std::max(quad.Left, 0); x < std::min(quad.Right, image.Width); ++x)
        {
          image.Pixels[(static_cast<std::size_t>(y) * static_cast<std::size_t>(image.Width)) + static_cast<std::size_t>(x)] = luma;
        }
      }
    }
    return image;
  }

  void WritePgm(const std::filesystem::path& path, const Image& image)
  {
    std::ofstream file(path, std::ios::binary);
    if (!file)
    {
      throw std::runtime_error("Failed to open '" + path.string() + "' for writing");
    }
    file << "P5\n" << image.Width << ' ' << image.Height << "\n255\n";
    file.write(reinterpret_cast<const char*>(image.Pixels.data()), static_cast<std::streamsize>(image.Pixels.size()));
    if (!file)
    {
      throw std::runtime_error("Failed to write '" + path.string() + "'");
    }
  }

  template <typename T>
  T ParseNumber(const std::string_view text, const std::string_view name)
  {
    T value{};
    const auto* const end = text.data() + text.size();
    const auto result = std::from_chars(text.data(), end, value);
    if (result.ec != std::errc() || result.ptr != end)
    {
      throw std::invalid_argument("Invalid value '" + std::string(text) + "' for " + std::string(name));
    }
    return value;
  }

  std::pair<int32_t, int32_t> ParsePair(const std::string_view text, const char separator, const std::string_view name)
  {
    const auto split = text.find(separator);
    if (split == std::string_view::npos)
    {
      throw std::invalid_argument("Expected <a>" + std::string(1, separator) + "<b> for " + std::string(name));
    }
    return {ParseNumber<int32_t>(text.substr(0, split), name), ParseNumber<int32_t>(text.substr(split + 1), name)};
  }

  std::string ToHex(const std::string_view text)
  {
    constexpr std::string_view Digits = "0123456789abcdef";
    std::string hex;
    for (const char ch : text)
    {
      const auto value = static_cast<uint8_t>(ch);
      hex += Digits[value >> 4u];
      hex += Digits[value & 0xFu];
    }
    return hex;
  }

  //! splitmix64: a tiny deterministic generator, so every platform writes the same digest
  uint64_t NextRandom(uint64_t& rState)
  {
    rState += 0x9E3779B97F4A7C15u;
    uint64_t value = rState;
    value = (value ^ (value >> 30u)) * 0xBF58476D1CE4E5B9u;
    value = (value ^ (value >> 27u)) * 0x94D049BB133111EBu;
    return value ^ (value >> 31u);
  }

  //! modules.csv: the QR module matrix of many pseudo random payloads (frame, end and start markers with 0-64 byte names, so every symbol
  //! version and mask occurs). Other implementations of the marker (the C# library) must reproduce every row exactly.
  //! Columns: kind, run id, frame index, animation ticks, start UTC ticks, start name (hex), symbol size, modules (hex): row major, one
  //! bit per module (1 = dark), most significant bit first, the last byte zero padded.
  void WriteModuleDigest(const std::filesystem::path& directory)
  {
    constexpr int32_t RowCount = 512;
    std::ofstream digest(directory / "modules.csv");
    if (!digest)
    {
      throw std::runtime_error("Failed to create modules.csv in '" + directory.string() + "'");
    }
    digest << "kind,runId,frameIndex,animationTicks,startUtcTicks,startNameHex,size,modulesHex\n";

    uint64_t state = 0x6D622D6672616D65u;
    for (int32_t row = 0; row < RowCount; ++row)
    {
      FM::Payload payload;
      payload.Kind = static_cast<FM::MarkerKind>(row % 3);
      payload.FrameIndex = NextRandom(state);
      payload.AnimationTicks = static_cast<int64_t>(NextRandom(state));
      payload.RunId = static_cast<uint32_t>(NextRandom(state));
      int64_t startUtcTicks = 0;
      std::string name;
      if (payload.Kind == FM::MarkerKind::SequenceStart)
      {
        startUtcTicks = static_cast<int64_t>(NextRandom(state) >> 1u);
        // Every length 0..64, with an occasional two byte UTF-8 character
        const auto length = static_cast<std::size_t>((row / 3) % static_cast<int32_t>(FM::MaxStartNameBytes + 1u));
        while (name.size() < length)
        {
          const uint64_t pick = NextRandom(state);
          if (pick % 7u == 0u && name.size() + 2u <= length)
          {
            name += "\xC3\xA6";
          }
          else
          {
            name += static_cast<char>(' ' + static_cast<char>(pick % 95u));
          }
        }
      }

      FM::ModuleMatrix matrix;
      if (!FM::GenerateModules(payload, matrix, {startUtcTicks, name}))
      {
        throw std::runtime_error("GenerateModules failed for digest row " + std::to_string(row));
      }
      std::string bits;
      uint8_t current = 0;
      int32_t bitCount = 0;
      for (int32_t y = 0; y < matrix.Size; ++y)
      {
        for (int32_t x = 0; x < matrix.Size; ++x)
        {
          current = static_cast<uint8_t>((static_cast<uint32_t>(current) << 1u) | (matrix.IsDark(x, y) ? 1u : 0u));
          if (++bitCount == 8)
          {
            bits += static_cast<char>(current);
            current = 0;
            bitCount = 0;
          }
        }
      }
      if (bitCount > 0)
      {
        bits += static_cast<char>(static_cast<uint8_t>(static_cast<uint32_t>(current) << static_cast<uint32_t>(8 - bitCount)));
      }
      digest << static_cast<uint32_t>(payload.Kind) << ',' << payload.RunId << ',' << payload.FrameIndex << ',' << payload.AnimationTicks << ','
             << startUtcTicks << ',' << ToHex(name) << ',' << matrix.Size << ',' << ToHex(bits) << '\n';
    }
  }

  void WriteGolden(const std::filesystem::path& directory)
  {
    WriteModuleDigest(directory);

    constexpr std::array<FM::Payload, 10> Payloads{{
      {0u, 0, 0u, FM::MarkerKind::Frame},
      {1u, 166'667, 1u, FM::MarkerKind::Frame},
      {123'456'789u, 36'000'000'000, 1u, FM::MarkerKind::Frame},
      {42u, -1, 2u, FM::MarkerKind::Frame},
      {7u, std::numeric_limits<int64_t>::min(), 3u, FM::MarkerKind::Frame},
      {std::numeric_limits<uint64_t>::max(), std::numeric_limits<int64_t>::max(), std::numeric_limits<uint32_t>::max(), FM::MarkerKind::Frame},
      {0x0102030405060708u, 0x1112131415161718, 0x21222324u, FM::MarkerKind::Frame},
      {600u, 100'000'000, 5u, FM::MarkerKind::SequenceStart},
      {601u, 100'166'667, 6u, FM::MarkerKind::SequenceStart},
      {900u, 150'000'000, 5u, FM::MarkerKind::SequenceEnd},
    }};
    constexpr std::array<int32_t, 4> ModuleSizes{2, 3, 4, 6};

    std::ofstream manifest(directory / "manifest.csv");
    if (!manifest)
    {
      throw std::runtime_error("Failed to create manifest in '" + directory.string() + "'");
    }
    manifest << "file,kind,runId,frameIndex,animationTicks,startUtcTicks,startNameHex,moduleSizePx,quietZoneModules,originX,originY,width,"
                "height\n";

    for (std::size_t payloadIndex = 0; payloadIndex < Payloads.size(); ++payloadIndex)
    {
      for (const int32_t moduleSize : ModuleSizes)
      {
        RenderRequest request;
        request.Payload = Payloads[payloadIndex];
        if (request.Payload.Kind == FM::MarkerKind::SequenceStart && request.Payload.RunId == 6u)
        {
          // 2026-09-23T12:00:00Z and a 61 byte UTF-8 name (limit is 64)
          request.StartUtcTicks = 639'257'616'000'000'000;
          request.StartName = "golden-run \xC3\xA6\xC3\xB8\xC3\xA5 0123456789012345678901234567890123456789012";
        }
        request.Options.ModuleSizePx = moduleSize;
        // Origin and canvas are multiples of 12 (lcm of 2,3,4,6) so every integer downscale test keeps module edges pixel aligned.
        request.Origin = {36, 36};
        const int32_t canvas = ((request.Origin.X + FM::MaxMarkerSizePx(request.Options) + 36 + 11) / 12) * 12;
        request.CanvasWidth = canvas;
        request.CanvasHeight = canvas;

        const Image image = Render(request);
        std::string fileName = "marker_p";
        fileName += std::to_string(payloadIndex);
        fileName += "_m";
        fileName += std::to_string(moduleSize);
        fileName += ".pgm";
        WritePgm(directory / fileName, image);
        manifest << fileName << ',' << static_cast<uint32_t>(request.Payload.Kind) << ',' << request.Payload.RunId << ','
                 << request.Payload.FrameIndex << ',' << request.Payload.AnimationTicks << ',' << request.StartUtcTicks << ','
                 << ToHex(request.StartName) << ',' << request.Options.ModuleSizePx << ',' << request.Options.QuietZoneModules << ','
                 << request.Origin.X << ',' << request.Origin.Y << ',' << image.Width << ',' << image.Height << '\n';
      }
    }
  }

  void PrintUsage()
  {
    std::cout << "Usage:\n"
                 "  marker-render --frame <u64> --ticks <i64> [--run <u32>] [--kind frame|start|end] [--name <utf8>]\n"
                 "                [--utc-ticks <i64>] [--module <px>] [--quiet <modules>] [--canvas <W>x<H>] [--origin <X>,<Y>]\n"
                 "                [--background <0-255>] -o <file.pgm>\n"
                 "  marker-render --golden <directory>\n";
  }
}

int main(int argc, char* argv[])
{
  try
  {
    RenderRequest request;
    std::string outputPath;
    std::string goldenDirectory;

    for (int i = 1; i < argc; ++i)
    {
      const std::string_view arg(argv[i]);
      const auto next = [&]() -> std::string_view
      {
        if (i + 1 >= argc)
        {
          throw std::invalid_argument("Missing value for " + std::string(arg));
        }
        return argv[++i];
      };

      if (arg == "--help" || arg == "-h")
      {
        PrintUsage();
        return 0;
      }
      if (arg == "--frame")
      {
        request.Payload.FrameIndex = ParseNumber<uint64_t>(next(), arg);
      }
      else if (arg == "--ticks")
      {
        request.Payload.AnimationTicks = ParseNumber<int64_t>(next(), arg);
      }
      else if (arg == "--name")
      {
        request.StartName = next();
      }
      else if (arg == "--utc-ticks")
      {
        request.StartUtcTicks = ParseNumber<int64_t>(next(), arg);
      }
      else if (arg == "--run")
      {
        request.Payload.RunId = ParseNumber<uint32_t>(next(), arg);
      }
      else if (arg == "--kind")
      {
        const std::string_view kind = next();
        if (kind == "frame")
        {
          request.Payload.Kind = FM::MarkerKind::Frame;
        }
        else if (kind == "start")
        {
          request.Payload.Kind = FM::MarkerKind::SequenceStart;
        }
        else if (kind == "end")
        {
          request.Payload.Kind = FM::MarkerKind::SequenceEnd;
        }
        else
        {
          throw std::invalid_argument("--kind must be frame, start or end");
        }
      }
      else if (arg == "--module")
      {
        request.Options.ModuleSizePx = ParseNumber<int32_t>(next(), arg);
      }
      else if (arg == "--quiet")
      {
        request.Options.QuietZoneModules = ParseNumber<int32_t>(next(), arg);
      }
      else if (arg == "--canvas")
      {
        std::tie(request.CanvasWidth, request.CanvasHeight) = ParsePair(next(), 'x', arg);
      }
      else if (arg == "--origin")
      {
        std::tie(request.Origin.X, request.Origin.Y) = ParsePair(next(), ',', arg);
      }
      else if (arg == "--background")
      {
        request.Background = static_cast<uint8_t>(ParseNumber<uint32_t>(next(), arg));
      }
      else if (arg == "-o" || arg == "--output")
      {
        outputPath = next();
      }
      else if (arg == "--golden")
      {
        goldenDirectory = next();
      }
      else
      {
        throw std::invalid_argument("Unknown argument '" + std::string(arg) + "'");
      }
    }

    if (!goldenDirectory.empty())
    {
      WriteGolden(goldenDirectory);
      return 0;
    }
    if (outputPath.empty())
    {
      PrintUsage();
      return 1;
    }
    if (!FM::IsValid(request.Options))
    {
      throw std::invalid_argument("Invalid module size or quiet zone");
    }
    WritePgm(outputPath, Render(request));
    return 0;
  }
  catch (const std::exception& ex)
  {
    std::cerr << "marker-render: " << ex.what() << '\n';
    return 1;
  }
}
