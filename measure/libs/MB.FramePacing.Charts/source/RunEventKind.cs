//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What went wrong at a moment of a run, in two groups that are never mixed up: what the frames did on the display (dropped by the target,
//* an older frame shown out of order, a torn refresh), and what the capture missed (a refresh not recorded, dropped by the source, missed
//* without a word, or not decoded).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  public enum RunEventKind
  {
    /// <summary>Frames the target rendered that never reached the display (RunChartData.DroppedBeforeFrame), where they were due.</summary>
    FramesDropped,

    /// <summary>A refresh that showed an older frame again after a newer one (PresentedFrame.OlderFrames).</summary>
    OutOfOrder,

    /// <summary>A refresh that showed two frames: a capture card's torn capture (its markers disagree), a camera's torn frame.</summary>
    Torn,

    /// <summary>A capture the recorder dropped (captures.csv NotRecorded), at the time it would have had.</summary>
    NotRecorded,

    /// <summary>Frames the capture source reported dropping (captures.csv sourceDropsBefore), just before the capture it reported them with.</summary>
    SourceDropped,

    /// <summary>Refreshes the device clock says the capture missed without a word (captures.csv missedBefore).</summary>
    Missed,

    /// <summary>A capture card's capture whose marker could not be read (captures.csv Undecodable).</summary>
    NotDecoded,
  }
}
