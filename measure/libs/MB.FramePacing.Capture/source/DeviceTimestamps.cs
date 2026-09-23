//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Special device timestamp values shared by the capture sources and the recorder.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture
{
  public static class DeviceTimestamps
  {
    /// <summary>Passed to <see cref="IFrameSink.EndFrame"/> when the device timestamp will be supplied later.</summary>
    public const long PendingTicks = long.MinValue + 1;
  }
}
