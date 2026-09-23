//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The video stream format ffmpeg reports on stderr: size and frame rate.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture.Ffmpeg
{
  public readonly record struct VideoStreamInfo(int Width, int Height, double Fps);
}
