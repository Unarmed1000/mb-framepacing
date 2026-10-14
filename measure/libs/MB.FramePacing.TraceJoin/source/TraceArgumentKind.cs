//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What kind of value an argument of a slice or a moment holds.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.TraceJoin
{
  /// <summary>The kind of value a <see cref="TraceArgument"/> holds.</summary>
  public enum TraceArgumentKind
  {
    Whole,
    Number,
    Text,
  }
}
