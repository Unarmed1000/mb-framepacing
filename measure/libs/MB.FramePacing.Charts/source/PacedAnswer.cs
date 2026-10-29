//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Whether a pacer or a frame limiter held a section's frames, as the markers' paced flag says it (RunHeadline.Pacing): a frame without the flag
//* was not held (the user, 2026-10-29: a run without the bit is not paced), never worked out from the other pacing fields.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  public enum PacedAnswer
  {
    /// <summary>Some of the section's frames carry the paced flag: all of them, or the share that does.</summary>
    Paced,

    /// <summary>None of the section's frames carries the paced flag: no pacer or limiter held them.</summary>
    NotPaced,
  }
}
