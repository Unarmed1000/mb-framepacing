//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The video stream format ffmpeg reports on stderr: size and frame rate.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture.Ffmpeg
{
  public readonly record struct VideoStreamInfo(int Width, int Height, double Fps);
}
