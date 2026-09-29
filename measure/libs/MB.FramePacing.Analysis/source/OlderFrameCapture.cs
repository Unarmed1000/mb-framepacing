//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A capture that showed an older frame out of order: after a newer frame was presented, the display showed an earlier frame index again
//* (frames presented in another order than they were rendered). It belongs to the presented frame that was the newest at the time.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  /// <param name="CaptureTicks">When the capture was taken (the analysis's capture clock, as FirstSeenTicks).</param>
  /// <param name="FrameIndex">The older frame index it showed.</param>
  public readonly record struct OlderFrameCapture(long CaptureTicks, ulong FrameIndex);
}
