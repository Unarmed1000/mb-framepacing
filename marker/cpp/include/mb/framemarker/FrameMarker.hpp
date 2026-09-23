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
//
// This is the header to include: it pulls in every type (one header per type) and declares the functions.

#include <mb/framemarker/Constants.hpp>
#include <mb/framemarker/IndexedCount.hpp>
#include <mb/framemarker/MarkerKind.hpp>
#include <mb/framemarker/MarkerSlot.hpp>
#include <mb/framemarker/ModuleMatrix.hpp>
#include <mb/framemarker/Options.hpp>
#include <mb/framemarker/Payload.hpp>
#include <mb/framemarker/Point.hpp>
#include <mb/framemarker/Quad.hpp>
#include <mb/framemarker/StartMetadata.hpp>
#include <mb/framemarker/Version.hpp>
#include <mb/framemarker/Vertex.hpp>
#include <array>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <ratio>
#include <span>

namespace MB::FrameMarker
{
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

  //! Vertex and index counts for a frame or end marker (a start marker needs the Max... variants above).
  constexpr std::size_t MaxFrameTriangleVertexCount() noexcept
  {
    return MaxFrameQuadCount() * 6u;
  }

  constexpr std::size_t MaxFrameIndexedVertexCount() noexcept
  {
    return MaxFrameQuadCount() * 4u;
  }

  constexpr std::size_t MaxFrameIndexCount() noexcept
  {
    return MaxFrameQuadCount() * 6u;
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

  //! Generate the marker as a triangle list, written straight into dst: 6 vertices per quad (see GenerateQuads for the quad order),
  //! (TL, TR, BL) (BL, TR, BR), clockwise on screen. Every vertex lies on a pixel corner. Does not allocate.
  //! Frame/end markers need at most MaxFrameTriangleVertexCount() vertices, start markers MaxTriangleVertexCount().
  //! Returns the number of vertices written, or 0 if the options are invalid or dst is too small.
  std::size_t GenerateTriangles(const Payload& payload, const Options& options, Point origin, std::span<Vertex> dst) noexcept;

  //! GenerateTriangles for a start marker carrying metadata (payload.Kind is forced to SequenceStart).
  std::size_t GenerateStartTriangles(const Payload& payload, const StartMetadata& metadata, const Options& options, Point origin,
                                     std::span<Vertex> dst) noexcept;

  //! Generate the marker as an indexed triangle list: 4 vertices (TL, TR, BR, BL) and 6 indices (0,1,3)(3,1,2) per quad, clockwise on
  //! screen. baseVertex is added to every index. Does not allocate.
  //! Frame/end markers need at most MaxFrameIndexedVertexCount() vertices and MaxFrameIndexCount() indices.
  //! Returns {0,0} if the options are invalid or a destination is too small.
  IndexedCount GenerateIndexed(const Payload& payload, const Options& options, Point origin, std::span<Vertex> dstVertices,
                               std::span<uint32_t> dstIndices, uint32_t baseVertex = 0) noexcept;

  //! GenerateIndexed for a start marker carrying metadata (payload.Kind is forced to SequenceStart).
  IndexedCount GenerateStartIndexed(const Payload& payload, const StartMetadata& metadata, const Options& options, Point origin,
                                    std::span<Vertex> dstVertices, std::span<uint32_t> dstIndices, uint32_t baseVertex = 0) noexcept;

  //! Convert quads to a triangle list: 6 vertices per quad, (TL, TR, BL) (BL, TR, BR), clockwise on screen (+y down).
  //! Returns the number of vertices written, or 0 if dst is too small.
  std::size_t QuadsToTriangles(std::span<const Quad> quads, std::span<Vertex> dst) noexcept;

  //! Convert quads to an indexed triangle list: 4 vertices (TL, TR, BR, BL) and 6 indices (0,1,3)(3,1,2) per quad, clockwise on screen.
  //! baseVertex is added to every index. Returns {0,0} if a destination is too small.
  IndexedCount QuadsToIndexed(std::span<const Quad> quads, std::span<Vertex> dstVertices, std::span<uint32_t> dstIndices,
                              uint32_t baseVertex = 0) noexcept;
}

#endif
