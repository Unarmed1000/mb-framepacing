#ifndef MB_FRAMEPACING_MARKER_FRAMEMARKER_HPP
#define MB_FRAMEPACING_MARKER_FRAMEMARKER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The marker module's functions (C#'s FrameMarker): encode a payload, generate its module matrix and draw it as pixel aligned geometry or a
// bitmap. The marker encodes a frame index and the animation time (C# TimeSpan ticks, 100 ns) so a capture of the display output can be
// compared against the capture timeline. See doc/marker-format.md for the full specification.
//
// Coordinate system: pixels, origin at the top-left corner, +x to the right, +y down.
// Every quad edge lies on an integer pixel edge and a quad covers exactly the pixels [Left(),Right()) x [Top(),Bottom()).

#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/marker/IndexedCount.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <mb/framepacing/marker/MarkerQuad.hpp>
#include <mb/framepacing/marker/ModuleMatrix.hpp>
#include <mb/framepacing/marker/Options.hpp>
#include <mb/framepacing/marker/Payload.hpp>
#include <mb/framepacing/marker/PixelFormat.hpp>
#include <mb/framepacing/marker/StartMetadata.hpp>
#include <mb/framepacing/marker/Vertex.hpp>
#include <cstddef>
#include <cstdint>
#include <span>

namespace MB::FramePacing::Marker
{
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

  //! Serialize a complete payload into dst (Payload::MaxEncodedByteCount bytes is always enough). Start markers append the metadata, other
  //! kinds ignore it. A frame or end marker is 53 bytes, a start marker 77, a sync marker 16
  //! (doc/marker-format.md). Returns the number of bytes written, or 0 if dst is too small.
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
  //! Returns the number of quads written, or 0 if the matrix is empty or dst is too small.
  std::size_t ModulesToQuads(const ModuleMatrix& matrix, const Options& options, Point origin, std::span<MarkerQuad> dst) noexcept;

  //! The marker as a triangle list, written straight into dst: 6 vertices per quad (see ModulesToQuads for the quad order), (TL, TR, BL)
  //! (BL, TR, BR), clockwise on screen. Every vertex lies on a pixel corner. Every marker needs at most MaxTriangleVertexCount() vertices.
  //! Returns the number of vertices written, or 0 if the matrix is empty or dst is too small.
  std::size_t ModulesToTriangles(const ModuleMatrix& matrix, const Options& options, Point origin, std::span<Vertex> dst) noexcept;

  //! The marker as an indexed triangle list: 4 vertices (TL, TR, BR, BL) and 6 indices (0,1,3)(3,1,2) per quad, clockwise on screen.
  //! baseVertex is added to every index. Every marker needs at most MaxIndexedVertexCount() vertices and MaxIndexCount() indices.
  //! Returns {0,0} if the matrix is empty or a destination is too small.
  IndexedCount ModulesToIndexed(const ModuleMatrix& matrix, const Options& options, Point origin, std::span<Vertex> dstVertices,
                                std::span<uint32_t> dstIndices, uint32_t baseVertex = 0) noexcept;

  //! The marker's static grid, for drawing it with per-frame indices only (ModulesToGridIndices): the vertices stay the same while the kind's
  //! symbol size, the options and the origin do. Vertices 0..3 are the light background (TL, TR, BR, BL, luma 255); then the corners of
  //! the modules, dark (luma 0), row-major: corner (column, row) is vertex 4 + row x (N + 1) + column, N the kind's modules per side. Every
  //! vertex lies on a pixel corner. Does not allocate.
  //! Returns the number of vertices written (GridVertexCount(kind)), or 0 if dst is too small.
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
    case PixelFormat::R8G8B8:
      return 3;
    case PixelFormat::R8G8B8A8:
      return 4;
    case PixelFormat::R8:
      break;
    }
    return 1;
  }

  //! Draw the marker into a width x height pixel buffer, rows stride bytes apart (0 = width x BytesPerPixel(format)): the light background
  //! (symbol + quiet zone), then the dark modules, 0 (dark) or 255 (light) in every colour channel and alpha 255. The marker is clipped to
  //! the buffer; other pixels are left as they are. With a module size of 1 and origin (0,0) this is a module-resolution image (a texture to
  //! scale up with point filtering). Does not allocate.
  //! Returns false, writing nothing, if the matrix is empty, the stride is shorter than a row or dst is too small.
  bool ModulesToBitmap(const ModuleMatrix& matrix, const Options& options, Point origin, std::span<uint8_t> dst, int32_t width, int32_t height,
                       PixelFormat format, std::size_t stride = 0) noexcept;
}

#endif
