//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A report card as shapes: its size, its title (the SVG's title and aria label), what it draws, in order, on the card (a translucent dark
//* grey rounded rectangle the size of the drawing), and its plot areas with their data ranges. SvgCardWriter writes it as SVG; the GUI draws
//* it directly. FlatShapes lists the shapes as a file shows them: those of the scrolling layers (ScrollShape) in their place.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Charts
{
  public sealed record CardDrawing(string Title, double Width, double Height, IReadOnlyList<CardShape> Shapes, IReadOnlyList<CardPlot> Plots)
  {
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
