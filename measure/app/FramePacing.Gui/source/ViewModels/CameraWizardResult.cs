//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the camera rig wizard set up: the saved camera to capture with and the source it films.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Gui.ViewModels
{
  /// <summary>What the camera rig wizard set up: the saved camera to capture with and the source it films.</summary>
  public sealed record CameraWizardResult(string RigName, CameraSourceChoice Source);
}
