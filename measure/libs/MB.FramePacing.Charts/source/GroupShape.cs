//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Shapes of a report card moved down by TranslateY (whole pixels).
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Charts
{
  public sealed record GroupShape(double TranslateY, IReadOnlyList<CardShape> Children) : CardShape;
}
