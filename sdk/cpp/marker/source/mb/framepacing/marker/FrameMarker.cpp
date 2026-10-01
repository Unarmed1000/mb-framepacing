// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// FrameMarker.hpp's functions: the payload's wire format (doc/marker-format.md: C# must match it byte for byte), the QR symbol and
// every way of drawing it.
#include <mb/framepacing/core/ByteSpanUtil.hpp>
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
#include <algorithm>
#include <array>
#include <bit>
#include <cassert>
#include "detail/QrEncoder.hpp"
#include "detail/QrSymbol.hpp"
#include "detail/WireFormat.hpp"

namespace MB::FramePacing::Marker
{
  namespace
  {

    //! The 53 byte header every kind starts with (a sync marker is its first 16 bytes: which run and frame).
    std::array<uint8_t, WireFormat::PayloadByteCount> EncodeHeader(const Payload& payload) noexcept
    {
      std::array<uint8_t, WireFormat::PayloadByteCount> bytes{};
      bytes[WireFormat::OffsetMagic0] = WireFormat::PayloadMagic0;
      bytes[WireFormat::OffsetMagic1] = WireFormat::PayloadMagic1;
      bytes[WireFormat::OffsetVersion] = WireFormat::PayloadFormatVersion;
      bytes[WireFormat::OffsetKind] = static_cast<uint8_t>(payload.Kind());
      ByteSpanUtil::WriteLE(bytes, WireFormat::OffsetRunId, payload.RunId());
      ByteSpanUtil::WriteLE(bytes, WireFormat::OffsetFrameIndex, payload.FrameIndex());
      bytes[WireFormat::OffsetFlags] = static_cast<uint8_t>(payload.Flags());
      // Two's complement, identical to C# BinaryPrimitives.WriteInt64LittleEndian
      ByteSpanUtil::WriteLE(bytes, WireFormat::OffsetAnimationTicks, payload.AnimationTime().Ticks());
      ByteSpanUtil::WriteLE(bytes, WireFormat::OffsetPreferredFrameTicks, payload.PreferredFrameTime().Ticks());
      ByteSpanUtil::WriteLE(bytes, WireFormat::OffsetTargetFrameTicks, payload.TargetFrameTime().Ticks());
      ByteSpanUtil::WriteLE(bytes, WireFormat::OffsetIntendedDisplayTicks, payload.IntendedDisplayTime().UnsignedTicks());
      ByteSpanUtil::WriteLE(bytes, WireFormat::OffsetCpuStartTicks, payload.CpuStartTime().UnsignedTicks());
      ByteSpanUtil::WriteLE(bytes, WireFormat::OffsetCpuBusyTicks, payload.CpuBusy().Ticks());
      return bytes;
    }

    static_assert(ModuleMatrix::MainSize == 41);
    static_assert(MaxQuadCount() == 862u);
    static_assert(MaxTriangleVertexCount() == std::size_t{862} * 6u);

    //! The first module at or after column from of a row (whose first module is bit rowStart) that is dark (or light), or size when there
    //! is none. Reads whole bytes: a byte without such a module is skipped at once.
    int32_t FindModule(const std::span<const uint8_t> bits, const std::size_t rowStart, const int32_t from, const int32_t size,
                       const bool dark) noexcept
    {
      int32_t x = from;
      while (x < size)
      {
        const std::size_t index = rowStart + static_cast<std::size_t>(x);
        const auto offset = static_cast<uint32_t>(index % 8u);
        const uint32_t byte = dark ? static_cast<uint32_t>(bits[index / 8u]) : (~static_cast<uint32_t>(bits[index / 8u]) & 0xFFu);
        const auto remaining = static_cast<uint8_t>(byte & (0xFFu >> offset));
        if (remaining == 0u)
        {
          x += static_cast<int32_t>(8u - offset);
          continue;
        }
        // The most significant bit is the first module
        x += static_cast<int32_t>(static_cast<uint32_t>(std::countl_zero(remaining)) - offset);
        return std::min(x, size);
      }
      return size;
    }

    //! Every horizontal run of dark modules, row by row: emit(row, firstColumn, endColumn) returns false to stop early.
    template <typename TEmit>
    bool WalkRuns(const ModuleMatrix& matrix, const TEmit& emit) noexcept
    {
      const int32_t size = matrix.Size();
      const std::span<const uint8_t> bits = matrix.Bits();
      for (int32_t y = 0; y < size; ++y)
      {
        const std::size_t rowStart = static_cast<std::size_t>(y) * static_cast<std::size_t>(size);
        int32_t x = FindModule(bits, rowStart, 0, size, true);
        while (x < size)
        {
          const int32_t runEnd = FindModule(bits, rowStart, x, size, false);
          if (!emit(y, x, runEnd))
          {
            return false;
          }
          x = FindModule(bits, rowStart, runEnd, size, true);
        }
      }
      return true;
    }

