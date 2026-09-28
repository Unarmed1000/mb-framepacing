//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The SVG building blocks of the reports, ported from mb-framepacing-explained's timing diagrams (tools/timing_diagrams/generate_diagrams.py
//* and generate_charts.py): their style sheet verbatim (a translucent dark grey card that reads the same on a white and on a dark page), the
//* text() and ms() helpers with Python's number formatting, and the card with its title and description.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;

namespace MB.FramePacing.Charts
{
  public static class SvgMarkup
  {
    /// <summary>generate_diagrams.py's STYLE, verbatim.</summary>
    public const string DiagramStyle =
      "\n  text { font-family: \"Segoe UI Variable Text\", \"Segoe UI\", Inter, system-ui, -apple-system, \"Helvetica Neue\", Arial, sans-serif;\n"
      + "         font-size: 13px; fill: #e6edf3; font-variant-numeric: tabular-nums; }\n"
      + "  .card { fill: #1f242b; fill-opacity: 0.94; stroke: #8b949e; stroke-opacity: 0.25; stroke-width: 1; }\n"
      + "  .title { font-size: 20px; font-weight: 600; letter-spacing: -0.01em; }\n"
      + "  .sub { fill: #8b949e; }\n"
      + "  .label { font-size: 11px; font-weight: 600; letter-spacing: 0.08em; fill: #8b949e; }\n"
      + "  .vsync-n { font-size: 11px; fill: #6e7681; letter-spacing: 0.04em; }\n"
      + "  .axis { font-size: 12px; fill: #c9d1d9; }\n"
      + "  .vsync { stroke: #ffffff; stroke-opacity: 0.34; stroke-width: 1; stroke-dasharray: 3 4; }\n"
      + "  .vsync-skip { stroke: #ffffff; stroke-opacity: 0.09; stroke-width: 1; stroke-dasharray: 2 6; }\n"
      + "  .vsync-target { font-size: 11px; font-weight: 600; fill: #c9d1d9; letter-spacing: 0.04em; }\n"
      + "  .vsync-n-skip { font-size: 11px; fill: #545b64; letter-spacing: 0.04em; }\n"
      + "  .box { fill: #ffffff; fill-opacity: 0.06; stroke: #ffffff; stroke-opacity: 0.22; stroke-width: 1; }\n"
      + "  .frame { font-size: 14px; font-weight: 700; }\n"
      + "  .box-time { fill: #b1bac4; font-size: 12px; }\n"
      + "  .arrow { stroke: #8b949e; stroke-width: 1.2; }\n"
      + "  .arrowhead { fill: #8b949e; }\n"
      + "  .ok { fill: #2ea043; }\n"
      + "  .hold { fill: #2ea043; fill-opacity: 0.45; }\n"
      + "  .again { fill: #d29922; }\n"
      + "  .off { fill: #e5534b; }\n"
      + "  .cell-text { font-size: 14px; font-weight: 700; fill: #ffffff; }\n"
      + "  .dark-text { fill: #1c1f24; }\n"
      + "  .zero { fill: #6e7681; }\n"
      + "  .err { fill: #ff7b72; font-weight: 600; }\n"
      + "  .err-pill { fill: #e5534b; fill-opacity: 0.16; }\n"
      + "  .neutral { fill: #3d444d; }\n"
      + "  .lane { font-size: 15px; font-weight: 600; }\n"
      + "  .same { fill: #7ee787; font-weight: 600; }\n";

    /// <summary>generate_charts.py's CHART_STYLE, verbatim.</summary>
    public const string ChartStyle =
      "\n  .tile { fill: #ffffff; fill-opacity: 0.05; stroke: #ffffff; stroke-opacity: 0.12; stroke-width: 1; }\n"
      + "  .tile-value { font-size: 20px; font-weight: 600; }\n"
      + "  .grid { stroke: #ffffff; stroke-opacity: 0.1; stroke-width: 1; }\n"
      + "  .zero-line { stroke: #ffffff; stroke-opacity: 0.45; stroke-width: 1; }\n"
      + "  .bar { fill: #e5534b; }\n"
      + "  .display-line { stroke: #2ea043; stroke-width: 2; fill: none; }\n"
      + "  .step-line { stroke: #58a6ff; stroke-width: 1.2; fill: none; stroke-linejoin: round; }\n"
      + "  .strip-a { fill: #6e7681; }\n"
      + "  .strip-b { fill: #adbac7; }\n"
      + "  .average-line { stroke: #d29922; stroke-width: 1.5; stroke-dasharray: 6 4; }\n"
      + "  .average-text { fill: #d29922; font-size: 12px; }\n";

