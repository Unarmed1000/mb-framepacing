//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What FrameMarkerOverlay currently draws: frame markers outside a run, the start marker, the run's frame markers or the end marker.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

#if UNITY_2021_3_OR_NEWER
namespace MB.FramePacing.Marker.Unity
{
  public enum MarkerPhase
  {
    /// <summary>No run: frame markers with run id 0 (when the overlay draws while idle).</summary>
    Idle,

    /// <summary>The start marker, shown long enough for the capture to see it.</summary>
    Start,

    /// <summary>A run is being measured: frame markers with the run's id.</summary>
    Running,

    /// <summary>The end marker, shown long enough for the capture to see it.</summary>
    End,
  }
}
#endif
