//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A row of a frame log (FrameLog): its frame's number, and every whole number the row has, by column. A moment of the frame is a time on
//* the application's CPU clock, in the log's own ticks of 100 ns: the log is the sample's file, and its times stay as it wrote them.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Charts.Stages
{
  /// <summary>Experimental. One frame of a frame log.</summary>
  public sealed record FrameLogFrame(long Index, IReadOnlyDictionary<string, long> Values)
  {
    /// <summary>The value of a column; null when the row has none.</summary>
    public long? At(string name) => Values.TryGetValue(name, out long value) ? value : null;

    /// <summary>A stage of the frame, when the log has both of its ends in order.</summary>
    public (long First, long Last)? Span(string begin, string end) =>
      At(begin) is { } first && At(end) is { } last && last >= first ? (first, last) : null;

    /// <summary>The frame's start: every frame of a log that was read has one.</summary>
    public long Start => Values["frameStartTicks"];

    /// <summary>When the driver reported the frame's first pixel going out.</summary>
    public long? Shown => At("firstPixelOutTicks") ?? At("feedbackDisplayTicks");

    /// <summary>The swap interval the frame was presented with: 1 where the log does not say.</summary>
    public long SwapInterval => At("swapInterval") is { } interval and not 0 ? interval : 1;
  }
}