    //! Walk the marker in draw order: the light background (symbol + quiet zone), then one dark quad per horizontal run of dark modules.
    //! Every quad goes straight to emit, which writes it in its output format and returns false when the output is full.
    template <typename TEmit>
    bool WalkQuads(const ModuleMatrix& matrix, const Options& options, const Point origin, const TEmit& emit) noexcept
    {
      const int32_t moduleSize = options.ModuleSizePx();
      const int32_t markerSize = (matrix.Size() + (2 * options.QuietZoneModules())) * moduleSize;
      const int32_t symbolLeft = origin.X + options.QuietZonePx();
      const int32_t symbolTop = origin.Y + options.QuietZonePx();

      if (!emit(MarkerQuad{Rectangle(origin.X, origin.Y, markerSize, markerSize), false}))
      {
        return false;
      }
      return WalkRuns(matrix,
                      [&](const int32_t row, const int32_t first, const int32_t end) noexcept
                      {
                        const int32_t top = symbolTop + (row * moduleSize);
                        return emit(MarkerQuad{Rectangle(symbolLeft + (first * moduleSize), top, (end - first) * moduleSize, moduleSize), true});
                      });
    }

    //! 6 vertices: (TL, TR, BL) (BL, TR, BR), clockwise on screen (+y down).
    void WriteTriangles(const MarkerQuad& quad, const std::span<Vertex, 6> dst) noexcept
    {
      const uint8_t luma = quad.Dark ? 0u : 255u;
      const Vertex topLeft{quad.Rect.Left(), quad.Rect.Top(), luma};
      const Vertex topRight{quad.Rect.Right(), quad.Rect.Top(), luma};
      const Vertex bottomRight{quad.Rect.Right(), quad.Rect.Bottom(), luma};
      const Vertex bottomLeft{quad.Rect.Left(), quad.Rect.Bottom(), luma};
      dst[0] = topLeft;
      dst[1] = topRight;
      dst[2] = bottomLeft;
      dst[3] = bottomLeft;
      dst[4] = topRight;
      dst[5] = bottomRight;
    }

    //! 4 vertices (TL, TR, BR, BL) and 6 indices (0,1,3)(3,1,2), clockwise on screen. first is the index of the first vertex.
    void WriteIndexed(const MarkerQuad& quad, const std::span<Vertex, 4> dstVertices, const std::span<uint32_t, 6> dstIndices,
                      const uint32_t first) noexcept
    {
      const uint8_t luma = quad.Dark ? 0u : 255u;
      dstVertices[0] = Vertex{quad.Rect.Left(), quad.Rect.Top(), luma};
      dstVertices[1] = Vertex{quad.Rect.Right(), quad.Rect.Top(), luma};
      dstVertices[2] = Vertex{quad.Rect.Right(), quad.Rect.Bottom(), luma};
      dstVertices[3] = Vertex{quad.Rect.Left(), quad.Rect.Bottom(), luma};
      dstIndices[0] = first + 0u;
      dstIndices[1] = first + 1u;
      dstIndices[2] = first + 3u;
      dstIndices[3] = first + 3u;
      dstIndices[4] = first + 1u;
      dstIndices[5] = first + 2u;
    }

    std::size_t BuildQuads(const ModuleMatrix& matrix, const Options& options, const Point origin, const std::span<MarkerQuad> dst) noexcept
    {
      std::size_t count = 0;
      const bool complete = WalkQuads(matrix, options, origin,
                                      [&](const MarkerQuad& quad) noexcept
                                      {
                                        if (count >= dst.size())
                                        {
                                          return false;
                                        }
                                        dst[count++] = quad;
                                        return true;
                                      });
      return complete ? count : 0;
    }

    std::size_t BuildTriangles(const ModuleMatrix& matrix, const Options& options, const Point origin, const std::span<Vertex> dst) noexcept
    {
      std::size_t count = 0;
      const bool complete = WalkQuads(matrix, options, origin,
                                      [&](const MarkerQuad& quad) noexcept
                                      {
                                        if (dst.size() - count < 6u)
                                        {
                                          return false;
                                        }
                                        WriteTriangles(quad, dst.subspan(count).first<6>());
                                        count += 6u;
                                        return true;
                                      });
      return complete ? count : 0;
    }

