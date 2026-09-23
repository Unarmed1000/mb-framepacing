#ifndef MB_FRAMEMARKER_FRAMEMARKER_HPP
#define MB_FRAMEMARKER_FRAMEMARKER_HPP
// SPDX-License-Identifier: BSD-3-Clause
//
// MB::FrameMarker - renders a machine readable frame marker (QR code) as pixel aligned geometry.
//
// The marker encodes a frame index and the animation time (C# TimeSpan ticks, 100ns) so a capture of the display output
// can be compared against the capture timeline. See doc/marker-format.md for the full specification.
//
// Coordinate system: pixels, origin at the top-left corner, +x to the right, +y down.
// Every quad edge lies on an integer pixel edge and a quad covers exactly the pixels [Left,Right) x [Top,Bottom).

#include <mb/framemarker/Version.hpp>
#include <array>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <ratio>
#include <span>
#include <string_view>

namespace MB::FrameMarker
{
  //! Frame and end markers are fixed to QR version 2 (25x25 modules), ECC level M, byte mode, so they never change size.
  inline constexpr int32_t FrameQrVersion = 2;
  //! Start markers carry metadata and use the smallest version in [FrameQrVersion, MaxQrVersion] that fits.
  inline constexpr int32_t MaxQrVersion = 6;

  constexpr int32_t QrModuleCountForVersion(const int32_t version) noexcept
  {
    return (4 * version) + 17;
  }

  inline constexpr int32_t FrameQrModuleCount = QrModuleCountForVersion(FrameQrVersion);
  inline constexpr int32_t MaxQrModuleCount = QrModuleCountForVersion(MaxQrVersion);

  //! Payload header, shared by every marker kind (little endian):
  //! magic "MF" (2) | format version (1) | kind (1) | frame index u64 (8) | animation ticks i64 (8) | run id u32 (4)
  inline constexpr std::size_t PayloadByteCount = 24;
  inline constexpr uint8_t PayloadMagic0 = 'M';
  inline constexpr uint8_t PayloadMagic1 = 'F';
  inline constexpr uint8_t PayloadFormatVersion = 1;

  //! Start marker payload: header (24) | start time UTC i64 (8) | name length u8 (1) | name UTF-8 (0..MaxStartNameBytes)
  inline constexpr std::size_t MaxStartNameBytes = 64;
  inline constexpr std::size_t StartPayloadFixedByteCount = PayloadByteCount + 8u + 1u;
  inline constexpr std::size_t MaxEncodedPayloadByteCount = StartPayloadFixedByteCount + MaxStartNameBytes;

  //! C# TimeSpan / DateTime resolution
  inline constexpr int64_t TicksPerSecond = 10'000'000;
  //! C# DateTime ticks (since 0001-01-01) at the Unix epoch
  inline constexpr int64_t UnixEpochDateTimeTicks = 621'355'968'000'000'000;

  //! Recommended distance in source pixels between the marker and the edge of the frame.
  inline constexpr int32_t RecommendedInsetPx = 32;

  inline constexpr int32_t MinModuleSizePx = 1;
  inline constexpr int32_t MaxModuleSizePx = 1024;
  inline constexpr int32_t MaxQuietZoneModules = 16;
  inline constexpr int32_t RecommendedQuietZoneModules = 4;

  //! What a marker means. Frame markers are drawn every frame of a test run; the sequence markers bracket the run so the analyzer can cut
  //! the capture to exactly the measured window. See doc/marker-format.md "Test sequences".
  enum class MarkerKind : uint8_t
  {
    Frame = 0,
    SequenceStart = 1,
    SequenceEnd = 2,
  };

  inline constexpr uint8_t MaxMarkerKindValue = static_cast<uint8_t>(MarkerKind::SequenceEnd);

  struct Payload
  {
    //! The application's own rendered-frame counter. Unrelated to the capture card's frame counter.
    uint64_t FrameIndex{0};
    //! Animation time in C# TimeSpan ticks (100ns).
    int64_t AnimationTicks{0};
    //! Identifies one test run. The start marker, every frame marker and the end marker of a run carry the same id.
    uint32_t RunId{0};
    MarkerKind Kind{MarkerKind::Frame};

    constexpr bool operator==(const Payload&) const noexcept = default;
  };

  //! Extra data carried by a SequenceStart marker.
  struct StartMetadata
  {
    //! Wall clock start time as C# DateTime UTC ticks (100ns since 0001-01-01), 0 = unknown. See ToDateTimeTicks.
    int64_t UtcTicks{0};
    //! Test name, UTF-8, at most MaxStartNameBytes bytes. Not copied: must stay valid while encoding.
    std::string_view Name;
  };

  struct Options
  {
    //! Size of one QR module in source pixels. See doc/marker-format.md "Sizing" or RecommendModuleSizePx.
    int32_t ModuleSizePx{6};
    //! White border around the symbol in modules. The QR specification asks for 4.
    int32_t QuietZoneModules{RecommendedQuietZoneModules};
  };

