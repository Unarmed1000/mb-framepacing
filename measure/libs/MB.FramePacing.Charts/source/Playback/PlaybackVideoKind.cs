//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Which video a playback report holds in its folder: it never links anything outside it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts.Playback
{
  public enum PlaybackVideoKind
  {
    /// <summary>A copy of the recording, which browsers play as it is.</summary>
    Copied,

    /// <summary>A copy browsers can play (remuxed or transcoded by ffmpeg) of a recording they cannot.</summary>
    Transcoded,

    /// <summary>No video: browsers cannot play the recording and no playable copy was made. The page can open a file by hand.</summary>
    None,
  }
}
