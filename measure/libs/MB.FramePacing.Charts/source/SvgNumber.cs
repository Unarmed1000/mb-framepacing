//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A number of a card shape and the decimals the SVG writes it with (Python's rounding, SvgMarkup.Fixed). The GUI draws the value itself.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  public readonly record struct SvgNumber(double Value, int Decimals)
  {
    public override string ToString() => SvgMarkup.Fixed(Value, Decimals);
  }
}
