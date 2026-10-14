//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The tracks' names and descriptions as they are written into a trace: tools/trace_events.py's, word for word (the two write the same
//* bytes). The names say what the capture's data says and no more (sdk/doc/vocabulary.md has the terms).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>The names and descriptions of the <see cref="TraceTrack"/>s.</summary>
  public static class TraceTrackText
  {
    /// <summary>Every track, in the order they are shown.</summary>
    public static IReadOnlyList<TraceTrack> All { get; } = Enum.GetValues<TraceTrack>();

    /// <summary>The track's name in the trace.</summary>
    public static string NameOf(TraceTrack track) =>
      track switch
      {
        TraceTrack.Refreshes => "Refreshes (captured)",
        TraceTrack.OnScreen => "On screen (measured)",
        TraceTrack.Intended => "Intended display (the pacer's)",
        TraceTrack.Cpu => "CPU (from the markers)",
        TraceTrack.AnimationError => "Animation error (ms)",
        TraceTrack.DisplayStep => "Display time step (ms)",
        TraceTrack.Lateness => "Lateness (ms)",
        TraceTrack.FrameEvents => "Frame events",
        TraceTrack.CaptureEvents => "Capture events",
        _ => throw new ArgumentOutOfRangeException(nameof(track)),
      };

    public static string DescriptionOf(TraceTrack track) =>
      track switch
      {
        TraceTrack.Refreshes =>
          "Each capture of the display for a refresh, named a and b in turn; a capture that was not decoded is named so, and a gap is a refresh not captured. A capture card's captures only.",
        TraceTrack.OnScreen =>
          "Each frame from the moment a capture of the display first showed it for as long as it stayed. Measured by mb-framepacing. "
            + "Coloured by its verdict, one colour each (the viewer picks the colours): late, delta time jitter, off (beyond the error threshold "
            + "otherwise), static, unknown (a capture gap), on time.",
        TraceTrack.Intended => "When the application's frame pacer aimed for each frame to be shown, as its marker carried it.",
        TraceTrack.Cpu => "Each frame's CPU start time and CPU busy time, as its marker carried them.",
        TraceTrack.AnimationError =>
          "Animation time step minus display time step, per frame. Zero is a frame shown for exactly as long as it animated for.",
        TraceTrack.DisplayStep => "How long the frame before stayed on screen: the time from its display time to this frame's.",
        TraceTrack.Lateness => "How long after its intended display time a frame was shown, by the run's on-time frames.",
        TraceTrack.FrameEvents =>
          "What the capture found about the frames: late ones, frame indices that were skipped, frames out of order, torn frames.",
        TraceTrack.CaptureEvents => "Where the capture itself has gaps: captures that were not decoded, dropped by the source or missed.",
        _ => throw new ArgumentOutOfRangeException(nameof(track)),
      };

    /// <summary>True for the tracks that hold a value per frame.</summary>
    public static bool IsCounter(TraceTrack track) => track is TraceTrack.AnimationError or TraceTrack.DisplayStep or TraceTrack.Lateness;
  }
}
