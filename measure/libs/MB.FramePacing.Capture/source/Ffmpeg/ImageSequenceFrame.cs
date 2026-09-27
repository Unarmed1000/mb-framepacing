//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One image of an image sequence and the time it was captured.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture.Ffmpeg
{
  public sealed record ImageSequenceFrame(string Path, long TimeTicks);
}
