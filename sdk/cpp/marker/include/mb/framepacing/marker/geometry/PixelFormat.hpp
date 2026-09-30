#ifndef MB_FRAMEPACING_MARKER_GEOMETRY_PIXELFORMAT_HPP
#define MB_FRAMEPACING_MARKER_GEOMETRY_PIXELFORMAT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Marker
{
  //! The pixels ModulesToBitmap writes. Each pixel is a run of bytes in memory order (bytes, not a packed integer, so the layout does not
  //! depend on the CPU's endianness); pixels follow each other left to right, rows are the stride apart, top row first. Every colour
  //! channel holds the same value: 0 for a dark module, 255 for a light one.
  //!
  //!   R8        1 byte:  [L]
  //!   R8G8B8    3 bytes: [R, G, B]          (R = G = B = L)
  //!   R8G8B8A8  4 bytes: [R, G, B, A]       (R = G = B = L, A = 255)
  //!
  //! Because R, G and B are equal, a B8G8R8 or B8G8R8A8 buffer gets exactly the same bytes: use R8G8B8 or R8G8B8A8 for them. An ARGB buffer
  //! (alpha first) is not supported.
  enum class PixelFormat : uint8_t
  {
    //! 1 byte per pixel: [L].
    R8 = 0,
    //! 3 bytes per pixel: [R, G, B].
    R8G8B8 = 1,
    //! 4 bytes per pixel: [R, G, B, A], A always 255.
    R8G8B8A8 = 2,
  };
}

#endif
