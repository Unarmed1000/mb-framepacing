//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a frame was aimed at, as its markers say it, for the charts' reference lines: its target frame time (the pacer's aim now) and its
//* preferred frame time (what the application wants), each in the analysis's order of fallbacks (the marker's own value, else the
//* preferred frame time, else the target given to the tools or one refresh), without the schedule's step or the frames dropped before it,
//* which make a line jump after every drop or late frame. Null when the frame is presented on demand: there is no interval to aim for.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using MB.FramePacing.Analysis;
using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Charts
{
  public static class FrameReference
  {
    /// <summary>The target frame time before <paramref name="frame"/>, as written (ticks): the marker's, else its preferred frame time.</summary>
    public static long? Target(PresentedFrame frame) =>
      frame.MarkerTargetFrameTime.Ticks switch
      {
        uint.MaxValue => null, // MarkerPayload.OnDemandFrameTime
        0 => Preferred(frame),
        var ticks => ticks,
      };

    /// <summary>
    /// The preferred frame time of <paramref name="frame"/>, as written (ticks): the marker's, else what the analysis took (the target given
    /// to the tools, else one refresh).
    /// </summary>
    public static long? Preferred(PresentedFrame frame) =>
      frame.MarkerPreferredFrameTime.Ticks switch
      {
        uint.MaxValue => null, // MarkerPayload.OnDemandFrameTime
        0 => frame.PreferredFrameTime?.Ticks,
        var ticks => ticks,
      };
  }
}
