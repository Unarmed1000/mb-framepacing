//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A frame as the application itself named it in a trace: a trace section around the frame's CPU work, named
//* "mbfp frame <run id> <frame index>" with the numbers its marker carries. It is the one thing a trace and a capture can share: with
//* it a frame of the trace is known by its marker's frame index, not by its time.
//*
//* An application writes it through its platform's trace API: begun where it reads the clock for the marker's CPU start time, ended at
//* the moment that ends the marker's CPU busy time, on the thread that makes the frame.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Globalization;

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. A frame's own trace section, with its marker's run id and frame index; times on the trace's clock.</summary>
  public readonly record struct FrameSection(uint RunId, ulong FrameIndex, long StartNs, long EndNs)
  {
    /// <summary>What every such section's name begins with.</summary>
    public const string NamePrefix = "mbfp frame ";

    /// <summary>The name of a frame's section.</summary>
    public static string NameOf(uint runId, ulong frameIndex) =>
      NamePrefix + runId.ToString(CultureInfo.InvariantCulture) + " " + frameIndex.ToString(CultureInfo.InvariantCulture);

    /// <summary>The run id and the frame index of a section's <paramref name="name"/>; false for a name that is not one.</summary>
    public static bool TryParseName(string name, out uint runId, out ulong frameIndex)
    {
      runId = 0;
      frameIndex = 0;
      if (!name.StartsWith(NamePrefix, StringComparison.Ordinal))
        return false;
      var numbers = name.AsSpan(NamePrefix.Length);
      int space = numbers.IndexOf(' ');
      return space > 0
        && uint.TryParse(numbers[..space], NumberStyles.None, CultureInfo.InvariantCulture, out runId)
        && ulong.TryParse(numbers[(space + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out frameIndex);
    }
  }
}
