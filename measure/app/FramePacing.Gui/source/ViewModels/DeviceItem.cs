//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* An entry of the source list. Device is only set for capture cards.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using MB.FramePacing.Capture.Ffmpeg;

namespace MB.FramePacing.Gui.ViewModels
{
  /// <summary>An entry of the source list. <see cref="Device"/> is only set for capture cards.</summary>
  public sealed record DeviceItem(string Title, CaptureDevice? Device, SourceKind Kind = SourceKind.Device)
  {
    public bool IsSynthetic => Kind == SourceKind.Synthetic;

    public override string ToString() => Title;
  }
}
