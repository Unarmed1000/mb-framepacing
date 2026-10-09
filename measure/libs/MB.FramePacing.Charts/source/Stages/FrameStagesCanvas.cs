//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a frame stages chart draws into: shapes on the cards' dark card, with a title and a line under it, numbers to a tenth of a pixel.
//* A tip is a shape's tooltip. A polygon and a line through several points are paths.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Linq;
using static MB.FramePacing.Charts.SvgMarkup;

namespace MB.FramePacing.Charts.Stages
{
  internal sealed class FrameStagesCanvas
  {
    private readonly List<CardShape> m_shapes = new List<CardShape>();
    private readonly string m_title;
    private readonly double m_width;
    private readonly double m_height;

    public FrameStagesCanvas(double width, double height, string title, string subtitle)
    {
      m_width = width;
      m_height = height;
      m_title = title;
      Text(28, 40, title, "title");
      Text(28, 62, subtitle, "sub");
    }

    public void Rect(double x, double y, double width, double height, string style, double radius = 0, string tip = "") =>
      m_shapes.Add(new RectShape(style, N(x, 1), N(y, 1), N(width, 1), N(height, 1), radius != 0 ? Coordinate(radius) : string.Empty) { Tip = tip });

    public void Line(double x1, double y1, double x2, double y2, string style, string tip = "") =>
      m_shapes.Add(new LineShape(style, N(x1, 1), N(y1, 1), N(x2, 1), N(y2, 1)) { Tip = tip });

    public void Text(double x, double y, string text, string style = "", string anchor = "start") =>
      m_shapes.Add(new TextShape(x, y, text, style, anchor));

    /// <summary>A line through the points, not closed.</summary>
    public void Polyline(IReadOnlyList<(double X, double Y)> points, string style) => m_shapes.Add(new PathShape(style, Path(points, close: false)));

    public void Polygon(IReadOnlyList<(double X, double Y)> points, string style, string tip = "") =>
      m_shapes.Add(new PathShape(style, Path(points, close: true)) { Tip = tip });

    /// <summary>Colour swatches with their names, left to right from <paramref name="x"/>.</summary>
    public void Key(double x, double y, IReadOnlyList<(string Style, string Name)> entries)
    {
      foreach (var (style, name) in entries)
      {
        Rect(x, y - 10, 12, 12, style, radius: 2);
        Text(x + 18, y, name, "axis");
        x += 18 + (7.2 * name.Length) + 22;
      }
    }

    /// <summary>The drawing: the shapes with the charts' own style and fill patterns.</summary>
    public CardDrawing Drawing(string style, IReadOnlyList<string> definitions) =>
      new CardDrawing(m_title, m_width, m_height, m_shapes, new List<CardPlot>()) { Style = style, Definitions = definitions };

    /// <summary>A number to a tenth, without a trailing ".0".</summary>
    private static string Coordinate(double value)
    {
      string text = Fixed(value, 1);
      return text.EndsWith(".0", System.StringComparison.Ordinal) ? text[..^2] : text;
    }

    private static string Path(IReadOnlyList<(double X, double Y)> points, bool close) =>
      "M" + string.Join(" L", points.Select(point => $"{Coordinate(point.X)} {Coordinate(point.Y)}")) + (close ? " Z" : string.Empty);
  }
}