  struct Point
  {
    int32_t X{0};
    int32_t Y{0};

    constexpr bool operator==(const Point&) const noexcept = default;
  };

  //! Axis aligned rectangle covering the pixels [Left,Right) x [Top,Bottom).
  struct Quad
  {
    int32_t Left{0};
    int32_t Top{0};
    int32_t Right{0};
    int32_t Bottom{0};
    //! true: draw black (luma 0), false: draw white (luma 255)
    bool Dark{false};

    constexpr bool operator==(const Quad&) const noexcept = default;
  };

  struct Vertex
  {
    int32_t X{0};
    int32_t Y{0};
    //! 0 (dark) or 255 (light). Render it as the RGB color (Luma, Luma, Luma).
    uint8_t Luma{0};

    constexpr bool operator==(const Vertex&) const noexcept = default;
  };

  struct IndexedCount
  {
    std::size_t VertexCount{0};
    std::size_t IndexCount{0};
  };

  enum class MarkerSlot
  {
    TopLeft,
    MiddleLeft,
    BottomLeft,
  };

  //! The QR module matrix, 1 = dark module. Only the top-left Size x Size modules are used; rows are MaxQrModuleCount apart.
  struct ModuleMatrix
  {
    int32_t Size{0};
    std::array<uint8_t, static_cast<std::size_t>(MaxQrModuleCount) * MaxQrModuleCount> Modules{};

    constexpr bool IsDark(const int32_t x, const int32_t y) const noexcept
    {
      return Modules[(static_cast<std::size_t>(y) * MaxQrModuleCount) + static_cast<std::size_t>(x)] != 0;
    }
  };

  constexpr bool IsValid(const Options& options) noexcept
  {
    return options.ModuleSizePx >= MinModuleSizePx && options.ModuleSizePx <= MaxModuleSizePx && options.QuietZoneModules >= 0 &&
           options.QuietZoneModules <= MaxQuietZoneModules;
  }

  //! Width and height in source pixels of a marker (symbol + quiet zone) with the given symbol size.
  constexpr int32_t MarkerSizePx(const Options& options, const int32_t moduleCount) noexcept
  {
    return (moduleCount + (2 * options.QuietZoneModules)) * options.ModuleSizePx;
  }

  //! Width and height in source pixels of a frame or end marker (symbol + quiet zone).
  constexpr int32_t MarkerSizePx(const Options& options) noexcept
  {
    return MarkerSizePx(options, FrameQrModuleCount);
  }

  //! Largest possible start marker (a 64 byte name). Keep this area free around the marker origin while the start marker shows.
  constexpr int32_t MaxMarkerSizePx(const Options& options) noexcept
  {
    return MarkerSizePx(options, MaxQrModuleCount);
  }

  //! Upper bound on the number of quads for any marker: one background quad plus at most one quad per dark run.
  constexpr std::size_t MaxQuadCount() noexcept
  {
    return 1u + (static_cast<std::size_t>(MaxQrModuleCount) * ((static_cast<std::size_t>(MaxQrModuleCount) + 1u) / 2u));
  }

  //! Upper bound on the number of quads for a frame or end marker.
  constexpr std::size_t MaxFrameQuadCount() noexcept
  {
    return 1u + (static_cast<std::size_t>(FrameQrModuleCount) * ((static_cast<std::size_t>(FrameQrModuleCount) + 1u) / 2u));
  }

  constexpr std::size_t MaxTriangleVertexCount() noexcept
  {
    return MaxQuadCount() * 6u;
  }

  constexpr std::size_t MaxIndexedVertexCount() noexcept
  {
    return MaxQuadCount() * 4u;
  }

  constexpr std::size_t MaxIndexCount() noexcept
  {
    return MaxQuadCount() * 6u;
  }

  //! Convert a wall clock time to C# DateTime UTC ticks (the StartMetadata::UtcTicks format).
  constexpr int64_t ToDateTimeTicks(const std::chrono::system_clock::time_point timePoint) noexcept
  {
    return UnixEpochDateTimeTicks +
           std::chrono::duration_cast<std::chrono::duration<int64_t, std::ratio<1, TicksPerSecond>>>(timePoint.time_since_epoch()).count();
  }

  namespace Detail
  {
    constexpr int32_t CeilDiv(const int64_t numerator, const int64_t denominator) noexcept
    {
      return static_cast<int32_t>((numerator + denominator - 1) / denominator);
    }

    constexpr int32_t AlignDown(const int32_t value, const int32_t alignment) noexcept
    {
      return alignment <= 1 ? value : (value / alignment) * alignment;
    }

    constexpr int32_t AlignUp(const int32_t value, const int32_t alignment) noexcept
    {
      return alignment <= 1 ? value : CeilDiv(value, alignment) * alignment;
    }

