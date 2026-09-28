//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A text of a report card: its baseline at (X, Y), anchored start, middle or end.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  public sealed record TextShape(double X, double Y, string Content, string Class = "", string Anchor = "middle") : CardShape;
}
