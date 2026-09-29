//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The report cards' style sheet (SvgMarkup's, the one the SVG files carry) parsed into rules, so the GUI draws a card with exactly the
//* values the SVG shows: a shape's style is the 'text' rule for texts, then every rule of its classes in the order of the sheet (all
//* selectors are single classes, so a later rule wins, as in CSS).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MB.FramePacing.Charts
{
  public static class CardStyle
  {
    private static readonly IReadOnlyList<(string Selector, CardStyleRule Rule)> g_rules = Parse(
      SvgMarkup.DiagramStyle + SvgMarkup.ChartStyle + SvgMarkup.ReportStyle
    );

    private static readonly ConcurrentDictionary<(string Classes, bool Text), CardStyleRule> g_resolved = new();

    /// <summary>Every rule of the sheet in its order: the selector ("text", or a class without its dot) and what it sets.</summary>
    public static IReadOnlyList<(string Selector, CardStyleRule Rule)> Rules => g_rules;

    /// <summary>The style of a shape with <paramref name="classes"/> (space separated); <paramref name="text"/> starts from the 'text' rule.</summary>
    public static CardStyleRule Resolve(string classes, bool text) =>
      g_resolved.GetOrAdd(
        (classes, text),
        key =>
        {
          var names = key.Classes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
          var style = CardStyleRule.Empty;
          foreach (var (selector, rule) in g_rules)
          {
            if (selector == "text" ? key.Text : names.Contains(selector, StringComparer.Ordinal))
              style = style.Then(rule);
          }
          return style;
        }
      );

    /// <summary>The rules of a style sheet: 'selector { name: value; ... }' blocks.</summary>
    public static IReadOnlyList<(string Selector, CardStyleRule Rule)> Parse(string sheet)
    {
      var rules = new List<(string, CardStyleRule)>();
      foreach (Match block in Regex.Matches(sheet, @"([^{}]+)\{([^}]*)\}"))
      {
        string selector = block.Groups[1].Value.Trim().TrimStart('.');
        var rule = CardStyleRule.Empty;
        foreach (string declaration in block.Groups[2].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
          int colon = declaration.IndexOf(':', StringComparison.Ordinal);
          if (colon < 0)
            continue;
          string value = declaration[(colon + 1)..].Trim();
          rule = declaration[..colon].Trim() switch
          {
            "fill" => rule with { Fill = value },
            "fill-opacity" => rule with { FillOpacity = Number(value) },
            "stroke" => rule with { Stroke = value },
            "stroke-opacity" => rule with { StrokeOpacity = Number(value) },
            "stroke-width" => rule with { StrokeWidth = Number(value) },
            "stroke-dasharray" => rule with { StrokeDashArray = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Number).ToArray() },
            "stroke-linejoin" => rule with { RoundJoins = value == "round" },
            "font-family" => rule with { FontFamily = value },
            "font-size" => rule with { FontSize = Number(value.Replace("px", string.Empty, StringComparison.Ordinal)) },
            "font-weight" => rule with { FontWeight = int.Parse(value, CultureInfo.InvariantCulture) },
            "letter-spacing" => rule with { LetterSpacingEm = Number(value.Replace("em", string.Empty, StringComparison.Ordinal)) },
            "font-variant-numeric" => rule with { TabularNumbers = value == "tabular-nums" },
            _ => throw new FormatException($"The card style sheet uses '{declaration}', which the GUI does not draw"),
          };
        }
        rules.Add((selector, rule));
      }
      return rules;
    }

    private static double Number(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
  }
}
