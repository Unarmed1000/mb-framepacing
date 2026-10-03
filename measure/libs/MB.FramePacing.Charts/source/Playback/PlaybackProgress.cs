//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How far a playback export is: the step it is on and, while it copies a video, the share done.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts.Playback
{
  /// <param name="Step">What it does ("Copying the recording").</param>
  /// <param name="Fraction">The share of the step done (0 to 1), null when not known.</param>
  public readonly record struct PlaybackProgress(string Step, double? Fraction);
}
