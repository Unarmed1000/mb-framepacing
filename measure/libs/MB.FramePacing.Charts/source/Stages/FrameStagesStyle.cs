//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The frame stages charts' own style rules and fill patterns, after the cards' style sheet: the stages' colours, the patterns of the
//* waits (hatched: a sleep or a wait the log does not name; bars: a vertical blank wait; dots: a frame slot wait; crossed: waiting to be
//* shown), and for the lanes view a colour per frame.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MB.FramePacing.Charts.Stages
{
  internal static class FrameStagesStyle
  {
    /// <summary>A colour per frame for the lanes view: its fill, its text, its expected box, its joining line, its waiting pattern.</summary>
    public static readonly IReadOnlyList<string> FrameColours = new[] { "#58a6ff", "#3fb950", "#d29922", "#a371f7", "#39c5cf", "#db61a2" };

    public static int Hues => FrameColours.Count;

    public const string Style =
      "\n  text { pointer-events: none; }\n"
      + "  .row-line { stroke: #ffffff; stroke-opacity: 0.06; stroke-width: 1; }\n"
      + "  .vsync { stroke: #ffffff; stroke-opacity: 0.3; stroke-width: 1; stroke-dasharray: 3 4; }\n"
      + "  .vsync-n { font-size: 11px; fill: #6e7681; letter-spacing: 0.04em; }\n"
      + "  .frame-label { font-size: 12px; font-weight: 600; fill: #c9d1d9; }\n"
      + "  .lane-label { font-size: 10px; fill: #6e7681; }\n"
      + "  .update { fill: #58a6ff; fill-opacity: 0.5; }\n"
      + "  .draw { fill: #58a6ff; }\n"
      + "  .acquire { fill: #a371f7; }\n"
      + "  .present { fill: #d29922; }\n"
      + "  .gpu { fill: #2ea043; }\n"
      + "  .gpu-bound { fill: #2ea043; fill-opacity: 0.3; stroke: #2ea043; stroke-width: 1; stroke-dasharray: 3 3; }\n"
      + "  .wait { fill: url(#hatch); }\n"
      + "  .wait-vsync { fill: url(#bars); }\n"
      + "  .wait-slot { fill: url(#dots); }\n"
      + "  .dot { fill: #8b949e; }\n"
      + "  .timed-call { fill: #db61a2; stroke: #f0f6fc; stroke-width: 1.2; }\n"
      + "  .timed-arrow { fill: #db61a2; stroke: #0d1117; stroke-width: 0.8; }\n"
      + "  .unlogged { fill: #8b949e; fill-opacity: 0.1; stroke: #8b949e; stroke-width: 1; stroke-dasharray: 3 3; }\n"
      + "  .queue { fill: url(#cross); }\n"
      + "  .hatch-line { stroke: #8b949e; stroke-width: 1.4; }\n"
      + "  .screen-a { fill: #6e7681; }\n"
      + "  .screen-b { fill: #adbac7; }\n"
      + "  .screen-long { fill: #e5534b; }\n"
      + "  .marker-tick { stroke: #ffffff; stroke-opacity: 0.75; stroke-width: 1; }\n"
      + "  .present-arrow { fill: #d29922; stroke: #0d1117; stroke-width: 0.8; }\n"
      + "  .present-line { stroke: #d29922; stroke-width: 1.4; fill: none; pointer-events: none; }\n"
      + "  .join-line { stroke: #8b949e; stroke-width: 1; fill: none; pointer-events: none; }\n"
      + "  .expected { fill: #f0f6fc; fill-opacity: 0.9; }\n"
      + "  .timed { fill: #db61a2; }\n"
      + "  .numbers { font-size: 11px; fill: #c9d1d9; }\n"
      + "  .not-shown { font-size: 11px; font-weight: 600; fill: #d29922; }\n"
      + "  .no-time { fill: #d29922; fill-opacity: 0.25; stroke: #d29922; stroke-width: 1.4; }\n"
      + "  .unknown { fill: #8b949e; fill-opacity: 0.1; stroke: #8b949e; stroke-width: 1; stroke-dasharray: 2 3; }\n"
      + "  .unknown-text { font-size: 12px; font-weight: 600; fill: #8b949e; }\n"
      + "  .err { font-size: 10px; font-weight: 600; fill: #0d1117; }\n"
      + "  .err-big { font-size: 10px; font-weight: 700; fill: #ff7b72; }\n"
      + "  .err-back { fill: #0d1117; fill-opacity: 0.85; pointer-events: none; }\n"
      + "  .lane { fill: #ffffff; fill-opacity: 0.035; }\n"
      + "  .lane-name { font-size: 12px; fill: #c9d1d9; }\n"
      + "  .soft { fill-opacity: 0.5; }\n"
      + "  .call { stroke: #f0f6fc; stroke-width: 1.2; }\n"
      + "  .submit { fill: #0d1117; fill-opacity: 0.6; }\n"
      + "  .long { stroke: #e5534b; stroke-width: 2; }\n"
      + "  .on { font-size: 11px; font-weight: 600; fill: #0d1117; }\n"
      + "  .id-in { font-size: 10px; font-weight: 600; fill: #0d1117; }\n"
      + "  .id-by { font-size: 10px; font-weight: 600; fill: #8b949e; }\n"
      + "  .slot { fill: none; stroke: #8b949e; stroke-opacity: 0.55; stroke-width: 1; }\n"
      + "  .over { fill: #e5534b; }\n"
      + "  .late-low { fill: #8b949e; }\n"
      + "  .late-high { fill: #d29922; }\n"
      + "  .late-full { fill: #e5534b; }\n"
      + "  .limit-text { font-size: 11px; fill: #ff7b72; }\n"
      + "  .change { stroke: #f0f6fc; stroke-width: 1.5; }\n"
      + "  .change-text { font-size: 11px; font-weight: 600; fill: #f0f6fc; }\n";

    /// <summary>The lanes view's rules of each frame colour.</summary>
    public static string LaneStyle { get; } = MakeLaneStyle();

    public static IReadOnlyList<string> Definitions { get; } =
      new[]
      {
        "<pattern id=\"hatch\" width=\"5\" height=\"5\" patternUnits=\"userSpaceOnUse\" patternTransform=\"rotate(45)\">"
          + "<line class=\"hatch-line\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"5\"/></pattern>",
        "<pattern id=\"cross\" width=\"5\" height=\"5\" patternUnits=\"userSpaceOnUse\" patternTransform=\"rotate(45)\">"
          + "<line class=\"hatch-line\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"5\"/><line class=\"hatch-line\" x1=\"0\" y1=\"0\" x2=\"5\" y2=\"0\"/></pattern>",
        "<pattern id=\"bars\" width=\"4\" height=\"4\" patternUnits=\"userSpaceOnUse\"><line class=\"hatch-line\" x1=\"1\" y1=\"0\" x2=\"1\" y2=\"4\"/></pattern>",
        "<pattern id=\"dots\" width=\"4\" height=\"4\" patternUnits=\"userSpaceOnUse\"><circle class=\"dot\" cx=\"2\" cy=\"2\" r=\"1\"/></pattern>",
      };

    /// <summary>The waiting pattern in each frame colour, for the lanes view.</summary>
    public static IReadOnlyList<string> LaneDefinitions { get; } =
      Enumerable
        .Range(0, FrameColours.Count)
        .Select(hue =>
          $"<pattern id=\"cross{hue}\" width=\"5\" height=\"5\" patternUnits=\"userSpaceOnUse\" patternTransform=\"rotate(45)\">"
          + $"<line class=\"x{hue}\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"5\"/><line class=\"x{hue}\" x1=\"0\" y1=\"0\" x2=\"5\" y2=\"0\"/></pattern>"
        )
        .ToArray();

    private static string MakeLaneStyle()
    {
      var style = new StringBuilder();
      for (int hue = 0; hue < FrameColours.Count; ++hue)
      {
        string colour = FrameColours[hue];
        style.Append($"  .f{hue} {{ fill: {colour}; }}\n");
        style.Append($"  .t{hue} {{ font-size: 11px; font-weight: 600; fill: {colour}; }}\n");
        style.Append($"  .e{hue} {{ fill: {colour}; fill-opacity: 0.12; stroke: {colour}; stroke-width: 1; }}\n");
        style.Append($"  .l{hue} {{ stroke: {colour}; stroke-opacity: 0.6; stroke-width: 1; fill: none; pointer-events: none; }}\n");
        style.Append($"  .q{hue} {{ fill: url(#cross{hue}); }}\n");
        style.Append($"  .x{hue} {{ stroke: {colour}; stroke-width: 1.4; }}\n");
        style.Append($"  .p{hue} {{ stroke: {colour}; stroke-width: 1.6; fill: none; }}\n");
        style.Append($"  .u{hue} {{ fill: {colour}; fill-opacity: 0.1; stroke: {colour}; stroke-width: 1; stroke-dasharray: 3 3; }}\n");
        style.Append($"  .s{hue} {{ fill: {colour}; fill-opacity: 0.85; }}\n");
      }
      return style.ToString();
    }
  }
}
