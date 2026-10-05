// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// marker-render: payload -> quads -> software rasterizer -> binary PGM (P5).
// Used to produce the golden images the C# decoder tests consume (test-data/markers).
//
//   marker-render --frame <u64> --ticks <i64> [--run <u32>] [--kind frame|start|end|sync] [--intended-ticks <i64>]
//                 [--target-ticks <u32>] [--cpu-start-ticks <i64>] [--cpu-busy-ticks <u32>] [--preferred-ticks <u32>]
//                 [--flags <0-255>] [--utc-ticks <i64>]
//                 [--sequence-id <text> | --sequence-id-hex <hex>] [--module <px>] [--quiet <modules>] [--canvas <W>x<H>] [--origin <X>,<Y>]
//                 [--background <0-255>] -o <file.pgm>
//   marker-render --golden <directory>
#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/Options.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/geometry/PixelFormat.hpp>
#include <mb/framepacing/marker/payload/MarkerFlags.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/marker/payload/SequenceId.hpp>
#include <mb/framepacing/marker/payload/StartMetadata.hpp>
#include <algorithm>
#include <array>
#include <charconv>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <limits>
#include <span>
#include <stdexcept>
#include <string>
#include <string_view>
#include <tuple>
#include <utility>
#include <vector>

namespace FP = MB::FramePacing;
namespace FM = MB::FramePacing::Marker;

namespace
{
  struct RenderRequest
  {
    FM::Payload Payload;
    FM::StartMetadata Start;
    FM::Options Options;
    int32_t CanvasWidth{0};
    int32_t CanvasHeight{0};
    FP::Point Origin{FM::Options::RecommendedInsetPx, FM::Options::RecommendedInsetPx};
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
    const int32_t markerSize = request.Options.MarkerSizePx(request.Payload.Kind());
    Image image;
    image.Width = request.CanvasWidth > 0 ? request.CanvasWidth : request.Origin.X + markerSize + FM::Options::RecommendedInsetPx;
    image.Height = request.CanvasHeight > 0 ? request.CanvasHeight : request.Origin.Y + markerSize + FM::Options::RecommendedInsetPx;
    image.Pixels.assign(static_cast<std::size_t>(image.Width) * static_cast<std::size_t>(image.Height), request.Background);