    constexpr int32_t ModuleSizeForStoredPx(const int32_t storedPxPerModule, const int32_t sourceHeight, const int32_t storedHeight) noexcept
    {
      if (sourceHeight <= 0 || storedHeight <= 0)
      {
        return storedPxPerModule;
      }
      // ModuleSizePx = ceil(storedPxPerModule / s) where s = storedHeight / sourceHeight
      const int32_t size = CeilDiv(static_cast<int64_t>(storedPxPerModule) * sourceHeight, storedHeight);
      return size < storedPxPerModule ? storedPxPerModule : size;
    }
  }

  //! Hard minimum module size: 2 stored pixels per module after all scaling (source -> capture -> stored).
  constexpr int32_t MinimumModuleSizePx(const int32_t sourceHeight, const int32_t storedHeight) noexcept
  {
    return Detail::ModuleSizeForStoredPx(2, sourceHeight, storedHeight);
  }

  //! Recommended module size: 3 stored pixels per module, or 4 when the capture card delivers MJPEG.
  constexpr int32_t RecommendModuleSizePx(const int32_t sourceHeight, const int32_t storedHeight, const bool mjpeg = false) noexcept
  {
    return Detail::ModuleSizeForStoredPx(mjpeg ? 4 : 3, sourceHeight, storedHeight);
  }

  //! Recommended marker origin for the given slot. alignPx should be the integer downscale ratio (1 if none) so module edges land on
  //! stored pixel edges.
  constexpr Point RecommendedOrigin(const MarkerSlot slot, const int32_t sourceWidth, const int32_t sourceHeight, const Options& options,
                                    const int32_t alignPx = 1) noexcept
  {
    (void)sourceWidth;
    const int32_t size = MarkerSizePx(options);
    const int32_t inset = Detail::AlignUp(RecommendedInsetPx, alignPx);
    switch (slot)
    {
    case MarkerSlot::MiddleLeft:
      return {inset, Detail::AlignDown((sourceHeight - size) / 2, alignPx)};
    case MarkerSlot::BottomLeft:
      return {inset, Detail::AlignDown(sourceHeight - inset - size, alignPx)};
    case MarkerSlot::TopLeft:
    default:
      return {inset, inset};
    }
  }

  //! Serialize the 24 byte payload header (the complete payload of frame and end markers).
  std::array<uint8_t, PayloadByteCount> EncodePayload(const Payload& payload) noexcept;

  //! Serialize a complete payload into dst (MaxEncodedPayloadByteCount bytes is always enough). Start markers append the metadata, other
  //! kinds ignore it. Returns the number of bytes written, or 0 if dst is too small or the name is longer than MaxStartNameBytes.
  std::size_t EncodePayload(const Payload& payload, const StartMetadata& metadata, std::span<uint8_t> dst) noexcept;

  //! Parse the wire format. Returns false on a wrong length, magic, format version or an unknown kind.
  //! For a start marker pMetadata (optional) receives the metadata; its Name points into bytes.
  bool TryDecodePayload(std::span<const uint8_t> bytes, Payload& rPayload, StartMetadata* pMetadata = nullptr) noexcept;

  //! Build the QR module matrix for the payload (metadata is only used by start markers).
  //! Returns false if the metadata is invalid or the QR encoder fails.
  bool GenerateModules(const Payload& payload, ModuleMatrix& rMatrix, const StartMetadata& metadata = {}) noexcept;

  //! Generate the marker as quads. The first quad is the light background (symbol + quiet zone), followed by one dark quad per
  //! horizontal run of dark modules. Draw them in order. Does not allocate.
  //! Frame/end markers produce at most MaxFrameQuadCount() quads. A start marker generated here carries empty metadata.
  //! Returns the number of quads written, or 0 if the options are invalid or dst is smaller than the generated quad count.
  std::size_t GenerateQuads(const Payload& payload, const Options& options, Point origin, std::span<Quad> dst) noexcept;

  //! Generate a start marker carrying metadata (payload.Kind is forced to SequenceStart). At most MaxQuadCount() quads, and the marker
  //! is at most MaxMarkerSizePx(options) wide and high. Same rules as GenerateQuads otherwise.
  std::size_t GenerateStartQuads(const Payload& payload, const StartMetadata& metadata, const Options& options, Point origin,
                                 std::span<Quad> dst) noexcept;

  //! Convert quads to a triangle list: 6 vertices per quad, (TL, TR, BL) (BL, TR, BR), clockwise on screen (+y down).
  //! Returns the number of vertices written, or 0 if dst is too small.
  std::size_t QuadsToTriangles(std::span<const Quad> quads, std::span<Vertex> dst) noexcept;

  //! Convert quads to an indexed triangle list: 4 vertices (TL, TR, BR, BL) and 6 indices (0,1,3)(3,1,2) per quad, clockwise on screen.
  //! baseVertex is added to every index. Returns {0,0} if a destination is too small.
  IndexedCount QuadsToIndexed(std::span<const Quad> quads, std::span<Vertex> dstVertices, std::span<uint32_t> dstIndices,
                              uint32_t baseVertex = 0) noexcept;
}

#endif
