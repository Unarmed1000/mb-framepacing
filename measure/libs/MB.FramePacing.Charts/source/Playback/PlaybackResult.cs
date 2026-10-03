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
  /// <param name="Video">The video they play (each report folder has its own when it is not the recording itself).</param>
  public sealed record PlaybackResult(IReadOnlyList<string> Pages, PlaybackVideo Video);
}
