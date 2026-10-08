//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One event of a run (RunEventKind): when, how many (frames dropped, refreshes missed), and the frames it names, when it names any.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  /// <param name="Time">When, on the capture's clock (PresentedFrame.FirstSeenTime's).</param>
  /// <param name="Count">How many frames or refreshes it stands for (1 for a single capture).</param>
  /// <param name="FrameIndex">
  /// The frame it names: the older frame shown out of order, a torn capture's main marker, the frame after dropped frames.
  /// </param>
  /// <param name="OtherFrameIndex">A torn capture's sync marker: the other frame of the refresh.</param>
  public readonly record struct RunEvent(NanosecondTickCount Time, long Count, ulong? FrameIndex = null, ulong? OtherFrameIndex = null);
}
