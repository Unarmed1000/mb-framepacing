//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A line of text in pieces of their own style, laid out end to end by whatever draws it: an SVG text with a tspan per piece (the browser
//* measures), the GUI measures each piece. So nothing that follows a word needs its width guessed: a key's swatches and words, a value
//* with its detail. Its baseline at (X, Y), anchored start, middle or end; Class applies to every piece, a piece's Class adds to it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Charts
{
  public sealed record TextRunsShape(double X, double Y, IReadOnlyList<TextRun> Runs, string Class = "", string Anchor = "start") : CardShape
  {
    /// <summary>The whole line's text.</summary>
    public string Content => string.Concat(Runs.Select(r => r.Text));

    /// <summary>A piece's classes: the line's and its own.</summary>
    public string ClassOf(TextRun run) => (Class + " " + run.Class).Trim();
  }
}
