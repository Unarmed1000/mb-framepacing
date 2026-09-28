// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framemarker/FrameMarker.hpp>
#include <algorithm>
#include <array>
#include <bit>
#include "qrcodegen.h"

namespace MB::FrameMarker
{
  namespace
  {
    constexpr std::size_t QrBufferLength = qrcodegen_BUFFER_LEN_FOR_VERSION(QrVersion);

    static_assert(MaxEncodedPayloadByteCount <= QrBufferLength);
    static_assert(QrModuleCount == 41);
    static_assert(MaxQuadCount() == 862u);
    static_assert(MaxTriangleVertexCount() == 862u * 6u);

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
    bool WalkRuns(const ModuleMatrix& matrix, TEmit&& emit) noexcept
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
    bool WalkQuads(const ModuleMatrix& matrix, const Options& options, const Point origin, TEmit&& emit) noexcept
    {
      const int32_t moduleSize = options.ModuleSizePx;
      const int32_t markerSize = (matrix.Size() + (2 * options.QuietZoneModules)) * options.ModuleSizePx;
      const int32_t symbolLeft = origin.X + (options.QuietZoneModules * moduleSize);
      const int32_t symbolTop = origin.Y + (options.QuietZoneModules * moduleSize);

      if (!emit(Quad{origin.X, origin.Y, origin.X + markerSize, origin.Y + markerSize, false}))
      {
        return false;
      }
      return WalkRuns(matrix,
                      [&](const int32_t row, const int32_t first, const int32_t end) noexcept
                      {
                        const int32_t top = symbolTop + (row * moduleSize);
                        return emit(Quad{symbolLeft + (first * moduleSize), top, symbolLeft + (end * moduleSize), top + moduleSize, true});
                      });
    }

    //! 6 vertices: (TL, TR, BL) (BL, TR, BR), clockwise on screen (+y down).
    void WriteTriangles(const Quad& quad, const std::span<Vertex, 6> dst) noexcept
    {
      const uint8_t luma = quad.Dark ? 0u : 255u;
      const Vertex topLeft{quad.Left, quad.Top, luma};
      const Vertex topRight{quad.Right, quad.Top, luma};
      const Vertex bottomRight{quad.Right, quad.Bottom, luma};
      const Vertex bottomLeft{quad.Left, quad.Bottom, luma};
      dst[0] = topLeft;
      dst[1] = topRight;
      dst[2] = bottomLeft;
      dst[3] = bottomLeft;
      dst[4] = topRight;
      dst[5] = bottomRight;
    }

    //! 4 vertices (TL, TR, BR, BL) and 6 indices (0,1,3)(3,1,2), clockwise on screen. first is the index of the first vertex.
    void WriteIndexed(const Quad& quad, const std::span<Vertex, 4> dstVertices, const std::span<uint32_t, 6> dstIndices,
                      const uint32_t first) noexcept
    {
      const uint8_t luma = quad.Dark ? 0u : 255u;
      dstVertices[0] = Vertex{quad.Left, quad.Top, luma};
      dstVertices[1] = Vertex{quad.Right, quad.Top, luma};
      dstVertices[2] = Vertex{quad.Right, quad.Bottom, luma};
      dstVertices[3] = Vertex{quad.Left, quad.Bottom, luma};
      dstIndices[0] = first + 0u;
      dstIndices[1] = first + 1u;
      dstIndices[2] = first + 3u;
      dstIndices[3] = first + 3u;
      dstIndices[4] = first + 1u;
      dstIndices[5] = first + 2u;
    }

