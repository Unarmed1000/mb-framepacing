//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The pixels Marker.ModulesToBitmap writes. Each pixel is a run of bytes in memory order (bytes, not a packed integer, so the layout does
//* not depend on the CPU's endianness); pixels follow each other left to right, rows are the stride apart, top row first. Every colour
//* channel holds the same value: 0 for a dark module, 255 for a light one.
//*
//*   Gray8   1 byte:  [L]
//*   Rgb24   3 bytes: [R, G, B]          (R = G = B = L)
//*   Rgba32  4 bytes: [R, G, B, A]       (R = G = B = L, A = 255)
//*
//* Because R, G and B are equal, a BGR24 or BGRA32 buffer (Unity's BGRA32, System.Drawing's Format32bppArgb in memory) gets exactly the same
//* bytes: use Rgb24 or Rgba32 for them. A buffer with alpha first in memory (ARGB) is not supported.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FrameMarker
{
  public enum PixelFormat : byte
  {
    /// <summary>1 byte per pixel: [L].</summary>
    Gray8 = 0,

    /// <summary>3 bytes per pixel: [R, G, B], all equal.</summary>
    Rgb24 = 1,

    /// <summary>4 bytes per pixel: [R, G, B, A], R, G and B equal, A always 255.</summary>
    Rgba32 = 2,
  }
}
