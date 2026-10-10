//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A capture's clocks against a system clock (tools/trace_alignment.py's Alignment). A time on the capture's clock less CaptureMinusAppNs
//* is on the application's clock; a time on the application's clock plus AppClockOffsetNs is on the system clock the application's clock
//* is. VBlankShiftNs is taken off display times only (VBlankFit).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. Where a capture's times are on the system clock an application's clock is, in whole nanoseconds.</summary>
  public sealed record TraceAlignment(TraceAnchor Anchor, long CaptureMinusAppNs, long AppClockOffsetNs = 0, long VBlankShiftNs = 0)
  {
    /// <summary>A time the markers carry (CPU start, intended display), on the system clock.</summary>
    public long MarkerTime(long appNs) => appNs + AppClockOffsetNs;

    /// <summary>A time on the capture's clock (a capture, an event of the capture), on the system clock.</summary>
    public long CaptureTime(long captureNs) => captureNs - CaptureMinusAppNs + AppClockOffsetNs;

    /// <summary>A frame's display time on the system clock: its time on the capture's clock, moved onto the vertical blanks.</summary>
    public long DisplayTime(long firstSeenNs) => CaptureTime(firstSeenNs) - VBlankShiftNs;
  }
}
