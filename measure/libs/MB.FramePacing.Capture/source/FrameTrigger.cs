//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a frame inspector found in one frame: nothing that changes the recording, a start marker or an end marker.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture
{
  public enum FrameTrigger
  {
    None,

    /// <summary>The start marker of a run: a recorder waiting for the start begins writing (with its pre-roll).</summary>
    Start,

    /// <summary>The end marker of the run: a recorder that stops at the end writes its end tail and stops.</summary>
    End,
  }
}
