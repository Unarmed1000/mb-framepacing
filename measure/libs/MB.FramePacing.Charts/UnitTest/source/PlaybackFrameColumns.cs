//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads a playback page's frame columns back as the page's script does (readFrames in PlaybackPage.html, the form PlaybackData.cs
//* describes): each column its values or runs of equal values; capture and index as steps from the frame before; t as what is left of
//* its step after its captures' whole periods; step as its difference from the time to the next frame.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MB.FramePacing.Charts.UnitTest
{
  internal static class PlaybackFrameColumns
  {
    /// <summary>The frame columns of a page's data (<paramref name="root"/>) by name: t, index, capture, step, error, hold, flags.</summary>
    public static Dictionary<string, long?[]> Read(JsonElement root)
    {
      long period = root.GetProperty("periodTicks").GetInt64();
      var frames = root.GetProperty("frames");
      var columns = frames.EnumerateObject().ToDictionary(c => c.Name, c => Values(c.Value));
      var (t, capture, index, step) = (columns["t"], columns["capture"], columns["index"], columns["step"]);
      for (int i = 1; i < t.Length; ++i)
      {
        t[i] += t[i - 1] + (capture[i] * period);
        capture[i] += capture[i - 1];
        index[i] += index[i - 1];
      }
      for (int i = 0; i + 1 < t.Length; ++i)
        step[i] += t[i + 1] - t[i];
      return columns;
    }

    /// <summary>How a column is written: "values" or "rle".</summary>
    public static string FormOf(JsonElement root, string name) => root.GetProperty("frames").GetProperty(name).EnumerateObject().Single().Name;

    private static long?[] Values(JsonElement column)
    {
      static long? Number(JsonElement v) => v.ValueKind == JsonValueKind.Null ? null : v.GetInt64();
      if (column.TryGetProperty("values", out var values))
        return values.EnumerateArray().Select(Number).ToArray();
      var runs = column.GetProperty("rle").EnumerateArray().ToList();
      var all = new List<long?>();
      for (int i = 0; i < runs.Count; i += 2)
        all.AddRange(Enumerable.Repeat(Number(runs[i]), runs[i + 1].GetInt32()));
      return all.ToArray();
    }
  }
}
