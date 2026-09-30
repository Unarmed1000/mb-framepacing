#ifndef MB_FRAMEPACING_MARKER_GEOMETRY_PIXELFORMATUTIL_HPP
#define MB_FRAMEPACING_MARKER_GEOMETRY_PIXELFORMATUTIL_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/marker/geometry/PixelFormat.hpp>
#include <cstdint>

namespace MB::FramePacing::Marker::PixelFormatUtil
{
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
}

#endif
