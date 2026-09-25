//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The pages of the camera rig wizard (VERY EXPERIMENTAL camera support).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Gui.ViewModels
{
  public enum CameraWizardStep
  {
    /// <summary>Use a saved camera or set up a new one.</summary>
    Choose,

    /// <summary>New camera: mount it and show the markers.</summary>
    Mount,

    /// <summary>What to film: a live camera, a clip filmed with it, or the synthetic camera.</summary>
    Source,

    /// <summary>New camera: calibrate and check the setup.</summary>
    Calibrate,

    /// <summary>New camera: save it under a name.</summary>
    Save,

    /// <summary>Saved camera: check it has not moved.</summary>
    Verify,

    Done,
  }
}
