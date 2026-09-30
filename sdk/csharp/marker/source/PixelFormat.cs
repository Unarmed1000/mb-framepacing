//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The pixels FrameMarker.ModulesToBitmap writes. Each pixel is a run of bytes in memory order (bytes, not a packed integer, so the layout does
//* not depend on the CPU's endianness); pixels follow each other left to right, rows are the stride apart, top row first. Every colour
//* channel holds the same value: 0 for a dark module, 255 for a light one.
//*
//*   R8        1 byte:  [L]
//*   R8G8B8    3 bytes: [R, G, B]          (R = G = B = L)
//*   R8G8B8A8  4 bytes: [R, G, B, A]       (R = G = B = L, A = 255)
//*
//* Because R, G and B are equal, a B8G8R8 or B8G8R8A8 buffer (Unity's TextureFormat.BGRA32, System.Drawing's Format32bppArgb in memory) gets
//* exactly the same bytes: use R8G8B8 or R8G8B8A8 for them. A buffer with alpha first in memory (ARGB) is not supported.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Marker
{
  public enum PixelFormat : byte
  {
    /// <summary>1 byte per pixel: [L].</summary>
    R8 = 0,

    /// <summary>3 bytes per pixel: [R, G, B], all equal.</summary>
    R8G8B8 = 1,

    /// <summary>4 bytes per pixel: [R, G, B, A], R, G and B equal, A always 255.</summary>
    R8G8B8A8 = 2,
  }
}
