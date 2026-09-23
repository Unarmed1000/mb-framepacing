//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Resolves device timestamps that arrive after the pixels (for example ffmpeg's showinfo lines on stderr).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture
{
  /// <summary>Resolves device timestamps that arrive after the pixels (for example ffmpeg's showinfo lines on stderr).</summary>
  public interface IDeviceTimestampSource
  {
    bool TryGetDeviceTicks(long captureIndex, out long deviceTicks);
  }
}
