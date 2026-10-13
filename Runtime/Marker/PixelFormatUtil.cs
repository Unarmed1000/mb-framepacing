//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What follows from a PixelFormat (the C++ library's PixelFormatUtil).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Marker
{
  public static class PixelFormatUtil
  {
    /// <summary>Bytes per pixel of a <see cref="PixelFormat"/>. A value without a name counts as R8.</summary>
    public static int BytesPerPixel(PixelFormat format) =>
      format switch
      {
        PixelFormat.R8G8B8 => 3,
        PixelFormat.R8G8B8A8 => 4,
        _ => 1,
      };
  }
}
