//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The two questions the playback page asks about a recording before it writes a video.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts.Playback
{
  public enum PlaybackQuestionKind
  {
    /// <summary>Browsers play the recording: copy it into the playback folder (yes), or link it where it is (no)?</summary>
    CopyOrLink,

    /// <summary>Browsers cannot play the recording: make a playable copy with ffmpeg (yes), or link it as it is (no)?</summary>
    Transcode,
  }
}
