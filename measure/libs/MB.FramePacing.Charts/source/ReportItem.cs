//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The items of a report card, each with a stable id that switches it on or off (ReportOptions, 'render --hide / --only'): the title, the
//* description, the display box, every headline tile (or all of them), and every panel.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
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
    public const string AverageFps = "average-fps";
    public const string OnePercentLow = "one-percent-low";
    public const string PointOnePercentLow = "point-one-percent-low";
    public const string FramesOff = "frames-off";
    public const string ErrorP99 = "error-p99";
    public const string ErrorP999 = "error-p999";
    public const string WorstError = "worst-error";
    public const string LateFrames = "late-frames";
    public const string AnimationError = "animation-error";
    public const string DisplayTimeStep = "display-time-step";
    public const string FrameTime = "frametime";
    public const string LateShare = "late-share";
    public const string RefreshStrip = "refresh-strip";

    /// <summary>Every item, in the order the card shows them, with what it is.</summary>
    public static readonly IReadOnlyList<(string Id, string Description)> All = new[]
    {
      (Title, "the run's title"),
      (Description, "the lines under the title"),
      (Display, "the display box (refresh rate, time per refresh, target)"),
      (Tiles, "every headline tile"),
      (AverageFps, "tile: average fps (and the presented frames)"),
      (OnePercentLow, "tile: 1 % low"),
      (PointOnePercentLow, "tile: 0.1 % low"),
      (FramesOff, "tile: frames visibly off"),
      (ErrorP99, "tile: animation error p99"),
      (ErrorP999, "tile: animation error p99.9"),
      (WorstError, "tile: worst error"),
      (LateFrames, "tile: late frames"),
      (AnimationError, "panel: animation error per frame"),
      (DisplayTimeStep, "panel: display time step"),
      (FrameTime, "panel: frametime and CPU busy (from the markers)"),
      (LateShare, "panel: share of late frames in the last 2 s"),
      (RefreshStrip, "panel: refresh strip"),
    };

    /// <summary>The ids of the headline tiles, which <see cref="Tiles"/> switches together.</summary>
    public static readonly IReadOnlyList<string> TileIds = new[]
    {
      AverageFps,
      OnePercentLow,
      PointOnePercentLow,
      FramesOff,
      ErrorP99,
      ErrorP999,
      WorstError,
      LateFrames,
    };

    public static bool IsKnown(string id) => All.Any(item => item.Id == id);
  }
}
