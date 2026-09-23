//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Which capture clock the analysis uses: device timestamps, host timestamps, or device when every record has one.
//*
//* (c) 2026 Mana Battery
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
