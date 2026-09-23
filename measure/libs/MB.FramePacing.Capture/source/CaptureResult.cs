//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The outcome of a capture run: where it was written, the session info and the recorder statistics.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture
{
  public sealed record CaptureResult(string Directory, string FramesPath, CaptureSessionInfo Session);
}
