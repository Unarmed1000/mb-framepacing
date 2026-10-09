//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A straight line of a report card.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  public sealed record LineShape(string Class, SvgNumber X1, SvgNumber Y1, SvgNumber X2, SvgNumber Y2) : CardShape
  {
    /// <summary>What a viewer shows while the pointer is on the shape (the SVG's title of the element); empty for none.</summary>
    public string Tip { get; init; } = string.Empty;
  }
}
