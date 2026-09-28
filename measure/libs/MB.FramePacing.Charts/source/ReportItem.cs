//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The items of a report card, each with a stable id that switches it on or off (ReportOptions, 'render --hide / --only'): the title, the
//* description, the display box, every headline tile (or all of them), and every panel.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Charts
{
  public static class ReportItem
  {
    public const string Title = "title";
    public const string Description = "description";
    public const string Display = "display";
    public const string Tiles = "tiles";
    public const string PresentedFrames = "presented-frames";
    public const string FramesOff = "frames-off";
    public const string ErrorPerFrame = "error-per-frame";
    public const string TypicalError = "typical-error";
    public const string WorstError = "worst-error";
    public const string LateFrames = "late-frames";
    public const string WorstLate = "worst-late";
    public const string Resolution = "resolution";
    public const string AnimationError = "animation-error";
    public const string DisplayTimeStep = "display-time-step";
    public const string LateShare = "late-share";
    public const string RefreshStrip = "refresh-strip";

    /// <summary>Every item, in the order the card shows them, with what it is.</summary>
    public static readonly IReadOnlyList<(string Id, string Description)> All = new[]
    {
      (Title, "the run's title"),
      (Description, "the lines under the title"),
      (Display, "the display box (refresh rate, time per refresh, target)"),
      (Tiles, "every headline tile"),
      (PresentedFrames, "tile: presented frames"),
      (FramesOff, "tile: frames visibly off"),
      (ErrorPerFrame, "tile: error per frame and percent error"),
      (TypicalError, "tile: typical error (p95)"),
      (WorstError, "tile: worst error"),
      (LateFrames, "tile: late frames"),
      (WorstLate, "tile: worst 2 s late"),
      (Resolution, "tile: resolution"),
      (AnimationError, "panel: animation error per frame"),
      (DisplayTimeStep, "panel: display time step"),
      (LateShare, "panel: share of late frames in the last 2 s"),
      (RefreshStrip, "panel: refresh strip"),
    };

    /// <summary>The ids of the headline tiles, which <see cref="Tiles"/> switches together.</summary>
    public static readonly IReadOnlyList<string> TileIds = new[]
    {
      PresentedFrames,
      FramesOff,
      ErrorPerFrame,
      TypicalError,
      WorstError,
      LateFrames,
      WorstLate,
      Resolution,
    };

    public static bool IsKnown(string id) => All.Any(item => item.Id == id);
  }
}
