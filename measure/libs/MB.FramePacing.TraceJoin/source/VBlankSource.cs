//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Where a trace has the display's vertical blanks: the kernel's events of one display controller, or a counter that changes with every
//* one. Which counter is the display's own is not something the tools know.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.TraceJoin
{
  /// <summary>A source of vertical blank times in a trace.</summary>
  /// <param name="IsCounter">True for a counter, false for the kernel's vertical blank events.</param>
  /// <param name="Count">How many vertical blanks it has.</param>
  public sealed record VBlankSource(bool IsCounter, string Name, long Count)
  {
    public string Kind => IsCounter ? "counter" : "kernel";
  }
}