    IndexedCount BuildIndexed(const ModuleMatrix& matrix, const Options& options, const Point origin, const std::span<Vertex> dstVertices,
                              const std::span<uint32_t> dstIndices, const uint32_t baseVertex) noexcept
    {
      IndexedCount count;
      const bool complete =
        WalkQuads(matrix, options, origin,
                  [&](const MarkerQuad& quad) noexcept
                  {
                    if (dstVertices.size() - count.VertexCount < 4u || dstIndices.size() - count.IndexCount < 6u)
                    {
                      return false;
                    }
                    WriteIndexed(quad, dstVertices.subspan(count.VertexCount).first<4>(), dstIndices.subspan(count.IndexCount).first<6>(),
                                 baseVertex + static_cast<uint32_t>(count.VertexCount));
                    count.VertexCount += 4u;
                    count.IndexCount += 6u;
                    return true;
                  });
      return complete ? count : IndexedCount{};
    }

    //! Fill a quad, clipped to the buffer, with its luma in every colour channel (alpha 255).
    void FillQuad(const MarkerQuad& quad, const std::span<uint8_t> dst, const int32_t width, const int32_t height, const std::size_t bytesPerPixel,
                  const std::size_t stride) noexcept
    {
      const int32_t left = std::max(quad.Rect.Left(), 0);
      const int32_t right = std::min(quad.Rect.Right(), width);
      const int32_t top = std::max(quad.Rect.Top(), 0);
      const int32_t bottom = std::min(quad.Rect.Bottom(), height);
      if (left >= right || top >= bottom)
      {
        return;
      }
      const uint8_t luma = quad.Dark ? 0u : 255u;
      const auto columns = static_cast<std::size_t>(right - left);
      for (int32_t y = top; y < bottom; ++y)
      {
        const std::span<uint8_t> row =
          dst.subspan((static_cast<std::size_t>(y) * stride) + (static_cast<std::size_t>(left) * bytesPerPixel), columns * bytesPerPixel);
        if (bytesPerPixel == 4u)
        {
          for (std::size_t x = 0; x < row.size(); x += 4u)
          {
            row[x] = luma;
            row[x + 1u] = luma;
            row[x + 2u] = luma;
            row[x + 3u] = 255u;
          }
        }
        else
        {
          std::fill(row.begin(), row.end(), luma);
        }
      }
    }
  }

  std::size_t EncodePayload(const Payload& payload, const StartMetadata& metadata, const std::span<uint8_t> dst) noexcept
  {
    const bool isStart = payload.Kind() == MarkerKind::SequenceStart;
    const std::size_t byteCount = isStart                              ? WireFormat::StartPayloadByteCount
                                  : payload.Kind() == MarkerKind::Sync ? WireFormat::SyncPayloadByteCount
                                                                       : WireFormat::PayloadByteCount;
    if (static_cast<uint8_t>(payload.Kind()) > WireFormat::MaxMarkerKindValue || dst.size() < byteCount)
    {
      return 0;
    }

    // A sync marker is the start of the header: magic, format version, kind, run id and frame index
    const std::array<uint8_t, WireFormat::PayloadByteCount> header = EncodeHeader(payload);
    std::copy_n(header.begin(), std::min(byteCount, WireFormat::PayloadByteCount), dst.begin());
    if (isStart)
    {
      ByteSpanUtil::WriteLE(dst, WireFormat::OffsetStartUtcTicks, metadata.UtcTicks);
      std::copy_n(metadata.Id.Bytes.begin(), SequenceId::ByteCount, dst.subspan(WireFormat::OffsetSequenceId).begin());
    }
    return byteCount;
  }

