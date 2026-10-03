//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Writes a card drawing as SVG: the element with its size, title and style sheet (SvgMarkup's, verbatim), an optional page colour behind
//* the card, the card, then every shape in order, one element per line. A file shows exactly its range: the scrolling layers' shapes are
//* written where they are. A page that scrolls the card itself (the playback page) asks for the layers as they are: each a group with the
//* class "scroll-layer", clipped to its area, which the page moves sideways.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using static MB.FramePacing.Charts.SvgMarkup;

namespace MB.FramePacing.Charts
{
  public static class SvgCardWriter
  {
    /// <summary>The class of a scrolling layer's group when the layers are kept (<see cref="Write"/>'s scrollLayers).</summary>
    public const string ScrollLayerClass = "scroll-layer";

    /// <summary>
    /// The SVG of <paramref name="drawing"/>; <paramref name="background"/> puts a page colour behind the card (for previews). With
    /// <paramref name="scrollLayers"/> (a prefix for the clip paths' ids, unique in the page) the scrolling layers stay groups of their own.
    /// </summary>
    public static string Write(CardDrawing drawing, string? background = null, string? scrollLayers = null)
    {
      var layers = scrollLayers != null ? new LayerIds(scrollLayers) : null;
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
        Add(parts, shape, layers);
      parts.Add("</svg>");
      return string.Join("\n", parts) + "\n";
    }

    private static void Add(List<string> parts, CardShape shape, LayerIds? layers)
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
        case TextRunsShape t:
        {
          // One text, a tspan per styled piece: the browser lays them out end to end
          string cls = t.Class.Length > 0 ? $" class=\"{t.Class}\"" : string.Empty;
          string body = string.Concat(
            t.Runs.Select(r => r.Class.Length > 0 ? $"<tspan class=\"{r.Class}\">{Escape(r.Text)}</tspan>" : Escape(r.Text))
          );
          parts.Add($"<text x=\"{Fixed(t.X, 1)}\" y=\"{Fixed(t.Y, 1)}\" text-anchor=\"{t.Anchor}\"{cls}>{body}</text>");
          break;
        }
        case ScrollShape scroll when layers != null:
        {
          string id = $"{layers.Prefix}-clip-{layers.Count++}";
          parts.Add(
            $"<clipPath id=\"{id}\"><rect x=\"{Fixed(scroll.Left, 1)}\" y=\"{Fixed(scroll.Top, 1)}\" width=\"{Fixed(scroll.Right - scroll.Left, 1)}\" height=\"{Fixed(scroll.Bottom - scroll.Top, 1)}\"/></clipPath>"
          );
          parts.Add($"<g clip-path=\"url(#{id})\"><g class=\"{ScrollLayerClass}\">");
          foreach (var child in scroll.Children)
            Add(parts, child, layers);
          parts.Add("</g></g>");
          break;
        }
        case ScrollShape scroll:
          // A file shows exactly its range: the scrolling shapes are written where they are
          foreach (var child in scroll.Children)
            Add(parts, child, layers);
          break;
        case GroupShape g:
          parts.Add($"<g transform=\"translate(0 {Fixed(g.TranslateY, 0)})\">");
          foreach (var child in g.Children)
            Add(parts, child, layers);
          parts.Add("</g>");
          break;
        default:
          throw new ArgumentException($"Unknown card shape {shape.GetType().Name}", nameof(shape));
      }
    }

    /// <summary>The clip paths' ids of one SVG with its scrolling layers kept: the prefix, and how many there are so far.</summary>
    private sealed class LayerIds
    {
      public LayerIds(string prefix)
      {
        Prefix = prefix;
      }

      public string Prefix { get; }

      public int Count { get; set; }
    }
  }
}
