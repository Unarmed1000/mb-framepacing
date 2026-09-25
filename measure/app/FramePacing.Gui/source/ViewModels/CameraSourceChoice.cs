//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the camera rig wizard films: a source from the capture page list, with its clip path, live mode and recorded fps.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Gui.ViewModels
{
  /// <summary>What the camera rig wizard films: a source from the capture page list, with its clip path, live mode and recorded fps.</summary>
  /// <param name="MediaPath">The clip, image folder or stream (media sources only).</param>
  /// <param name="ModeText">The live device mode (devices only, empty = device default).</param>
  /// <param name="RecordedFps">The real recording rate of a slow motion video file, if not its own timestamps.</param>
  public sealed record CameraSourceChoice(DeviceItem Source, string MediaPath, string ModeText, string InputFormat, double? RecordedFps);
}
