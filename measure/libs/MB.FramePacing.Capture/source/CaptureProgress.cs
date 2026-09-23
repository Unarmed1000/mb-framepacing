//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Live progress of a capture run (phase, elapsed time, recorder statistics, drops and the last marker seen), reported to the command line and
//* the GUI.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture
{
  public readonly record struct CaptureProgress(
    CapturePhase Phase,
    TimeSpan Elapsed,
    FrameRecorderStats Recorder,
    long SourceDroppedFrames,
    MarkerDecodeResult? LastMarker
  )
  {
    public double CapturedFps(TimeSpan window) => window > TimeSpan.Zero ? Recorder.FramesCaptured / window.TotalSeconds : 0;
  }
}
