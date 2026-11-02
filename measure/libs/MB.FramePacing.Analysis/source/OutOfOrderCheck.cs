//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Frames that came back on screen: a display shows a newer frame and then an older one (PresentedFrame.OlderFrames). A warning with the facts: a
//* display should never do that, and it does not say where it happened, since a capture chain can reorder frames as well as a compositor.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Analysis
{
  public static class OutOfOrderCheck
  {
    /// <summary>The warning for <paramref name="frames"/> as its lines (the facts, then what to check), or none when no older frame came back.</summary>
    public static IReadOnlyList<string> Lines(IReadOnlyList<PresentedFrame> frames)
    {
      int captures = frames.Sum(f => f.OlderFrames?.Count ?? 0);
      if (captures == 0)
        return Array.Empty<string>();
      return new[]
      {
        $"An older frame came back on screen after a newer one {captures} time(s).",
        "A display should never do this: check the compositor and the capture chain.",
      };
    }

    /// <summary>The warning for <paramref name="frames"/> as one text, or null (<see cref="Lines"/>).</summary>
    public static string? Warning(IReadOnlyList<PresentedFrame> frames) => Lines(frames) is { Count: > 0 } lines ? string.Join(" ", lines) : null;
  }
}
