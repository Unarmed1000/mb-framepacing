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
      long period = root.GetProperty("periodNs").GetInt64();
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

    /// <summary>
    /// What every video frame's QR says, as the page's script reads it (readVideoFrames): from the capture index <c>FirstIndex</c> on, the main
    /// marker's frame index, a torn capture's sync marker's and the capture's status by name. Null when the data has none.
    /// </summary>
    public static (long FirstIndex, long?[] Qr, long?[] Sync, string[] Status)? ReadVideoFrames(JsonElement root)
    {
      if (!root.TryGetProperty("videoFrames", out var videoFrames))
        return null;
      var names = root.GetProperty("videoFrameStatuses").EnumerateArray().Select(n => n.GetString()!).ToArray();
      var qr = Values(videoFrames.GetProperty("qr"));
      var sync = Values(videoFrames.GetProperty("sync"));
      long? last = null;
      for (int i = 0; i < qr.Length; ++i)
      {
        if (qr[i] is not { } step)
          continue;
        qr[i] = last is { } before ? before + step : step;
        last = qr[i];
        if (sync[i] is { } syncStep)
          sync[i] = qr[i] + syncStep;
      }
      var status = Values(videoFrames.GetProperty("status")).Select(s => names[(int)s!.Value]).ToArray();
      return (videoFrames.GetProperty("firstIndex").GetInt64(), qr, sync, status);
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
