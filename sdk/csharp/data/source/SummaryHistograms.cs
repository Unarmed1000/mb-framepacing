//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* summary.json's runs[].histograms: the animation error and the display time step, in fixed bins.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.Text.Json.Serialization;

namespace MB.FramePacing.Data
{
  public sealed record SummaryHistograms(
    [property: JsonRequired] SummaryHistogram AnimationErrorMs,
    [property: JsonRequired] SummaryHistogram DisplayDeltaMs
  );
}
