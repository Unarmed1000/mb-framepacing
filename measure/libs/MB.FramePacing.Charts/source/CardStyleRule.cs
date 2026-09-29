//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a report card's style sheet (SvgMarkup) says about one class, or what several classes together say about a shape: its fill and
//* stroke with their opacities, the stroke's width, dashes and joins, and for text the font. Null means the sheet does not set it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Charts
{
  /// <param name="Fill">The fill colour as written (#rrggbb), or "none".</param>
  /// <param name="FillOpacity">The fill's opacity.</param>
  /// <param name="Stroke">The stroke colour as written (#rrggbb), or "none".</param>
  /// <param name="StrokeOpacity">The stroke's opacity.</param>
  /// <param name="StrokeWidth">The stroke's width.</param>
  /// <param name="StrokeDashArray">The dash and gap lengths.</param>
  /// <param name="RoundJoins">The stroke's segments join round.</param>
  /// <param name="FontFamily">The font families as written, first choice first.</param>
  /// <param name="FontSize">The font size in pixels.</param>
  /// <param name="FontWeight">The font weight (400 regular, 600 semi-bold, 700 bold).</param>
  /// <param name="LetterSpacingEm">The letter spacing in em.</param>
  /// <param name="TabularNumbers">The digits take equal widths.</param>
  public sealed record CardStyleRule(
    string? Fill = null,
    double? FillOpacity = null,
    string? Stroke = null,
    double? StrokeOpacity = null,
    double? StrokeWidth = null,
    IReadOnlyList<double>? StrokeDashArray = null,
    bool? RoundJoins = null,
    string? FontFamily = null,
    double? FontSize = null,
    int? FontWeight = null,
    double? LetterSpacingEm = null,
    bool? TabularNumbers = null
  )
  {
    public static readonly CardStyleRule Empty = new CardStyleRule();

    /// <summary>This rule with every value <paramref name="later"/> sets replacing its own (a later rule of the sheet wins).</summary>
    public CardStyleRule Then(CardStyleRule later) =>
      new CardStyleRule(
        later.Fill ?? Fill,
        later.FillOpacity ?? FillOpacity,
        later.Stroke ?? Stroke,
        later.StrokeOpacity ?? StrokeOpacity,
        later.StrokeWidth ?? StrokeWidth,
        later.StrokeDashArray ?? StrokeDashArray,
        later.RoundJoins ?? RoundJoins,
        later.FontFamily ?? FontFamily,
        later.FontSize ?? FontSize,
        later.FontWeight ?? FontWeight,
        later.LetterSpacingEm ?? LetterSpacingEm,
        later.TabularNumbers ?? TabularNumbers
      );
  }
}
