//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a hold on the display time step panel shows: a frame on screen until the next one, at the next frame's display time step.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  public enum HoldKind
  {
    /// <summary>The next frame came as planned (no late verdict, nothing lost).</summary>
    AsPlanned,

    /// <summary>
    /// The next frame was late: this frame stayed on screen half a refresh or more longer than the next one was animated for and than its
    /// intended period.
    /// </summary>
    Late,

    /// <summary>While this frame was the newest, the display showed an older frame again (frames presented out of order).</summary>
    OlderFrameBack,

    /// <summary>
    /// The frame just before the next one was rendered and not presented: its present failed, as the next frame's marker says. Frames the
    /// target dropped may come before it too.
    /// </summary>
    PresentFailed,

    /// <summary>Frames the target rendered after this one never reached the display before the next (captured without a gap).</summary>
    FramesDropped,

    /// <summary>A capture gap made the next frame's display time step uncertain: not judged.</summary>
    Unknown,
  }
}
