//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Marks for the values a chart's scale cuts off (a hitch far above everything else): a small triangle at the edge of the data area where
//* the value leaves it, and the value as text beside it, largest first and only where it does not overlap an earlier one.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using SkiaSharp;

namespace MB.FramePacing.Charts
{
  internal sealed class ClippedValueMarks
  {
    private const float MarkerHalfWidth = 4;
    private const float MarkerHeight = 6;
    private const float FontSize = 11;
    private const float LabelGap = 4;

    private readonly List<(float X, double Value, bool Top)> m_marks = new List<(float, double, bool)>();

    /// <summary>A value at pixel column <paramref name="x"/> beyond the top (or the bottom) of the data area.</summary>
    public void Add(float x, double value, bool top) => m_marks.Add((x, value, top));

    public int Count => m_marks.Count;

    public void Draw(SKCanvas canvas, PixelRect data, SKColor markColor, SKColor textColor, Func<double, string> format)
    {
      if (m_marks.Count == 0)
        return;
      using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
      paint.Color = markColor;
      foreach (var (x, _, top) in m_marks)
      {
        float edge = top ? data.Top : data.Bottom;
        float inside = top ? edge + MarkerHeight : edge - MarkerHeight;
        using var path = new SKPath();
        path.MoveTo(x, edge);
        path.LineTo(x - MarkerHalfWidth, inside);
        path.LineTo(x + MarkerHalfWidth, inside);
        path.Close();
        canvas.DrawPath(path, paint);
      }

      using var typeface = SKTypeface.FromFamilyName(Fonts.Default) ?? SKTypeface.Default;
      using var font = new SKFont(typeface, FontSize);
      paint.Color = textColor;
      var placed = new List<(float Left, float Right, bool Top)>();
      foreach (var (x, value, top) in m_marks.OrderByDescending(m => Math.Abs(m.Value)))
      {
        string text = format(value);
        float left = x + MarkerHalfWidth + 2;
        float right = left + font.MeasureText(text);
        if (right > data.Right)
        {
          right = x - MarkerHalfWidth - 2;
          left = right - font.MeasureText(text);
        }
        if (placed.Any(p => p.Top == top && left < p.Right + LabelGap && right > p.Left - LabelGap))
          continue;
        placed.Add((left, right, top));
        float y = top ? data.Top + MarkerHeight + FontSize : data.Bottom - MarkerHeight - 2;
        canvas.DrawText(text, left, y, SKTextAlign.Left, font, paint);
      }
    }
  }
}
