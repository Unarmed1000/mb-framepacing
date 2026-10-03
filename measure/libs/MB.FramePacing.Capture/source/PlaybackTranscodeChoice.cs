//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a playback report does with a recording browsers cannot play: ask, make a playable copy with ffmpeg, or have no video (the
//* configuration's playbackTranscode, the command line's --playback-transcode).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture
{
  public enum PlaybackTranscodeChoice
  {
    /// <summary>Ask every time (the default).</summary>
    Ask,

    /// <summary>Make a playable copy in the report's folder (every frame and its timestamp kept).</summary>
    Yes,

    /// <summary>No video: the report says so and lets the viewer open a file of the recording.</summary>
    No,
  }
}
