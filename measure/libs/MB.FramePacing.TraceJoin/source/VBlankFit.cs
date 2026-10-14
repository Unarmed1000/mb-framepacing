//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How the measured display times sit on a trace's vertical blanks.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.TraceJoin
{
  /// <summary>
  /// <paramref name="ShiftNs"/> was taken off every display time (it is within half a refresh either way); afterwards the
  /// display times of <paramref name="UsedFrames"/> frames have a vertical blank within half a refresh, and the furthest of them is
  /// <paramref name="SpreadNs"/> from its vertical blank.
  /// </summary>
  public readonly record struct VBlankFit(long ShiftNs, int UsedFrames, long SpreadNs);
}