  bool TryDecodePayload(const std::span<const uint8_t> bytes, Payload& rPayload, StartMetadata* const pMetadata) noexcept
  {
    if (bytes.size() < WireFormat::SyncPayloadByteCount || bytes[WireFormat::OffsetMagic0] != WireFormat::PayloadMagic0 ||
        bytes[WireFormat::OffsetMagic1] != WireFormat::PayloadMagic1 || bytes[WireFormat::OffsetVersion] != WireFormat::PayloadFormatVersion ||
        bytes[WireFormat::OffsetKind] > WireFormat::MaxMarkerKindValue)
    {
      return false;
    }

    const auto kind = static_cast<MarkerKind>(bytes[WireFormat::OffsetKind]);
    if (kind == MarkerKind::Sync)
    {
      if (bytes.size() != WireFormat::SyncPayloadByteCount)
      {
        return false;
      }
      rPayload = Payload{kind, ByteSpanUtil::ReadLE<uint32_t>(bytes, WireFormat::OffsetRunId),
                         ByteSpanUtil::ReadLE<uint64_t>(bytes, WireFormat::OffsetFrameIndex), MarkerFlags::None, TimeSpan()};
      if (pMetadata != nullptr)
      {
        *pMetadata = StartMetadata{};
      }
      return true;
    }
    if (bytes.size() < WireFormat::PayloadByteCount)
    {
      return false;
    }
    StartMetadata metadata;
    if (kind == MarkerKind::SequenceStart)
    {
      if (bytes.size() != WireFormat::StartPayloadByteCount)
      {
        return false;
      }
      metadata.UtcTicks = ByteSpanUtil::ReadLE<int64_t>(bytes, WireFormat::OffsetStartUtcTicks);
      std::copy_n(bytes.subspan(WireFormat::OffsetSequenceId).begin(), SequenceId::ByteCount, metadata.Id.Bytes.begin());
    }
    else if (bytes.size() != WireFormat::PayloadByteCount)
    {
      return false;
    }

    // Every flags value is accepted: bits without a name are reserved and kept
    rPayload = Payload{kind,
                       ByteSpanUtil::ReadLE<uint32_t>(bytes, WireFormat::OffsetRunId),
                       ByteSpanUtil::ReadLE<uint64_t>(bytes, WireFormat::OffsetFrameIndex),
                       static_cast<MarkerFlags>(bytes[WireFormat::OffsetFlags]),
                       TimeSpan(ByteSpanUtil::ReadLE<int64_t>(bytes, WireFormat::OffsetAnimationTicks)),
                       TimeSpan32(ByteSpanUtil::ReadLE<uint32_t>(bytes, WireFormat::OffsetPreferredFrameTicks)),
                       TimeSpan32(ByteSpanUtil::ReadLE<uint32_t>(bytes, WireFormat::OffsetTargetFrameTicks)),
                       TickCount64::FromUnsignedTicks(ByteSpanUtil::ReadLE<uint64_t>(bytes, WireFormat::OffsetIntendedDisplayTicks)),
                       TickCount64::FromUnsignedTicks(ByteSpanUtil::ReadLE<uint64_t>(bytes, WireFormat::OffsetCpuStartTicks)),
                       TimeSpan32(ByteSpanUtil::ReadLE<uint32_t>(bytes, WireFormat::OffsetCpuBusyTicks))};
    if (pMetadata != nullptr)
    {
      *pMetadata = metadata;
    }
    return true;
  }

  bool GenerateModules(const Payload& payload, ModuleMatrix& rMatrix, const StartMetadata& metadata) noexcept
  {
    std::array<uint8_t, Payload::MaxEncodedByteCount> data{};
    const std::size_t byteCount = EncodePayload(payload, metadata, data);
    if (byteCount == 0)
    {
      return false;
    }

    // Every kind is pinned to one version, so the symbol never changes size between frames.
    const int32_t version = payload.Kind() == MarkerKind::Sync ? WireFormat::SyncQrVersion : WireFormat::QrVersion;
    // Cannot fail: every kind's bytes fit its version (WireFormat::QrCapacityBytes, WireFormat::SyncQrCapacityBytes)
    QrEncoder::QrSymbol symbol;
    [[maybe_unused]] const bool encoded = QrEncoder::Encode(std::span<const uint8_t>(data).first(byteCount), version, symbol);
    assert(encoded);

    std::array<uint8_t, ModuleMatrix::MaxPackedModuleByteCount> bits{};
    QrEncoder::PackModules(symbol, bits);
    return ModuleMatrix::TryFromBits(symbol.Size, bits, rMatrix);
  }

  std::size_t ModulesToQuads(const ModuleMatrix& matrix, const Options& options, const Point origin, const std::span<MarkerQuad> dst) noexcept
  {
    return matrix.Size() > 0 ? BuildQuads(matrix, options, origin, dst) : 0u;
  }

  std::size_t ModulesToTriangles(const ModuleMatrix& matrix, const Options& options, const Point origin, const std::span<Vertex> dst) noexcept
  {
    return matrix.Size() > 0 ? BuildTriangles(matrix, options, origin, dst) : 0u;
  }

