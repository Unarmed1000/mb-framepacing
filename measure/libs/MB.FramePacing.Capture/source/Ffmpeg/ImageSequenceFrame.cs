//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One image of an image sequence and the time it was captured.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture.Ffmpeg
{
  public sealed record ImageSequenceFrame(string Path, TickCount64 Time);
}
