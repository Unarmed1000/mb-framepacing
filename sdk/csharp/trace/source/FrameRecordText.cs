//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The strings of the frame record: the slice an application logs to a
//* trace for each frame, so that a trace and a capture of the same run can be merged. The document is the reference; the C++ module has
//* the same strings under the same names.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Trace
{
  /// <summary>The strings of the frame record.</summary>
  public static class FrameRecordText
  {
    /// <summary>The category the slices are logged in.</summary>
    public const string Category = "mbfp";

    /// <summary>The name of the track the slices are on, under the application's process.</summary>
    public const string TrackName = "mbfp frames";

    /// <summary>What a slice's name begins with: the run id and the frame index follow, as decimal numbers with a space between.</summary>
    public const string NamePrefix = "mbfp frame ";

    /// <summary>The annotation with the marker's animation time, in nanoseconds: on every slice.</summary>
    public const string AnimationNs = "animationNs";

    /// <summary>The annotation with the marker's preferred frame time, in nanoseconds: left out when the marker's is 0.</summary>
    public const string MarkerPreferredNs = "markerPreferredNs";

    /// <summary>The annotation with the marker's target frame time, in nanoseconds: left out when the marker's is 0.</summary>
    public const string MarkerTargetNs = "markerTargetNs";

    /// <summary>The annotation with the marker's intended display time, in nanoseconds: left out when the marker's is 0.</summary>
    public const string IntendedDisplayNs = "intendedDisplayNs";

    /// <summary>The clock a slice's times are on: the system's CLOCK_MONOTONIC, by the number Perfetto gives its built-in clocks.</summary>
    public const int ClockId = 3;
  }
}
