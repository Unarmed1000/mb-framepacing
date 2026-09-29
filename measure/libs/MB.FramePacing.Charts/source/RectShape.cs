//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A rectangle of a report card, with rounded corners of Rx (its SVG text, empty for square corners).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  public sealed record RectShape(string Class, SvgNumber X, SvgNumber Y, SvgNumber Width, SvgNumber Height, string Rx = "") : CardShape;
}
