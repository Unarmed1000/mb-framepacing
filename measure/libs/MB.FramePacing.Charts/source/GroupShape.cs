//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Shapes of a report card moved down by TranslateY (whole pixels).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Charts
{
  public sealed record GroupShape(double TranslateY, IReadOnlyList<CardShape> Children) : CardShape;
}
