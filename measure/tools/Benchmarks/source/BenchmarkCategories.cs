//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The benchmarks' categories. A long-running benchmark (runs of an hour or ten, many cases) is left out of a run unless it asks for it with
//* --long-running (Program).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Benchmarks
{
  internal static class BenchmarkCategories
  {
    /// <summary>Takes many minutes: run only with --long-running.</summary>
    public const string LongRunning = "LongRunning";
  }
}