    // The library draws it: pixel (x,y) is covered when Left <= x < Right, exactly as a GPU rasterizes pixel-edge geometry
    FM::ModuleMatrix matrix;
    if (!FM::GenerateModules(request.Payload, matrix, request.Start) ||
        !FM::ModulesToBitmap(matrix, request.Options, request.Origin, image.Pixels, image.Width, image.Height, FM::PixelFormat::R8))
    {
      throw std::runtime_error("Drawing the marker failed");
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
    const char* const first = text.data();
    const char* const end = first + text.size();
    const auto result = std::from_chars(first, end, value);
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

  std::string ToHex(const std::span<const uint8_t> bytes)
  {
    constexpr std::string_view Digits = "0123456789abcdef";
    std::string hex;
    for (const uint8_t value : bytes)
    {
      hex += Digits[value >> 4u];
      hex += Digits[value & 0xFu];
    }
    return hex;
  }

  //! The sequence id column of the CSV files: 32 lowercase hex digits for a start marker, empty for every other kind.
  std::string SequenceIdHex(const FM::MarkerKind kind, const FM::SequenceId& id)
  {
    return kind == FM::MarkerKind::SequenceStart ? ToHex(id.Bytes) : std::string();
  }

  FM::SequenceId ParseSequenceIdHex(const std::string_view text, const std::string_view name)
  {
    const auto digit = [&](const char ch) -> uint32_t
    {
      if (ch >= '0' && ch <= '9')
      {
        return static_cast<uint32_t>(ch - '0');
      }
      if (ch >= 'a' && ch <= 'f')
      {
        return static_cast<uint32_t>(ch - 'a') + 10u;
      }
      if (ch >= 'A' && ch <= 'F')
      {
        return static_cast<uint32_t>(ch - 'A') + 10u;
      }
      throw std::invalid_argument("Invalid hex digit in '" + std::string(text) + "' for " + std::string(name));
    };
    if (text.size() != FM::SequenceId::ByteCount * 2u)
    {
      throw std::invalid_argument(std::string(name) + " needs exactly 32 hex digits");
    }
    FM::SequenceId id;
    for (std::size_t i = 0; i < FM::SequenceId::ByteCount; ++i)
    {
      id.Bytes[i] = static_cast<uint8_t>((digit(text[2u * i]) << 4u) | digit(text[(2u * i) + 1u]));
    }
    return id;
  }

  //! A text tag sequence id for the golden cases (1 to 16 printable ASCII characters).
  constexpr FM::SequenceId TextSequenceId(const std::string_view text)
  {
    FM::SequenceId id;
    if (!FM::SequenceId::TryFromText(text, id))
    {
      throw std::invalid_argument("A text sequence id is 1 to 16 printable ASCII characters");
    }
    return id;
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

  //! modules.csv: the QR module matrix of many pseudo random payloads (frame, start, end and sync markers; the start markers carry random,
  //! text, empty and all 0xFF sequence ids; both symbol versions use every mask). Other implementations of the marker (C#, Python) must
  //! reproduce every row exactly.
  //! Columns, the payload's in the order of the wire format: kind, run id, frame index, flags (the byte, reserved bits included),
  //! animation ticks, preferred frame ticks, target frame ticks, intended display ticks, CPU start ticks, CPU busy ticks; then start UTC
  //! ticks, sequence id (32 hex digits, start markers only), symbol size, modules (hex): row major, one bit per module (1 = dark), most
  //! significant bit first, the last byte zero padded.
  void WriteModuleDigest(const std::filesystem::path& directory)
  {
    constexpr int32_t RowCount = 512;
    std::ofstream digest(directory / "modules.csv");
    if (!digest)
    {
      throw std::runtime_error("Failed to create modules.csv in '" + directory.string() + "'");
    }
    // The payload's columns in the order of the wire format
    digest << "kind,runId,frameIndex,flags,animationTicks,preferredFrameTicks,targetFrameTicks,intendedDisplayTicks,cpuStartTicks,cpuBusyTicks,"
              "startUtcTicks,sequenceIdHex,size,modulesHex\n";

    // "mb-frame": with it both symbol versions (2 and 6) use all eight masks in these rows. Check that again when the payload's bytes
    // change (the mask is in a symbol's format bits), and take the next seed that does when it no longer holds
    uint64_t state = 0x6D622D6672616D65u;
    for (int32_t row = 0; row < RowCount; ++row)
    {
      // Drawn in this order, not the wire format's: the digest's values depend on it
      const auto kind = static_cast<FM::MarkerKind>(row % 4);
      const uint64_t frameIndex = NextRandom(state);
      const FP::TimeSpan animationTime(static_cast<int64_t>(NextRandom(state)));
      const auto runId = static_cast<uint32_t>(NextRandom(state));
      const FP::TickCount64 intendedDisplayTime(static_cast<int64_t>(NextRandom(state)));
      const FP::TimeSpan32 targetFrameTime(static_cast<uint32_t>(NextRandom(state)));
      const FP::TickCount64 cpuStartTime(static_cast<int64_t>(NextRandom(state)));
      const FP::TimeSpan32 cpuBusy(static_cast<uint32_t>(NextRandom(state) & 0xFFFFFFFFu));
      const FP::TimeSpan32 preferredFrameTime(static_cast<uint32_t>(NextRandom(state) & 0xFFFFFFFFu));
      const auto flags = static_cast<FM::MarkerFlags>(NextRandom(state) & 0xFFu);
      const FM::Payload payload(kind, runId, frameIndex, flags, animationTime, preferredFrameTime, targetFrameTime, intendedDisplayTime, cpuStartTime,
                                cpuBusy);
      FM::StartMetadata start;
      if (payload.Kind() == FM::MarkerKind::SequenceStart)
      {
        start.UtcTicks = static_cast<int64_t>(NextRandom(state) >> 1u);
        // Cycle through random bytes, a text tag of every length 1..16, no id (all zero) and all 0xFF
        switch ((row / 4) % 4)
        {
        case 0:
          for (uint8_t& value : start.Id.Bytes)
          {
            value = static_cast<uint8_t>(NextRandom(state) & 0xFFu);
          }
          break;
        case 1:
          {
            const auto length = static_cast<std::size_t>(((row / 16) % 16) + 1);
            for (std::size_t i = 0; i < length; ++i)
            {
              start.Id.Bytes[i] = static_cast<uint8_t>(0x20u + (NextRandom(state) % 95u));
            }
            break;
          }
        case 2:
          break;
        default:
          start.Id.Bytes.fill(0xFFu);
          break;
        }
      }

      FM::ModuleMatrix matrix;
      if (!FM::GenerateModules(payload, matrix, start))
      {
        throw std::runtime_error("GenerateModules failed for digest row " + std::to_string(row));
      }
      // The matrix is stored packed exactly as the digest writes it
      const std::vector<uint8_t> bits(matrix.Bits().begin(), matrix.Bits().end());
      digest << static_cast<uint32_t>(payload.Kind()) << ',' << payload.RunId() << ',' << payload.FrameIndex() << ','
             << static_cast<uint32_t>(payload.Flags()) << ',' << payload.AnimationTime().Ticks() << ',' << payload.PreferredFrameTime().Ticks() << ','
             << payload.TargetFrameTime().Ticks() << ',' << payload.IntendedDisplayTime().Ticks() << ',' << payload.CpuStartTime().Ticks() << ','
             << payload.CpuBusy().Ticks() << ',' << start.UtcTicks << ',' << SequenceIdHex(payload.Kind(), start.Id) << ',' << matrix.Size() << ','
             << ToHex(bits) << '\n';
    }
  }

  void WriteGolden(const std::filesystem::path& directory)
  {
    WriteModuleDigest(directory);

    struct GoldenCase
    {
      FM::Payload Payload;
      FM::StartMetadata Start;
    };
    // 2026-09-23T12:00:00Z
    constexpr int64_t GoldenStartUtcTicks = 639'257'616'000'000'000;
    // Arbitrary bytes that are not text: a zero byte in the middle, 0xFF, and a last byte that is not zero
    constexpr FM::SequenceId GoldenBytesId{
      {0x6Fu, 0x9Du, 0x2Cu, 0x41u, 0x8Bu, 0x3Eu, 0x4Au, 0x7Fu, 0x95u, 0xD0u, 0x1Cu, 0x00u, 0xE2u, 0xFFu, 0x80u, 0x7Au}};
    constexpr std::array<GoldenCase, 11> Cases{{
      {{FM::MarkerKind::Frame, 0u, 0u, FM::MarkerFlags::NoFlags, FP::TimeSpan{0}}, {}},
      {{FM::MarkerKind::Frame, 1u, 1u, FM::MarkerFlags::NoFlags, FP::TimeSpan{166'667}}, {}},
      {{FM::MarkerKind::Frame, 1u, 123'456'789u, FM::MarkerFlags::NoFlags, FP::TimeSpan{36'000'000'000}, FP::TimeSpan32{166'667u},
        FP::TimeSpan32{333'333u}, FP::TickCount64{987'654'321'000}, FP::TickCount64{987'653'987'666}, FP::TimeSpan32{123'456u}},
       {}},
      {{FM::MarkerKind::Frame, 2u, 42u, FM::MarkerFlags::NoFlags, FP::TimeSpan{-1}}, {}},
      {{FM::MarkerKind::Frame, 3u, 7u, FM::MarkerFlags::NoFlags, FP::TimeSpan{std::numeric_limits<int64_t>::min()}}, {}},
      {{FM::MarkerKind::Frame, std::numeric_limits<uint32_t>::max(), std::numeric_limits<uint64_t>::max(), static_cast<FM::MarkerFlags>(0xFFu),
        FP::TimeSpan{std::numeric_limits<int64_t>::max()}, FM::Payload::OnDemandFrameTime, FP::TimeSpan32{std::numeric_limits<uint32_t>::max()},
        FP::TickCount64{std::numeric_limits<int64_t>::min()}, FP::TickCount64{std::numeric_limits<int64_t>::max()},
        FP::TimeSpan32{std::numeric_limits<uint32_t>::max()}},
       {}},
      {{FM::MarkerKind::Frame, 0x21222324u, 0x0102030405060708u, FM::MarkerFlags::StaticAfter, FP::TimeSpan{0x1112131415161718},
        FP::TimeSpan32{0x71727374u}, FP::TimeSpan32{0x41424344u}, FP::TickCount64{0x3132333435363738}, FP::TickCount64{0x5152535455565758},
        FP::TimeSpan32{0x61626364u}},
       {}},
      // Start and end markers carry the frame's values too
      {{FM::MarkerKind::SequenceStart, 5u, 600u, FM::MarkerFlags::StaticAfter, FP::TimeSpan{100'000'000}, FP::TimeSpan32{10'000'000u},
        FP::TimeSpan32{0u}, FP::TickCount64{0}, FP::TickCount64{0}, FP::TimeSpan32{80'000u}},
       {0, TextSequenceId("golden-run")}},
      {{FM::MarkerKind::SequenceStart, 6u, 601u, FM::MarkerFlags::NoFlags, FP::TimeSpan{100'166'667}, FP::TimeSpan32{0}, FP::TimeSpan32{0u},
        FP::TickCount64{0}, FP::TickCount64{0}, FP::TimeSpan32{120'000u}},
       {GoldenStartUtcTicks, GoldenBytesId}},
      {{FM::MarkerKind::SequenceEnd, 5u, 900u, FM::MarkerFlags::NoFlags, FP::TimeSpan{150'000'000}, FP::TimeSpan32{0}, FP::TimeSpan32{0u},
        FP::TickCount64{0}, FP::TickCount64{0}, FP::TimeSpan32{80'000u}},
       {}},
      {{FM::MarkerKind::Sync, 0x21222324u, 0x0102030405060708u, FM::MarkerFlags::NoFlags, FP::TimeSpan{0}}, {}},
    }};
    constexpr std::array<int32_t, 4> ModuleSizes{2, 3, 4, 6};

    std::ofstream manifest(directory / "manifest.csv");
    if (!manifest)
    {
      throw std::runtime_error("Failed to create manifest in '" + directory.string() + "'");
    }
    // The payload's columns in the order of the wire format
    manifest << "file,kind,runId,frameIndex,flags,animationTicks,preferredFrameTicks,targetFrameTicks,intendedDisplayTicks,cpuStartTicks,"
                "cpuBusyTicks,startUtcTicks,sequenceIdHex,moduleSizePx,quietZoneModules,originX,originY,width,height\n";

    for (std::size_t payloadIndex = 0; payloadIndex < Cases.size(); ++payloadIndex)
    {
      for (const int32_t moduleSize : ModuleSizes)
      {
        RenderRequest request;
        request.Payload = Cases[payloadIndex].Payload;
        request.Start = Cases[payloadIndex].Start;
        request.Options = FM::Options(moduleSize);
        // Origin and canvas are multiples of 12 (lcm of 2,3,4,6) so every integer downscale test keeps module edges pixel aligned.
        request.Origin = {36, 36};
        const int32_t canvas = ((request.Origin.X + request.Options.MarkerSizePx() + 36 + 11) / 12) * 12;
        request.CanvasWidth = canvas;
        request.CanvasHeight = canvas;

        const Image image = Render(request);
        std::string fileName = "marker_p";
        fileName += std::to_string(payloadIndex);
        fileName += "_m";
        fileName += std::to_string(moduleSize);
        fileName += ".pgm";
        WritePgm(directory / fileName, image);
        manifest << fileName << ',' << static_cast<uint32_t>(request.Payload.Kind()) << ',' << request.Payload.RunId() << ','
                 << request.Payload.FrameIndex() << ',' << static_cast<uint32_t>(request.Payload.Flags()) << ','
                 << request.Payload.AnimationTime().Ticks() << ',' << request.Payload.PreferredFrameTime().Ticks() << ','
                 << request.Payload.TargetFrameTime().Ticks() << ',' << request.Payload.IntendedDisplayTime().Ticks() << ','
                 << request.Payload.CpuStartTime().Ticks() << ',' << request.Payload.CpuBusy().Ticks() << ',' << request.Start.UtcTicks << ','
                 << SequenceIdHex(request.Payload.Kind(), request.Start.Id) << ',' << request.Options.ModuleSizePx() << ','
                 << request.Options.QuietZoneModules() << ',' << request.Origin.X << ',' << request.Origin.Y << ',' << image.Width << ','
                 << image.Height << '\n';
      }
    }
  }

  void PrintUsage()
  {
    std::cout << "Usage:\n"
                 "  marker-render --frame <u64> --ticks <i64> [--run <u32>] [--kind frame|start|end|sync]\n"
                 "                [--intended-ticks <i64>] [--target-ticks <u32>] [--cpu-start-ticks <i64>] [--cpu-busy-ticks <u32>]\n"
                 "                [--preferred-ticks <u32>] [--flags <0-255>]\n"
                 "                [--utc-ticks <i64>] [--sequence-id <text, 1-16 printable ASCII> | --sequence-id-hex <32 hex digits>]\n"
                 "                [--module <px>] [--quiet <modules>] [--canvas <W>x<H>] [--origin <X>,<Y>]\n"
                 "                [--background <0-255>] -o <file.pgm>\n"
                 "  marker-render --golden <directory>\n";
  }
}

// Every exception is caught below; clang-tidy still follows MSVC's standard library into allocation failures past the handlers
// NOLINTNEXTLINE(bugprone-exception-escape)
int main(int argc, char* argv[])
{
  try
  {
    RenderRequest request;
    std::string outputPath;
    std::string goldenDirectory;
    int32_t moduleSize = FM::Options::DefaultModuleSizePx;
    int32_t quietZone = FM::Options::RecommendedQuietZoneModules;
    FM::MarkerKind kind = FM::MarkerKind::Frame;
    uint32_t runId = 0;
    uint64_t frameIndex = 0;
    FM::MarkerFlags flags = FM::MarkerFlags::NoFlags;
    FP::TimeSpan animationTime;
    FP::TimeSpan32 preferredFrameTime;
    FP::TimeSpan32 targetFrameTime;
    FP::TickCount64 intendedDisplayTime;
    FP::TickCount64 cpuStartTime;
    FP::TimeSpan32 cpuBusy;

    const std::span<char* const> arguments(argv, static_cast<std::size_t>(argc));
    for (std::size_t i = 1; i < arguments.size(); ++i)
    {
      const std::string_view arg(arguments[i]);
      const auto next = [&]() -> std::string_view
      {
        if (i + 1 >= arguments.size())
        {
          throw std::invalid_argument("Missing value for " + std::string(arg));
        }
        return arguments[++i];
      };

      if (arg == "--help" || arg == "-h")
      {
        PrintUsage();
        return 0;
      }
      if (arg == "--frame")
      {
        frameIndex = ParseNumber<uint64_t>(next(), arg);
      }
      else if (arg == "--ticks")
      {
        animationTime = FP::TimeSpan(ParseNumber<int64_t>(next(), arg));
      }
      else if (arg == "--intended-ticks")
      {
        intendedDisplayTime = FP::TickCount64(ParseNumber<int64_t>(next(), arg));
      }
      else if (arg == "--target-ticks")
      {
        targetFrameTime = FP::TimeSpan32(ParseNumber<uint32_t>(next(), arg));
      }
      else if (arg == "--cpu-start-ticks")
      {
        cpuStartTime = FP::TickCount64(ParseNumber<int64_t>(next(), arg));
      }
      else if (arg == "--cpu-busy-ticks")
      {
        cpuBusy = FP::TimeSpan32(ParseNumber<uint32_t>(next(), arg));
      }
      else if (arg == "--preferred-ticks")
      {
        preferredFrameTime = FP::TimeSpan32(ParseNumber<uint32_t>(next(), arg));
      }
      else if (arg == "--flags")
      {
        flags = static_cast<FM::MarkerFlags>(ParseNumber<uint8_t>(next(), arg));
      }
      else if (arg == "--sequence-id")
      {
        const std::string_view text = next();
        if (!FM::SequenceId::TryFromText(text, request.Start.Id))
        {
          throw std::invalid_argument("--sequence-id takes 1 to 16 printable ASCII characters (--sequence-id-hex takes any 16 bytes)");
        }
      }
      else if (arg == "--sequence-id-hex")
      {
        request.Start.Id = ParseSequenceIdHex(next(), arg);
      }
      else if (arg == "--utc-ticks")
      {
        request.Start.UtcTicks = ParseNumber<int64_t>(next(), arg);
      }
      else if (arg == "--run")
      {
        runId = ParseNumber<uint32_t>(next(), arg);
      }
      else if (arg == "--kind")
      {
        const std::string_view kindName = next();
        if (kindName == "frame")
        {
          kind = FM::MarkerKind::Frame;
        }
        else if (kindName == "start")
        {
          kind = FM::MarkerKind::SequenceStart;
        }
        else if (kindName == "end")
        {
          kind = FM::MarkerKind::SequenceEnd;
        }
        else if (kindName == "sync")
        {
          kind = FM::MarkerKind::Sync;
        }
        else
        {
          throw std::invalid_argument("--kind must be frame, start, end or sync");
        }
      }
      else if (arg == "--module")
      {
        moduleSize = ParseNumber<int32_t>(next(), arg);
      }
      else if (arg == "--quiet")
      {
        quietZone = ParseNumber<int32_t>(next(), arg);
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
        request.Background = ParseNumber<uint8_t>(next(), arg);
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
    if (moduleSize < FM::Options::MinModuleSizePx || moduleSize > FM::Options::MaxModuleSizePx || quietZone < 0 ||
        quietZone > FM::Options::MaxQuietZoneModules)
    {
      throw std::invalid_argument("Invalid module size or quiet zone");
    }
    request.Payload =
      FM::Payload(kind, runId, frameIndex, flags, animationTime, preferredFrameTime, targetFrameTime, intendedDisplayTime, cpuStartTime, cpuBusy);
    request.Options = FM::Options(moduleSize, quietZone);
    WritePgm(outputPath, Render(request));
    return 0;
  }
  catch (const std::exception& ex)
  {
    std::cerr << "marker-render: " << ex.what() << '\n';
    return 1;
  }
  catch (...)
  {
    std::cerr << "marker-render: unknown error\n";
    return 1;
  }
}
