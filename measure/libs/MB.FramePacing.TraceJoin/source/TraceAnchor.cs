//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What placed a capture's clock against the application's.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. What placed the capture's clock against the application's.</summary>
  public enum TraceAnchor
  {
    /// <summary>The pacer's intended display times: the analysis's own alignment.</summary>
    Schedule,

    /// <summary>The CPU start and CPU busy times: no frame is on screen before its CPU work ended.</summary>
    CpuWork,

    /// <summary>The offset that was given.</summary>
    Given,

    /// <summary>Nothing: the markers carry no times.</summary>
    Nothing,
  }
}
