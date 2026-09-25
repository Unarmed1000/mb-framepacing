//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The kinds of source the Capture page offers: a capture card, a video file, an image folder, a network stream or the synthetic test game.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Gui.ViewModels
{
  public enum SourceKind
  {
    /// <summary>A capture card found by ffmpeg.</summary>
    Device,
    VideoFile,
    ImageFolder,
    Stream,
    Synthetic,

    /// <summary>EXPERIMENTAL: the synthetic game filmed by a simulated high speed camera (needs a calibrated camera rig).</summary>
    SyntheticCamera,
  }
}
