//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One image of an image sequence and the time it was captured.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture.Ffmpeg
{
  public sealed record ImageSequenceFrame(string Path, long TimeTicks);
}
