//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A path of a report card, as SVG path data (M, L, H, V, h, v, Z; the GUI parses the same data).
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  public sealed record PathShape(string Class, string Data) : CardShape;
}
