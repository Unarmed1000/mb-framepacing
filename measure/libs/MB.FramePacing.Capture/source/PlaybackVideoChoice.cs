//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a playback page does with a recording browsers can play: ask, copy it into the page's folder, or link it where it is (the
//* configuration's playbackVideo, the command line's --playback-video).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture
{
  public enum PlaybackVideoChoice
  {
    /// <summary>Ask every time (the default).</summary>
    Ask,

    /// <summary>Copy the recording into the playback folder: the folder plays anywhere on its own.</summary>
    Copy,

    /// <summary>Link the recording where it is: no extra disk space, but the folder needs the recording.</summary>
    Link,
  }
}
