//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The kinds of source the Capture page offers: a video file, an image folder, and with the experimental features a capture card, a network
//* stream (live capture) or the synthetic camera.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Gui.ViewModels
{
  public enum SourceKind
  {
    /// <summary>EXPERIMENTAL (live capture): a capture card found by ffmpeg.</summary>
    Device,
    VideoFile,
    ImageFolder,

    /// <summary>EXPERIMENTAL (live capture): a network stream.</summary>
    Stream,

    /// <summary>EXPERIMENTAL: the synthetic game filmed by a simulated high speed camera (needs a calibrated camera rig).</summary>
    SyntheticCamera,
  }
}
