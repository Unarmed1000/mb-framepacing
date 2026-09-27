//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Which cause dominates a run's animation error: uneven display (bad pacing) or uneven animation steps on an even display (delta time jitter).
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  public enum PacingVerdict
  {
    /// <summary>No frame has an animation error above the threshold.</summary>
    None,

    /// <summary>At least two thirds of the error frames are at uneven display: frames shown late or early, or after skipped frames.</summary>
    BadPacing,

    /// <summary>At least two thirds of the error frames are on an even display: the animation time steps are uneven.</summary>
    DeltaTimeJitter,

    /// <summary>Both causes contribute.</summary>
    Both,
  }
}
