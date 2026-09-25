//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How one capture relates to the display's scanout.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  public enum ScanoutModel
  {
    /// <summary>A capture card: every capture holds one whole scanout, so markers that disagree mean tearing (vsync off).</summary>
    SingleScanout,

    /// <summary>
    /// EXPERIMENTAL: a camera filming the screen. The scanout rolls down the screen while the camera exposes, so the lower zone showing an older
    /// frame than the timing zone is normal; a frame that reaches the lower zone first was presented mid-scanout (a tear).
    /// </summary>
    Camera,
  }
}
