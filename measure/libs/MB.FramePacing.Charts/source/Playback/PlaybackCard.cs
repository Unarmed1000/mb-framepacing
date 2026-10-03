//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One of a playback page's report cards: the whole report, or a zoom step whose plots show a few seconds and scroll with the playhead (the
//* rest of the report in the card's scrolling layers).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts.Playback
{
  /// <param name="SecondsPerScreen">The seconds the plots show at a time; null for the whole report on one screen.</param>
  /// <param name="Drawing">The card, its plots showing the report's first <paramref name="SecondsPerScreen"/> seconds.</param>
  public sealed record PlaybackCard(double? SecondsPerScreen, CardDrawing Drawing);
}
