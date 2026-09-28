//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A report card as shapes: its size, its title (the SVG's title and aria label), what it draws, in order, on the card (a translucent dark
//* grey rounded rectangle the size of the drawing), and its plot areas with their data ranges. SvgCardWriter writes it as SVG; the GUI draws
//* it directly.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Charts
{
  public sealed record CardDrawing(string Title, double Width, double Height, IReadOnlyList<CardShape> Shapes, IReadOnlyList<CardPlot> Plots);
}
