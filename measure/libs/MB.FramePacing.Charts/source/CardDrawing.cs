//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A report card as shapes: its size, its title (the SVG's title and aria label), what it draws, in order, on the card (a translucent dark
//* grey rounded rectangle the size of the drawing), and its plot areas with their data ranges. SvgCardWriter writes it as SVG; the GUI draws
//* it directly. FlatShapes lists the shapes as a file shows them: those of the scrolling layers (ScrollShape) in their place.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Charts
{
  public sealed record CardDrawing(string Title, double Width, double Height, IReadOnlyList<CardShape> Shapes, IReadOnlyList<CardPlot> Plots)
  {
    /// <summary>
    /// Style rules of the drawing's own, after the cards' style sheet (the classes only it uses); empty for none. The SVG has them; the
    /// GUI, which draws with the cards' style sheet, does not draw a card that needs its own.
    /// </summary>
    public string Style { get; init; } = string.Empty;

    /// <summary>What the drawing's style rules refer to by id (fill patterns), each an SVG element for the file's defs; empty for none.</summary>
    public IReadOnlyList<string> Definitions { get; init; } = System.Array.Empty<string>();

    /// <summary>The shapes in drawing order with each scrolling layer's shapes in its place (groups stay groups).</summary>
    public IEnumerable<CardShape> FlatShapes => Flat(Shapes);

    private static IEnumerable<CardShape> Flat(IEnumerable<CardShape> shapes)
    {
      foreach (var shape in shapes)
      {
        if (shape is ScrollShape layer)
        {
          foreach (var child in Flat(layer.Children))
            yield return child;
        }
        else
        {
          yield return shape;
        }
      }
    }
  }
}
