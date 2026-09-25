//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One entry of the camera rig library (EXPERIMENTAL camera support): a calibrated camera saved under a name.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture.Camera
{
  /// <summary>One entry of the camera rig library: a calibrated camera saved under a name.</summary>
  /// <param name="Rig">The loaded rig, or null when the file could not be read (see <paramref name="Error"/>).</param>
  public sealed record SavedCameraRig(string Name, string Path, CameraRig? Rig, string? Error = null)
  {
    public override string ToString() => Name;
  }
}
