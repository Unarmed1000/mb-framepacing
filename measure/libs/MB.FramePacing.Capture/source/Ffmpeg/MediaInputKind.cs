//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What an import input is: a video file, a folder of images or a network stream.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture.Ffmpeg
{
  public enum MediaInputKind
  {
    VideoFile,
    ImageSequence,
    Stream,
  }
}
