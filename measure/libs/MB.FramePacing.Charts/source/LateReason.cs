//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Why a late frame was late, from the markers' CPU start time and CPU busy (PresentTimings): its present came late too, through its CPU work
//* or its start, or it came on time and the frame was held after it. Steps on the application's clock, so the clocks' offset cancels out.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  public enum LateReason
  {
    /// <summary>Its present came half a refresh or more after its intended period from the frame before's, and its CPU work alone took that long.</summary>
    SubmittedLateWork,

    /// <summary>Its present came half a refresh or more after its intended period from the frame before's, and its CPU work did not take that long: it started late.</summary>
    SubmittedLateStart,

    /// <summary>Its present came on time: the frame was held after it (GPU, queue, compositor or display; the capture can not tell which).</summary>
    ShownLateAfterPresent,
  }
}
