//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One camera rig check as the capture page lists it (EXPERIMENTAL camera support).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using MB.FramePacing.Capture.Camera;

namespace MB.FramePacing.Gui.ViewModels
{
  /// <summary>One camera rig check as the capture page lists it.</summary>
  public sealed record CameraCheckItem(CameraCheck Check)
  {
    public string Text => $"{Check.Level.ToString().ToUpperInvariant()}  {Check.Name}: {Check.Message}";

    public bool IsPass => Check.Level == CameraCheckLevel.Pass;

    public bool IsWarn => Check.Level == CameraCheckLevel.Warn;

    public bool IsFail => Check.Level == CameraCheckLevel.Fail;
  }
}