  IndexedCount ModulesToIndexed(const ModuleMatrix& matrix, const Options& options, const Point origin, const std::span<Vertex> dstVertices,
                                const std::span<uint32_t> dstIndices, const uint32_t baseVertex) noexcept
  {
    return matrix.Size() > 0 ? BuildIndexed(matrix, options, origin, dstVertices, dstIndices, baseVertex) : IndexedCount{};
  }

  std::size_t GridVertices(const MarkerKind kind, const Options& options, const Point origin, const std::span<Vertex> dst) noexcept
  {
    const std::size_t count = GridVertexCount(kind);
    if (dst.size() < count)
    {
      return 0;
    }
    const int32_t modules = ModuleMatrix::SizeFor(kind);
    const int32_t moduleSize = options.ModuleSizePx();
    const int32_t markerSize = options.MarkerSizePx(kind);
    dst[0] = Vertex{origin.X, origin.Y, 255u};
    dst[1] = Vertex{origin.X + markerSize, origin.Y, 255u};
    dst[2] = Vertex{origin.X + markerSize, origin.Y + markerSize, 255u};
    dst[3] = Vertex{origin.X, origin.Y + markerSize, 255u};
    const int32_t symbolLeft = origin.X + options.QuietZonePx();
    const int32_t symbolTop = origin.Y + options.QuietZonePx();
    std::size_t index = 4;
    for (int32_t row = 0; row <= modules; ++row)
    {
      for (int32_t column = 0; column <= modules; ++column)
      {
        dst[index++] = Vertex{symbolLeft + (column * moduleSize), symbolTop + (row * moduleSize), 0u};
      }
    }
    return count;
  }

  std::size_t ModulesToGridIndices(const ModuleMatrix& matrix, const std::span<uint32_t> dst, const uint32_t baseVertex) noexcept
  {
    if (matrix.Size() == 0 || dst.size() < 6u)
    {
      return 0;
    }
    const std::array<uint32_t, 6> background{0u, 1u, 3u, 3u, 1u, 2u};
    for (std::size_t i = 0; i < background.size(); ++i)
    {
      dst[i] = baseVertex + background[i];
    }
    std::size_t count = 6;
    const auto corners = static_cast<uint32_t>(matrix.Size()) + 1u;
    const bool complete = WalkRuns(matrix,
                                   [&](const int32_t row, const int32_t first, const int32_t end) noexcept
                                   {
                                     if (dst.size() - count < 6u)
                                     {
                                       return false;
                                     }
                                     const uint32_t top = baseVertex + 4u + (static_cast<uint32_t>(row) * corners);
                                     const uint32_t topLeft = top + static_cast<uint32_t>(first);
                                     const uint32_t topRight = top + static_cast<uint32_t>(end);
                                     const uint32_t bottomRight = topRight + corners;
                                     const uint32_t bottomLeft = topLeft + corners;
                                     dst[count++] = topLeft;
                                     dst[count++] = topRight;
                                     dst[count++] = bottomLeft;
                                     dst[count++] = bottomLeft;
                                     dst[count++] = topRight;
                                     dst[count++] = bottomRight;
                                     return true;
                                   });
    return complete ? count : 0u;
  }

  bool ModulesToBitmap(const ModuleMatrix& matrix, const Options& options, const Point origin, const std::span<uint8_t> dst, const int32_t width,
                       const int32_t height, const PixelFormat format, const std::size_t stride) noexcept
  {
    if (matrix.Size() == 0 || width < 0 || height < 0)
    {
      return false;
    }
    const auto bytesPerPixel = static_cast<std::size_t>(PixelFormatUtil::BytesPerPixel(format));
    const std::size_t rowBytes = static_cast<std::size_t>(width) * bytesPerPixel;
    const std::size_t rowStride = stride == 0 ? rowBytes : stride;
    if (rowStride < rowBytes)
    {
      return false;
    }
    if (height > 0)
    {
      // The last row needs no stride after it. dst.size() >= rowBytes is checked first, so the subtraction cannot wrap, and dividing
      // instead of multiplying keeps a huge stride from wrapping the bytes needed into a small number.
      const auto steps = static_cast<std::size_t>(height - 1);
      if (dst.size() < rowBytes || (steps > 0 && rowStride > (dst.size() - rowBytes) / steps))
      {
        return false;
      }
    }
    return WalkQuads(matrix, options, origin,
                     [&](const MarkerQuad& quad) noexcept
                     {
                       FillQuad(quad, dst, width, height, bytesPerPixel, rowStride);
                       return true;
                     });
  }

}
