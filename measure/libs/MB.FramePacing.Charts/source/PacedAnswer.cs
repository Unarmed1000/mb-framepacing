//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Whether a pacer or a frame limiter held a section's frames, as the markers' paced flag says it (RunHeadline.Pacing): the application's word,
//* never worked out from the other pacing fields.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  public enum PacedAnswer
  {
    /// <summary>The run's markers carry the paced flag, and some of the section's frames have it.</summary>
    MarkersSayPaced,

    /// <summary>The run's markers carry the paced flag, and none of the section's frames has it: the application says they were not held.</summary>
    MarkersSayNotPaced,

    /// <summary>No frame of the run carries the paced flag: the markers do not say.</summary>
    NotSaid,
  }
}
