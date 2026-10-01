//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What one run of the headless browser gave (HeadlessBrowser): whether the PNG was saved, the browser's exit code (none when it was ended
//* for taking too long) and what it wrote to its error output.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  internal readonly record struct BrowserAttempt(bool Saved, int? ExitCode, string Errors)
  {
    /// <summary>The browser was ended because it had not saved the PNG in time.</summary>
    public bool TimedOut => !Saved && !ExitCode.HasValue;
  }
}
