//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Writes a card drawing as SVG: the element with its size, title and style sheet (SvgMarkup's, verbatim), an optional page colour behind
//* the card, the card, then every shape in order, one element per line.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using static MB.FramePacing.Charts.SvgMarkup;

namespace MB.FramePacing.Charts
{
  public static class SvgCardWriter
  {
    /// <summary>The SVG of <paramref name="drawing"/>; <paramref name="background"/> puts a page colour behind the card (for previews).</summary>
    public static string Write(CardDrawing drawing, string? background = null)
    {
      string width = Fixed(drawing.Width, 0);
      string height = Fixed(drawing.Height, 0);
      var parts = new List<string>
      {
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" role=\"img\" aria-label=\"{Escape(drawing.Title)}\">",
        $"<title>{Escape(drawing.Title)}</title>",
        $"<style>{DiagramStyle}{ChartStyle}{ReportStyle}</style>",
      };
      if (background != null)
        parts.Add($"<rect width=\"100%\" height=\"100%\" fill=\"{Escape(background)}\"/>");
      parts.Add(
        $"<rect class=\"card\" x=\"0.5\" y=\"0.5\" width=\"{Fixed(drawing.Width - 1, 0)}\" height=\"{Fixed(drawing.Height - 1, 0)}\" rx=\"14\"/>"
      );
      foreach (var shape in drawing.Shapes)
        Add(parts, shape);
      parts.Add("</svg>");
      return string.Join("\n", parts) + "\n";
    }

    private static void Add(List<string> parts, CardShape shape)
    {
      switch (shape)
      {
        case RectShape r:
          parts.Add(
            $"<rect class=\"{r.Class}\" x=\"{r.X}\" y=\"{r.Y}\" width=\"{r.Width}\" height=\"{r.Height}\"{(r.Rx.Length > 0 ? $" rx=\"{r.Rx}\"" : string.Empty)}/>"
          );
          break;
        case LineShape l:
          parts.Add($"<line class=\"{l.Class}\" x1=\"{l.X1}\" y1=\"{l.Y1}\" x2=\"{l.X2}\" y2=\"{l.Y2}\"/>");
          break;
        case PathShape p:
          parts.Add($"<path class=\"{p.Class}\" d=\"{p.Data}\"/>");
          break;
        case TextShape t:
          parts.Add(Text(t.X, t.Y, t.Content, t.Class, t.Anchor));
          break;
        case GroupShape g:
          parts.Add($"<g transform=\"translate(0 {Fixed(g.TranslateY, 0)})\">");
          foreach (var child in g.Children)
            Add(parts, child);
          parts.Add("</g>");
          break;
        default:
          throw new ArgumentException($"Unknown card shape {shape.GetType().Name}", nameof(shape));
      }
    }
  }
}
