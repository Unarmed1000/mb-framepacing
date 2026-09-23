// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framemarker/FrameMarker.hpp>
#include <algorithm>
#include "qrcodegen.h"

namespace MB::FrameMarker
{
  namespace
  {
    constexpr std::size_t QrBufferLength = qrcodegen_BUFFER_LEN_FOR_VERSION(MaxQrVersion);

    static_assert(MaxEncodedPayloadByteCount <= QrBufferLength);
    static_assert(MaxFrameQuadCount() == 326u);
    static_assert(MaxQuadCount() == 862u);

    static_assert(MaxFrameTriangleVertexCount() == 326u * 6u);
    static_assert(MaxFrameIndexCount() == 326u * 6u);

    //! Walk the marker in draw order: the light background (symbol + quiet zone), then one dark quad per horizontal run of dark modules.
    //! Every quad goes straight to emit, which writes it in its output format and returns false when the output is full.
    template <typename TEmit>
    bool WalkQuads(const ModuleMatrix& matrix, const Options& options, const Point origin, TEmit&& emit) noexcept
    {
      const int32_t moduleSize = options.ModuleSizePx;
      const int32_t markerSize = MarkerSizePx(options, matrix.Size);
      const int32_t symbolLeft = origin.X + (options.QuietZoneModules * moduleSize);
      const int32_t symbolTop = origin.Y + (options.QuietZoneModules * moduleSize);

      if (!emit(Quad{origin.X, origin.Y, origin.X + markerSize, origin.Y + markerSize, false}))
      {
        return false;
      }
      for (int32_t y = 0; y < matrix.Size; ++y)
      {
        const int32_t top = symbolTop + (y * moduleSize);
        int32_t x = 0;
        while (x < matrix.Size)
        {
          if (!matrix.IsDark(x, y))
          {
            ++x;
            continue;
          }
          const int32_t runStart = x;
          while (x < matrix.Size && matrix.IsDark(x, y))
          {
            ++x;
          }
          if (!emit(Quad{symbolLeft + (runStart * moduleSize), top, symbolLeft + (x * moduleSize), top + moduleSize, true}))
          {
            return false;
          }
        }
      }
      return true;
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

    //! The module matrix for a marker; forceStart makes it a start marker carrying the metadata.
    bool BuildMatrix(const Payload& payload, const StartMetadata& metadata, const bool forceStart, const Options& options,
                     ModuleMatrix& rMatrix) noexcept
    {
      if (!IsValid(options))
      {
        return false;
      }
      if (!forceStart)
      {
        return GenerateModules(payload, rMatrix);
      }
      Payload startPayload = payload;
      startPayload.Kind = MarkerKind::SequenceStart;
      return GenerateModules(startPayload, rMatrix, metadata);
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

    // Frame and end markers are pinned to one version so the symbol never changes size between frames.
    const int32_t maxVersion = payload.Kind == MarkerKind::SequenceStart ? MaxQrVersion : FrameQrVersion;
    if (!qrcodegen_encodeBinary(dataAndTemp.data(), byteCount, qrCode.data(), qrcodegen_Ecc_MEDIUM, FrameQrVersion, maxVersion, qrcodegen_Mask_AUTO,
                                false))
    {
      return false;
    }

    rMatrix.Size = qrcodegen_getSize(qrCode.data());
    rMatrix.Modules.fill(0u);
    for (int32_t y = 0; y < rMatrix.Size; ++y)
    {
      for (int32_t x = 0; x < rMatrix.Size; ++x)
      {
        rMatrix.Modules[(static_cast<std::size_t>(y) * MaxQrModuleCount) + static_cast<std::size_t>(x)] =
          qrcodegen_getModule(qrCode.data(), x, y) ? 1u : 0u;
      }
    }
    return true;
  }

  std::size_t GenerateQuads(const Payload& payload, const Options& options, const Point origin, const std::span<Quad> dst) noexcept
  {
    ModuleMatrix matrix;
    return BuildMatrix(payload, {}, false, options, matrix) ? BuildQuads(matrix, options, origin, dst) : 0u;
  }

  std::size_t GenerateStartQuads(const Payload& payload, const StartMetadata& metadata, const Options& options, const Point origin,
                                 const std::span<Quad> dst) noexcept
  {
    ModuleMatrix matrix;
    return BuildMatrix(payload, metadata, true, options, matrix) ? BuildQuads(matrix, options, origin, dst) : 0u;
  }

  std::size_t GenerateTriangles(const Payload& payload, const Options& options, const Point origin, const std::span<Vertex> dst) noexcept
  {
    ModuleMatrix matrix;
    return BuildMatrix(payload, {}, false, options, matrix) ? BuildTriangles(matrix, options, origin, dst) : 0u;
  }

  std::size_t GenerateStartTriangles(const Payload& payload, const StartMetadata& metadata, const Options& options, const Point origin,
                                     const std::span<Vertex> dst) noexcept
  {
    ModuleMatrix matrix;
    return BuildMatrix(payload, metadata, true, options, matrix) ? BuildTriangles(matrix, options, origin, dst) : 0u;
  }

  IndexedCount GenerateIndexed(const Payload& payload, const Options& options, const Point origin, const std::span<Vertex> dstVertices,
                               const std::span<uint32_t> dstIndices, const uint32_t baseVertex) noexcept
  {
    ModuleMatrix matrix;
    return BuildMatrix(payload, {}, false, options, matrix) ? BuildIndexed(matrix, options, origin, dstVertices, dstIndices, baseVertex)
                                                            : IndexedCount{};
  }

  IndexedCount GenerateStartIndexed(const Payload& payload, const StartMetadata& metadata, const Options& options, const Point origin,
                                    const std::span<Vertex> dstVertices, const std::span<uint32_t> dstIndices, const uint32_t baseVertex) noexcept
  {
    ModuleMatrix matrix;
    return BuildMatrix(payload, metadata, true, options, matrix) ? BuildIndexed(matrix, options, origin, dstVertices, dstIndices, baseVertex)
                                                                 : IndexedCount{};
  }

  std::size_t QuadsToTriangles(const std::span<const Quad> quads, const std::span<Vertex> dst) noexcept
  {
    const std::size_t required = quads.size() * 6u;
    if (dst.size() < required)
    {
      return 0;
    }
    for (std::size_t i = 0; i < quads.size(); ++i)
    {
      WriteTriangles(quads[i], dst.subspan(i * 6u).first<6>());
    }
    return required;
  }

  IndexedCount QuadsToIndexed(const std::span<const Quad> quads, const std::span<Vertex> dstVertices, const std::span<uint32_t> dstIndices,
                              const uint32_t baseVertex) noexcept
  {
    const std::size_t requiredVertices = quads.size() * 4u;
    const std::size_t requiredIndices = quads.size() * 6u;
    if (dstVertices.size() < requiredVertices || dstIndices.size() < requiredIndices)
    {
      return {};
    }
    for (std::size_t i = 0; i < quads.size(); ++i)
    {
      WriteIndexed(quads[i], dstVertices.subspan(i * 4u).first<4>(), dstIndices.subspan(i * 6u).first<6>(),
                   baseVertex + static_cast<uint32_t>(i * 4u));
    }
    return {requiredVertices, requiredIndices};
  }
}
