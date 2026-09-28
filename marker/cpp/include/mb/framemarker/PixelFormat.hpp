#ifndef MB_FRAMEMARKER_PIXELFORMAT_HPP
#define MB_FRAMEMARKER_PIXELFORMAT_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FrameMarker
{
  //! The pixels ModulesToBitmap writes. Each pixel is a run of bytes in memory order (bytes, not a packed integer, so the layout does not
  //! depend on the CPU's endianness); pixels follow each other left to right, rows are the stride apart, top row first. Every colour
  //! channel holds the same value: 0 for a dark module, 255 for a light one.
  //!
  //!   Gray8   1 byte:  [L]
  //!   Rgb24   3 bytes: [R, G, B]          (R = G = B = L)
  //!   Rgba32  4 bytes: [R, G, B, A]       (R = G = B = L, A = 255)
  //!
  //! Because R, G and B are equal, a BGR24 or BGRA32 buffer gets exactly the same bytes: use Rgb24 or Rgba32 for them. An ARGB buffer
  //! (alpha first) is not supported.
  enum class PixelFormat : uint8_t
  {
    //! 1 byte per pixel: [L].
    Gray8 = 0,
    //! 3 bytes per pixel: [R, G, B].
    Rgb24 = 1,
    //! 4 bytes per pixel: [R, G, B, A], A always 255.
    Rgba32 = 2,
  };
}

#endif
