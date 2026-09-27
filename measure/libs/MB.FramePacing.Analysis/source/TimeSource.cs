//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Which capture clock the analysis uses: device timestamps, host timestamps, or device when every record has one.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  public enum TimeSource
  {
    /// <summary>Device timestamps when every record has one, otherwise host timestamps.</summary>
    Auto,
    Device,
    Host,
  }
}
