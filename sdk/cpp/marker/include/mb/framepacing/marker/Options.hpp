#ifndef MB_FRAMEPACING_MARKER_OPTIONS_HPP
#define MB_FRAMEPACING_MARKER_OPTIONS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/marker/MarkerKind.hpp>
#include <algorithm>
#include <cassert>
#include <cstdint>

namespace MB::FramePacing::Marker
{
  //! How large the marker is drawn: the size of one QR module in source pixels and the white border around the symbol in modules. Always
  //! valid: the module size is within [MinModuleSizePx, MaxModuleSizePx] and the quiet zone within [0, MaxQuietZoneModules]. The
  //! constructor asserts that; without asserts it clamps a value outside into its range.
  class Options
  {
    int32_t m_moduleSizePx{DefaultModuleSizePx};
    int32_t m_quietZoneModules{RecommendedQuietZoneModules};

  public:
    //! The module size of default Options.
    static constexpr int32_t DefaultModuleSizePx = 6;
    static constexpr int32_t MinModuleSizePx = 1;
    static constexpr int32_t MaxModuleSizePx = 1024;
    static constexpr int32_t MaxQuietZoneModules = 16;
    //! The QR specification asks for a quiet zone of 4 modules.
    static constexpr int32_t RecommendedQuietZoneModules = 4;
    //! Recommended distance in source pixels between the marker and the edge of the frame.
    static constexpr int32_t RecommendedInsetPx = 32;

    //! DefaultModuleSizePx pixel modules and the recommended quiet zone.
    constexpr Options() noexcept = default;

    //! See doc/marker-format.md "Sizing", or Recommended for a capture's scaling. The QR specification asks for a quiet zone of 4 modules.
    constexpr explicit Options(const int32_t moduleSizePx, const int32_t quietZoneModules = RecommendedQuietZoneModules) noexcept
      : m_moduleSizePx(std::clamp(moduleSizePx, MinModuleSizePx, MaxModuleSizePx))
      , m_quietZoneModules(std::clamp(quietZoneModules, 0, MaxQuietZoneModules))
    {
      assert(moduleSizePx >= MinModuleSizePx && moduleSizePx <= MaxModuleSizePx);
      assert(quietZoneModules >= 0 && quietZoneModules <= MaxQuietZoneModules);
    }

    //! The recommended options for a capture that stores the sourceHeight pixel high output storedHeight pixels high: 3 stored pixels per
    //! module (4 when the capture card delivers MJPEG) after all scaling (source -> capture -> stored), and the recommended quiet zone.
    //! A height of 0 or less means no scaling.
    static constexpr Options Recommended(const int32_t sourceHeight, const int32_t storedHeight, const bool mjpeg = false) noexcept
    {
      return Options(ModuleSizeForStoredPx(mjpeg ? 4 : 3, sourceHeight, storedHeight));
    }

    //! The smallest module size that still decodes: 2 stored pixels per module after all scaling, and the recommended quiet zone.
    static constexpr Options Minimum(const int32_t sourceHeight, const int32_t storedHeight) noexcept
    {
      return Options(ModuleSizeForStoredPx(2, sourceHeight, storedHeight));
    }

    [[nodiscard]] constexpr int32_t ModuleSizePx() const noexcept
    {
      return m_moduleSizePx;
    }

    [[nodiscard]] constexpr int32_t QuietZoneModules() const noexcept
    {
      return m_quietZoneModules;
    }

    //! The quiet zone in source pixels: the offset from the marker's origin to its symbol.
    [[nodiscard]] constexpr int32_t QuietZonePx() const noexcept
    {
      return m_quietZoneModules * m_moduleSizePx;
    }

    //! Width and height in source pixels of a marker (symbol + quiet zone). Frame, start and end markers have one size, the sync marker is
    //! smaller.
    [[nodiscard]] constexpr int32_t MarkerSizePx(const MarkerKind kind = MarkerKind::Frame) const noexcept
    {
      return (QrModuleCountFor(kind) + (2 * m_quietZoneModules)) * m_moduleSizePx;
    }

    //! Recommended origin of a marker in a sourceWidth x sourceHeight output: the main marker (frame, start and end) top-left, the sync
    //! marker bottom-left, RecommendedInsetPx from the edges. alignPx should be the capture's integer downscale ratio (1 if none) so module
    //! edges land on stored pixel edges.
    [[nodiscard]] constexpr Point RecommendedOrigin(const MarkerKind kind, const int32_t sourceWidth, const int32_t sourceHeight,
                                                    const int32_t alignPx = 1) const noexcept
    {
      (void)sourceWidth;
      const int32_t inset = AlignUp(RecommendedInsetPx, alignPx);
      if (kind == MarkerKind::Sync)
      {
        return {inset, AlignDown(sourceHeight - inset - MarkerSizePx(kind), alignPx)};
      }
      return {inset, inset};
    }

    constexpr bool operator==(const Options&) const noexcept = default;

  private:
    static constexpr int32_t CeilDiv(const int64_t numerator, const int64_t denominator) noexcept
    {
      return static_cast<int32_t>((numerator + denominator - 1) / denominator);
    }

    static constexpr int32_t AlignDown(const int32_t value, const int32_t alignment) noexcept
    {
      return alignment <= 1 ? value : (value / alignment) * alignment;
    }

    static constexpr int32_t AlignUp(const int32_t value, const int32_t alignment) noexcept
    {
      return alignment <= 1 ? value : CeilDiv(value, alignment) * alignment;
    }

    //! The module size that gives storedPxPerModule stored pixels per module, ceil(storedPxPerModule x sourceHeight / storedHeight),
    //! within the valid module sizes.
    static constexpr int32_t ModuleSizeForStoredPx(const int32_t storedPxPerModule, const int32_t sourceHeight, const int32_t storedHeight) noexcept
    {
      if (sourceHeight <= 0 || storedHeight <= 0)
      {
        return storedPxPerModule;
      }
      const int64_t size = (((static_cast<int64_t>(storedPxPerModule) * sourceHeight) + storedHeight) - 1) / storedHeight;
      return static_cast<int32_t>(std::clamp(size, int64_t{storedPxPerModule}, int64_t{MaxModuleSizePx}));
    }
  };
}

#endif
