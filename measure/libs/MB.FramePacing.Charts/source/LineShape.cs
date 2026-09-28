//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A straight line of a report card.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  public sealed record LineShape(string Class, SvgNumber X1, SvgNumber Y1, SvgNumber X2, SvgNumber Y2) : CardShape;
}
