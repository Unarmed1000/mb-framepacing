//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A number of a card shape and the decimals the SVG writes it with (Python's rounding, SvgMarkup.Fixed). The GUI draws the value itself.
//* Interpolated into a string or a StringBuilder it writes its digits in place: no string per number.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Charts
{
  public readonly record struct SvgNumber(double Value, int Decimals) : ISpanFormattable
  {
    public override string ToString() => SvgMarkup.Fixed(Value, Decimals);

    /// <summary>The same text as <see cref="ToString()"/>: the format and provider are not used.</summary>
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <summary>The same text as <see cref="ToString()"/>, written into <paramref name="destination"/>: the format and provider are not used.</summary>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
      SvgMarkup.TryFormatFixed(Value, Decimals, destination, out charsWritten);
  }
}
