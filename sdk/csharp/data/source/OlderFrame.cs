//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A capture that showed an older frame out of order, after the frame of its row was presented (frames CSV column olderFrames).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Data
{
  /// <param name="FrameIndex">The older frame index the capture showed.</param>
  /// <param name="CaptureTime">When the capture was taken, on the analysis's capture clock (as FrameRow.FirstSeenTime).</param>
  public readonly record struct OlderFrame(ulong FrameIndex, NanosecondTickCount CaptureTime);
}