    std::size_t BuildQuads(const ModuleMatrix& matrix, const Options& options, const Point origin, const std::span<Quad> dst) noexcept
    {
      std::size_t count = 0;
      const bool complete = WalkQuads(matrix, options, origin,
                                      [&](const Quad& quad) noexcept
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
                                      [&](const Quad& quad) noexcept
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
                  [&](const Quad& quad) noexcept
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
    void FillQuad(const Quad& quad, const std::span<uint8_t> dst, const int32_t width, const int32_t height, const std::size_t bytesPerPixel,
                  const std::size_t stride) noexcept
    {
      const int32_t left = std::max(quad.Left, 0);
      const int32_t right = std::min(quad.Right, width);
      const int32_t top = std::max(quad.Top, 0);
      const int32_t bottom = std::min(quad.Bottom, height);
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

  bool GenerateModules(const Payload& payload, ModuleMatrix& rMatrix, const StartMetadata& metadata) noexcept
  {
    std::array<uint8_t, QrBufferLength> dataAndTemp{};
    std::array<uint8_t, QrBufferLength> qrCode{};
    const std::size_t byteCount = EncodePayload(payload, metadata, dataAndTemp);
    if (byteCount == 0)
    {
      return false;
    }

    // Every kind is pinned to one version, so the symbol never changes size between frames.
    const int32_t version = payload.Kind == MarkerKind::Sync ? SyncQrVersion : QrVersion;
    if (!qrcodegen_encodeBinary(dataAndTemp.data(), byteCount, qrCode.data(), qrcodegen_Ecc_MEDIUM, version, version, qrcodegen_Mask_AUTO, false))
    {
      return false;
    }

    // Pack the symbol: row-major, most significant bit first, continuous across rows
    const int32_t size = qrcodegen_getSize(qrCode.data());
    std::array<uint8_t, MaxPackedModuleByteCount> bits{};
    std::size_t index = 0;
    for (int32_t y = 0; y < size; ++y)
    {
      for (int32_t x = 0; x < size; ++x, ++index)
      {
        if (qrcodegen_getModule(qrCode.data(), x, y))
        {
          bits[index / 8u] = static_cast<uint8_t>(bits[index / 8u] | (0x80u >> (index % 8u)));
        }
      }
    }
    return ModuleMatrix::TryFromBits(size, bits, rMatrix);
  }

  std::size_t ModulesToQuads(const ModuleMatrix& matrix, const Options& options, const Point origin, const std::span<Quad> dst) noexcept
  {
    return IsValid(options) && matrix.Size() > 0 ? BuildQuads(matrix, options, origin, dst) : 0u;
  }

  std::size_t ModulesToTriangles(const ModuleMatrix& matrix, const Options& options, const Point origin, const std::span<Vertex> dst) noexcept
  {
    return IsValid(options) && matrix.Size() > 0 ? BuildTriangles(matrix, options, origin, dst) : 0u;
  }

  IndexedCount ModulesToIndexed(const ModuleMatrix& matrix, const Options& options, const Point origin, const std::span<Vertex> dstVertices,
                                const std::span<uint32_t> dstIndices, const uint32_t baseVertex) noexcept
  {
    return IsValid(options) && matrix.Size() > 0 ? BuildIndexed(matrix, options, origin, dstVertices, dstIndices, baseVertex) : IndexedCount{};
  }

  std::size_t GridVertices(const MarkerKind kind, const Options& options, const Point origin, const std::span<Vertex> dst) noexcept
  {
    const std::size_t count = GridVertexCount(kind);
    if (!IsValid(options) || dst.size() < count)
    {
      return 0;
    }
    const int32_t modules = QrModuleCountFor(kind);
    const int32_t moduleSize = options.ModuleSizePx;
    const int32_t markerSize = MarkerSizePx(options, kind);
    dst[0] = Vertex{origin.X, origin.Y, 255u};
    dst[1] = Vertex{origin.X + markerSize, origin.Y, 255u};
    dst[2] = Vertex{origin.X + markerSize, origin.Y + markerSize, 255u};
    dst[3] = Vertex{origin.X, origin.Y + markerSize, 255u};
    const int32_t symbolLeft = origin.X + (options.QuietZoneModules * moduleSize);
    const int32_t symbolTop = origin.Y + (options.QuietZoneModules * moduleSize);
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
    if (!IsValid(options) || matrix.Size() == 0 || width < 0 || height < 0)
    {
      return false;
    }
    const auto bytesPerPixel = static_cast<std::size_t>(BytesPerPixel(format));
    const std::size_t rowBytes = static_cast<std::size_t>(width) * bytesPerPixel;
    const std::size_t rowStride = stride == 0 ? rowBytes : stride;
    const std::size_t required = height == 0 ? 0u : (rowStride * static_cast<std::size_t>(height - 1)) + rowBytes;
    if (rowStride < rowBytes || dst.size() < required)
    {
      return false;
    }
    return WalkQuads(matrix, options, origin,
                     [&](const Quad& quad) noexcept
                     {
                       FillQuad(quad, dst, width, height, bytesPerPixel, rowStride);
                       return true;
                     });
  }

}