    /// <summary>
    /// The report's own classes, in the same palette: the error threshold band, the held steps of the display time step (after the web page's
    /// frame chart: green as planned, red held too long, faint risers), the frametime (the web page's step-line blue) and CPU busy, the late
    /// share, the late strip cells, the marks of values beyond a scale, and warning values.
    /// </summary>
    public const string ReportStyle =
      "\n  .band { fill: #ffffff; fill-opacity: 0.06; }\n"
      + "  .held { stroke: #2ea043; stroke-width: 2.5; fill: none; }\n"
      + "  .held-late { stroke: #e5534b; stroke-width: 2.5; fill: none; }\n"
      + "  .bar-range { fill: #e5534b; fill-opacity: 0.3; }\n"
      + "  .held-fill { fill: #2ea043; }\n"
      + "  .held-fill-late { fill: #e5534b; }\n"
      + "  .held-range { fill: #2ea043; fill-opacity: 0.3; }\n"
      + "  .held-range-late { fill: #e5534b; fill-opacity: 0.3; }\n"
      + "  .riser { stroke: #ffffff; stroke-opacity: 0.3; stroke-width: 1; fill: none; }\n"
      + "  .late-line { stroke: #e5534b; stroke-width: 1.5; fill: none; stroke-linejoin: round; }\n"
      + "  .late-line-adapted { stroke: #d29922; stroke-width: 1.5; fill: none; stroke-linejoin: round; }\n"
      + "  .late-line-none { stroke: #2ea043; stroke-width: 1.5; fill: none; stroke-linejoin: round; }\n"
      + "  .frametime { stroke: #58a6ff; stroke-width: 2.5; fill: none; }\n"
      + "  .frametime-range { fill: #58a6ff; fill-opacity: 0.3; }\n"
      + "  .frametime-fill { fill: #58a6ff; }\n"
      + "  .cpu-busy { fill: #58a6ff; fill-opacity: 0.22; }\n"
      + "  .strip-late { fill: #e5534b; }\n"
      + "  .clip-mark { fill: #e5534b; }\n"
      + "  .clip-text { font-size: 11px; fill: #e6edf3; }\n"
      + "  .warn { fill: #d29922; }\n";

    private const double Epsilon = 1e-9;

    /// <summary>generate_diagrams.py's ms(): one decimal without a trailing .0, "0" for zero, "−" (U+2212) for negatives, "+" when asked.</summary>
    public static string Ms(double value, bool sign = false)
    {
      string text = Fixed(Math.Abs(value), 1);
      if (text.EndsWith(".0", StringComparison.Ordinal))
        text = text[..^2];
      if (Math.Abs(value) < Epsilon)
        return "0";
      if (value < 0)
        return "−" + text;
      return sign ? "+" + text : text;
    }

    /// <summary>generate_diagrams.py's text(): a text element at (x, y), one decimal, with a class and an anchor.</summary>
    public static string Text(double x, double y, string content, string cls = "", string anchor = "middle")
    {
      string classAttribute = cls.Length > 0 ? $" class=\"{cls}\"" : string.Empty;
      return $"<text x=\"{Fixed(x, 1)}\" y=\"{Fixed(y, 1)}\" text-anchor=\"{anchor}\"{classAttribute}>{Escape(content)}</text>";
    }

    /// <summary>
    /// The SVG element, its style, its card and its title and description lines (generate_diagrams.py's _svg_start). Without
    /// <paramref name="showTitle"/> the title only names the SVG, and the description lines take its place.
    /// </summary>
    public static List<string> Start(
      string title,
      double width,
      double height,
      IEnumerable<string> description,
      string? background = null,
      bool showTitle = true
    )
    {
      var parts = new List<string>
      {
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Fixed(width, 0)}\" height=\"{Fixed(height, 0)}\" viewBox=\"0 0 {Fixed(width, 0)} {Fixed(height, 0)}\" role=\"img\" aria-label=\"{Escape(title)}\">",
        $"<title>{Escape(title)}</title>",
        $"<style>{DiagramStyle}{ChartStyle}{ReportStyle}</style>",
      };
      if (background != null)
        parts.Add($"<rect width=\"100%\" height=\"100%\" fill=\"{Escape(background)}\"/>");
      parts.Add($"<rect class=\"card\" x=\"0.5\" y=\"0.5\" width=\"{Fixed(width - 1, 0)}\" height=\"{Fixed(height - 1, 0)}\" rx=\"14\"/>");
      if (showTitle)
        parts.Add(Text(20, 30, title, "title", "start"));
      int line = 0;
      foreach (var text in description)
        parts.Add(Text(20, (showTitle ? 54 : 30) + (line++ * 19), text, "sub", "start"));
      return parts;
    }

    /// <summary>
    /// Python's f"{value:.{decimals}f}": the double's exact decimal value rounded half to even (.NET's "F" rounds an exact tie away from zero,
    /// so 0.25 would give 0.3 where Python gives 0.2), and a minus sign for any negative value, even one that rounds to zero.
    /// </summary>
    public static string Fixed(double value, int decimals)
    {
      // .NET formats doubles exactly: 60 decimals hold every digit of the values drawn here
      string exact = Math.Abs(value).ToString("F60", CultureInfo.InvariantCulture);
      int point = exact.IndexOf('.');
      var digits = new List<char>(exact[..point]);
      digits.AddRange(exact.Substring(point + 1, decimals));
      string rest = exact[(point + 1 + decimals)..];
      bool up = rest.Length > 0 && (rest[0] > '5' || (rest[0] == '5' && (rest.AsSpan(1).IndexOfAnyExcept('0') >= 0 || (digits[^1] - '0') % 2 == 1)));
      if (up)
      {
        int i = digits.Count - 1;
        for (; i >= 0 && digits[i] == '9'; --i)
          digits[i] = '0';
        if (i < 0)
          digits.Insert(0, '1');
        else
          digits[i] = (char)(digits[i] + 1);
      }
      string whole = new string(digits.ToArray(), 0, digits.Count - decimals);
      string text = decimals > 0 ? whole + "." + new string(digits.ToArray(), digits.Count - decimals, decimals) : whole;
      return double.IsNegative(value) ? "-" + text : text;
    }

    /// <summary>xml.sax.saxutils.escape: &amp;, &lt; and &gt;.</summary>
    public static string Escape(string text) =>
      text.Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
  }
}
