//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Parses an optional frame rate typed into a text box.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Globalization;

namespace MB.FramePacing.Gui.ViewModels
{
  public static class FrameRateText
  {
    /// <summary>Null for an empty box, the rate otherwise; throws for anything that is not a positive number.</summary>
    public static double? ParseOptional(string? text)
    {
      if (string.IsNullOrWhiteSpace(text))
        return null;
      if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double fps) || fps <= 0)
        throw new FormatException($"'{text}' is not a frame rate");
      return fps;
    }
  }
}
