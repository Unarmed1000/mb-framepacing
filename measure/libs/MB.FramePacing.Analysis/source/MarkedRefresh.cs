//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A refresh period the frames' markers carry (the application's word about the display it works with), and how many frames carry it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  /// <param name="Period">The refresh period the markers carry, never 0 (unknown).</param>
  /// <param name="Frames">How many frames carry it.</param>
  public readonly record struct MarkedRefresh(NanosecondTimeDuration Period, int Frames)
  {
    /// <summary>The period's rate in Hz.</summary>
    public double Hz => RefreshAgreement.RateOf(Period);
  }
}
