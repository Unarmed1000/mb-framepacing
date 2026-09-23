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

    std::size_t BuildQuads(const ModuleMatrix& matrix, const Options& options, const Point origin, const std::span<Quad> dst) noexcept
    {
      if (dst.empty())
      {
        return 0;
      }

      const int32_t moduleSize = options.ModuleSizePx;
      const int32_t markerSize = MarkerSizePx(options, matrix.Size);
      const int32_t symbolLeft = origin.X + (options.QuietZoneModules * moduleSize);
      const int32_t symbolTop = origin.Y + (options.QuietZoneModules * moduleSize);

      std::size_t count = 0;
      dst[count++] = Quad{origin.X, origin.Y, origin.X + markerSize, origin.Y + markerSize, false};

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
          if (count >= dst.size())
          {
            return 0;
          }
          dst[count++] = Quad{symbolLeft + (runStart * moduleSize), top, symbolLeft + (x * moduleSize), top + moduleSize, true};
        }
      }
      return count;
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
    if (!IsValid(options) || !GenerateModules(payload, matrix))
    {
      return 0;
    }
    return BuildQuads(matrix, options, origin, dst);
  }

  std::size_t GenerateStartQuads(const Payload& payload, const StartMetadata& metadata, const Options& options, const Point origin,
                                 const std::span<Quad> dst) noexcept
  {
    Payload startPayload = payload;
    startPayload.Kind = MarkerKind::SequenceStart;
    ModuleMatrix matrix;
    if (!IsValid(options) || !GenerateModules(startPayload, matrix, metadata))
    {
      return 0;
    }
    return BuildQuads(matrix, options, origin, dst);
  }

  std::size_t QuadsToTriangles(const std::span<const Quad> quads, const std::span<Vertex> dst) noexcept
  {
    const std::size_t required = quads.size() * 6u;
    if (dst.size() < required)
    {
      return 0;
    }

    std::size_t i = 0;
    for (const Quad& quad : quads)
    {
      const uint8_t luma = quad.Dark ? 0u : 255u;
      const Vertex topLeft{quad.Left, quad.Top, luma};
      const Vertex topRight{quad.Right, quad.Top, luma};
      const Vertex bottomRight{quad.Right, quad.Bottom, luma};
      const Vertex bottomLeft{quad.Left, quad.Bottom, luma};
      dst[i++] = topLeft;
      dst[i++] = topRight;
      dst[i++] = bottomLeft;
      dst[i++] = bottomLeft;
      dst[i++] = topRight;
      dst[i++] = bottomRight;
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

    std::size_t v = 0;
    std::size_t i = 0;
    for (const Quad& quad : quads)
    {
      const uint8_t luma = quad.Dark ? 0u : 255u;
      const auto first = static_cast<uint32_t>(baseVertex + v);
      dstVertices[v++] = Vertex{quad.Left, quad.Top, luma};
      dstVertices[v++] = Vertex{quad.Right, quad.Top, luma};
      dstVertices[v++] = Vertex{quad.Right, quad.Bottom, luma};
      dstVertices[v++] = Vertex{quad.Left, quad.Bottom, luma};
      dstIndices[i++] = first + 0u;
      dstIndices[i++] = first + 1u;
      dstIndices[i++] = first + 3u;
      dstIndices[i++] = first + 3u;
      dstIndices[i++] = first + 1u;
      dstIndices[i++] = first + 2u;
    }
    return {requiredVertices, requiredIndices};
  }
}
