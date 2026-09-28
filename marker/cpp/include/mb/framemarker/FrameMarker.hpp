#ifndef MB_FRAMEMARKER_FRAMEMARKER_HPP
#define MB_FRAMEMARKER_FRAMEMARKER_HPP
// SPDX-License-Identifier: BSD-3-Clause
//
// MB::FrameMarker - renders a machine readable frame marker (QR code) as pixel aligned geometry or a bitmap.
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
#include <mb/framemarker/ModuleMatrix.hpp>
#include <mb/framemarker/Options.hpp>
#include <mb/framemarker/Payload.hpp>
#include <mb/framemarker/PixelFormat.hpp>
#include <mb/framemarker/Point.hpp>
#include <mb/framemarker/Quad.hpp>
#include <mb/framemarker/SequenceId.hpp>
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

  //! Modules per side of a marker's symbol: the main marker (frame, start and end) or the smaller sync marker.
  constexpr int32_t QrModuleCountFor(const MarkerKind kind) noexcept
  {
    return kind == MarkerKind::Sync ? SyncQrModuleCount : QrModuleCount;
  }

  //! Width and height in source pixels of a marker (symbol + quiet zone). Frame, start and end markers have one size, the sync marker is
  //! smaller.
  constexpr int32_t MarkerSizePx(const Options& options, const MarkerKind kind = MarkerKind::Frame) noexcept
  {
    return (QrModuleCountFor(kind) + (2 * options.QuietZoneModules)) * options.ModuleSizePx;
  }

  //! Upper bound on the number of quads for any marker: one background quad plus at most one quad per dark run.
  constexpr std::size_t MaxQuadCount() noexcept
  {
    return 1u + (static_cast<std::size_t>(QrModuleCount) * ((static_cast<std::size_t>(QrModuleCount) + 1u) / 2u));
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

  //! Vertices of a marker kind's static grid (GridVertices): 4 for the light background, then every module corner, (N + 1)^2. 1768 for the
  //! main marker, 680 for the sync marker; both fit 16-bit indices.
  constexpr std::size_t GridVertexCount(const MarkerKind kind) noexcept
  {
    const auto corners = static_cast<std::size_t>(QrModuleCountFor(kind)) + 1u;
    return 4u + (corners * corners);
  }

  constexpr std::size_t MaxGridVertexCount() noexcept
  {
    return GridVertexCount(MarkerKind::Frame);
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

  //! Recommended origin of a marker: the main marker (frame, start and end) top-left, the sync marker bottom-left. alignPx should be the
  //! integer downscale ratio (1 if none) so module edges land on stored pixel edges.
  constexpr Point RecommendedOrigin(const MarkerKind kind, const int32_t sourceWidth, const int32_t sourceHeight, const Options& options,
                                    const int32_t alignPx = 1) noexcept
  {
    (void)sourceWidth;
    const int32_t inset = Detail::AlignUp(RecommendedInsetPx, alignPx);
    if (kind == MarkerKind::Sync)
    {
      return {inset, Detail::AlignDown(sourceHeight - inset - MarkerSizePx(options, kind), alignPx)};
    }
    return {inset, inset};
  }

  //! Serialize a complete payload into dst (MaxEncodedPayloadByteCount bytes is always enough). Start markers append the metadata, other
  //! kinds ignore it. A frame or end marker is PayloadByteCount bytes, a start marker StartPayloadByteCount, a sync marker
  //! SyncPayloadByteCount. Returns the number of bytes written, or 0 if dst is too small.
  std::size_t EncodePayload(const Payload& payload, const StartMetadata& metadata, std::span<uint8_t> dst) noexcept;

  //! Parse the wire format. Returns false on a wrong length, magic, format version or an unknown kind.
  //! For a start marker pMetadata (optional) receives the metadata; other kinds reset it.
  bool TryDecodePayload(std::span<const uint8_t> bytes, Payload& rPayload, StartMetadata* pMetadata = nullptr) noexcept;

  //! Encode a marker: the payload's QR symbol as a module matrix, the one step every drawing output starts from (metadata is only used by
  //! start markers). Draw it with ModulesToQuads, ModulesToTriangles, ModulesToIndexed or ModulesToBitmap; one matrix can feed several.
  //! Does not allocate. Returns false (rMatrix unchanged) if the payload cannot be encoded.
  bool GenerateModules(const Payload& payload, ModuleMatrix& rMatrix, const StartMetadata& metadata = {}) noexcept;

  //! The marker as quads: the light background (symbol + quiet zone) first, then one dark quad per horizontal run of dark modules. Draw
  //! them in order. Every marker produces at most MaxQuadCount() quads. Does not allocate.
  //! Returns the number of quads written, or 0 if the options are invalid, the matrix is empty or dst is too small.
  std::size_t ModulesToQuads(const ModuleMatrix& matrix, const Options& options, Point origin, std::span<Quad> dst) noexcept;

  //! The marker as a triangle list, written straight into dst: 6 vertices per quad (see ModulesToQuads for the quad order), (TL, TR, BL)
  //! (BL, TR, BR), clockwise on screen. Every vertex lies on a pixel corner. Every marker needs at most MaxTriangleVertexCount() vertices.
  //! Returns the number of vertices written, or 0 if the options are invalid, the matrix is empty or dst is too small.
  std::size_t ModulesToTriangles(const ModuleMatrix& matrix, const Options& options, Point origin, std::span<Vertex> dst) noexcept;

  //! The marker as an indexed triangle list: 4 vertices (TL, TR, BR, BL) and 6 indices (0,1,3)(3,1,2) per quad, clockwise on screen.
  //! baseVertex is added to every index. Every marker needs at most MaxIndexedVertexCount() vertices and MaxIndexCount() indices.
  //! Returns {0,0} if the options are invalid, the matrix is empty or a destination is too small.
  IndexedCount ModulesToIndexed(const ModuleMatrix& matrix, const Options& options, Point origin, std::span<Vertex> dstVertices,
                                std::span<uint32_t> dstIndices, uint32_t baseVertex = 0) noexcept;

  //! The marker's static grid, for drawing it with per-frame indices only (ModulesToGridIndices): the vertices stay the same while the kind's
  //! symbol size, the options and the origin do. Vertices 0..3 are the light background (TL, TR, BR, BL, luma 255); then the corners of
  //! the modules, dark (luma 0), row-major: corner (column, row) is vertex 4 + row x (N + 1) + column, N the kind's modules per side. Every
  //! vertex lies on a pixel corner. Does not allocate.
  //! Returns the number of vertices written (GridVertexCount(kind)), or 0 if the options are invalid or dst is too small.
  std::size_t GridVertices(MarkerKind kind, const Options& options, Point origin, std::span<Vertex> dst) noexcept;

  //! The per-frame part of the grid drawing: the indices of the background, (0,1,3)(3,1,2), then 6 per horizontal run of dark modules,
  //! (TL, TR, BL) (BL, TR, BR) of the run's grid corners, clockwise on screen. Use the grid of the matrix's kind (a Sync matrix with the
  //! Sync grid). baseVertex is added to every index. Every marker needs at most MaxIndexCount() indices. Does not allocate.
  //! Returns the number of indices written, or 0 if the matrix is empty or dst is too small.
  std::size_t ModulesToGridIndices(const ModuleMatrix& matrix, std::span<uint32_t> dst, uint32_t baseVertex = 0) noexcept;

  //! Bytes per pixel of a PixelFormat.
  constexpr int32_t BytesPerPixel(const PixelFormat format) noexcept
  {
    switch (format)
    {
    case PixelFormat::Rgb24:
      return 3;
    case PixelFormat::Rgba32:
      return 4;
    case PixelFormat::Gray8:
      break;
    }
    return 1;
  }

  //! Draw the marker into a width x height pixel buffer, rows stride bytes apart (0 = width x BytesPerPixel(format)): the light background
  //! (symbol + quiet zone), then the dark modules, 0 (dark) or 255 (light) in every colour channel and alpha 255. The marker is clipped to
  //! the buffer; other pixels are left as they are. With ModuleSizePx 1 and origin (0,0) this is a module-resolution image (a texture to
  //! scale up with point filtering). Does not allocate.
  //! Returns false, writing nothing, if the options are invalid, the matrix is empty, the stride is shorter than a row or dst is too small.
  bool ModulesToBitmap(const ModuleMatrix& matrix, const Options& options, Point origin, std::span<uint8_t> dst, int32_t width, int32_t height,
                       PixelFormat format, std::size_t stride = 0) noexcept;
}

#endif
