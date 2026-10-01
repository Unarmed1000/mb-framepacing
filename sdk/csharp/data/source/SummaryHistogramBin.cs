//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One bin of a summary.json histogram.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.Text.Json.Serialization;

namespace MB.FramePacing.Data
{
  public sealed record SummaryHistogramBin([property: JsonRequired] double CenterMs, [property: JsonRequired] long Count);
}
