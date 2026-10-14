//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A text of a report card: its baseline at (X, Y), anchored start, middle or end.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  /// <summary>A line of text. <paramref name="Link"/>, where not empty, is a file or address the text links to in a file (the GUI draws no link).</summary>
  public sealed record TextShape(double X, double Y, string Content, string Class = "", string Anchor = "middle", string Link = "") : CardShape;
}
