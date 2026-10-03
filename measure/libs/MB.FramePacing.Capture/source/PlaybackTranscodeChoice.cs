//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a playback page does with a recording browsers cannot play: ask, make a playable copy with ffmpeg, or link the recording as it is
//* (the configuration's playbackTranscode, the command line's --playback-transcode).
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

    /// <summary>Make a playable copy in the playback folder (every frame and its timestamp kept).</summary>
    Yes,

    /// <summary>Link the recording as it is: the page says the browser cannot play it and lets the viewer pick another file.</summary>
    No,
  }
}
