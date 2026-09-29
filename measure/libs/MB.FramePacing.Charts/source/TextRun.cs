//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One piece of a line of text (TextRunsShape): its text and its classes, which add to the line's.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  public sealed record TextRun(string Text, string Class = "");
}
