//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Which video a playback folder's pages play: the recording where it is, or a file of the folder's own.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts.Playback
{
  public enum PlaybackVideoKind
  {
    /// <summary>The recording itself, linked where it is.</summary>
    Linked,

    /// <summary>A copy of the recording (browsers play it as it is) in the folder.</summary>
    Copied,

    /// <summary>A copy browsers can play (remuxed or transcoded by ffmpeg) in the folder.</summary>
    Transcoded,
  }
}
