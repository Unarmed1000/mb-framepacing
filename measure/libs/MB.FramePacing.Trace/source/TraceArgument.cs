//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A named value of a slice or a moment: the trace viewer shows them for the selected one.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. A named value of a slice or a moment: a whole number, a number or a text.</summary>
  public readonly record struct TraceArgument(string Name, TraceArgumentKind Kind, long Whole, double Number, string Text)
  {
    public static TraceArgument Of(string name, long value) => new TraceArgument(name, TraceArgumentKind.Whole, value, 0, string.Empty);

    public static TraceArgument Of(string name, double value) => new TraceArgument(name, TraceArgumentKind.Number, 0, value, string.Empty);

    public static TraceArgument Of(string name, string value) => new TraceArgument(name, TraceArgumentKind.Text, 0, 0, value);
  }
}
