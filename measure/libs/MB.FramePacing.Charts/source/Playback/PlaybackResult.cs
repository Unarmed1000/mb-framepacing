//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a playback export wrote: a report per run (or section of one), each in its own folder, and the video they play.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Charts.Playback
{
  /// <param name="Pages">The pages written (each report folder's index.html), a run or a section of one each.</param>
  /// <param name="Video">The video they play (each report folder has its own copy, unless the user named a video).</param>
  /// <param name="Warnings">What the export noticed without stopping (a named video shorter than the capture); empty when nothing.</param>
  public sealed record PlaybackResult(IReadOnlyList<string> Pages, PlaybackVideo Video, IReadOnlyList<string> Warnings);
}
