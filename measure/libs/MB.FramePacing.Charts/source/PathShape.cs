//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A path of a report card, as SVG path data (M, L, H, V, h, v, Z; the GUI parses the same data).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  public sealed record PathShape(string Class, string Data) : CardShape
  {
    /// <summary>What a viewer shows while the pointer is on the shape (the SVG's title of the element); empty for none.</summary>
    public string Tip { get; init; } = string.Empty;
  }
}
