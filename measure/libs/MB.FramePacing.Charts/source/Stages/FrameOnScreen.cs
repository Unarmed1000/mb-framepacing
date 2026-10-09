//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A frame's time on screen as far as a frame log says: from its display time to End. Where frames without a display time were presented
//* before the next frame that has one, End is the end of the frame's own swap interval, and from there to UnknownEnd the log does not say
//* which of them the display showed.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Charts.Stages
{
  /// <summary>Experimental. A frame's time on screen as far as its log says, in the log's ticks.</summary>
  /// <param name="Unreported">The frames without a display time that were presented before the next frame that has one.</param>
  public sealed record FrameOnScreen(long Shown, long End, long? UnknownEnd, IReadOnlyList<FrameLogFrame> Unreported);
}
