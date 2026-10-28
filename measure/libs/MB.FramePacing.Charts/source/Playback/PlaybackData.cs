//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The data a playback page carries inline (a JSON script element): the report's title and tiles, the plots of its card (where each panel is
//* on the card and which seconds it shows, so the page can draw the playhead and seek where it is clicked), the video, and the section's
//* frames as columns of whole nanoseconds. Times are the capture's: for an import, the video's own timestamps (originNs is the run's
//* first frame), so a video time in seconds times 10^9 minus originNs is the report's time. The JSON escapes '<', '>' and '&', so it
//* cannot end its script element.
//*
//* The frame columns are written small (an hour at 240 Hz is 864,000 frames), in whole numbers the page turns back exactly:
//* - capture, index: the first value, then each frame's step from the one before;
//* - t: the first value, then what is left of the step from the frame before after its captures' whole periods
//*   (t[i] - t[i-1] - (capture[i] - capture[i-1]) * periodNs: the rounding of the recording's timestamps);
//* - step: its difference from the time to the section's next frame (t[i+1] - t[i]; 0 for the last one), null as null;
//* - error, hold, flags: as they are.
//* Each column is then {"values": [...]}, or {"rle": [value, count, value, count, ...]} when that is fewer numbers. videoFrames holds
//* what each video frame's QR says in columns of the same form (WriteVideoFrames).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MB.FramePacing.Analysis;
using MB.FramePacing.Data;

namespace MB.FramePacing.Charts.Playback
{
  public static class PlaybackData
  {
    /// <summary>
    /// The format of the data; the page's script reads this one. 3: every time is in nanoseconds (periodNs, originNs, ...). 4: what every
    /// video frame's QR says (videoFrames).
    /// </summary>
    public const int FormatVersion = 4;

    /// <summary>
    /// The data of <paramref name="section"/> drawn as <paramref name="cards"/> (the whole report, then the zoom steps), playing
    /// <paramref name="video"/>: a file in the page's folder, named by its file name only, as nothing in the page names a local path.
    /// </summary>
    public static string Json(RunSection section, IReadOnlyList<PlaybackCard> cards, ReportOptions options, PlaybackVideo video, string toolVersion)
    {
      using var stream = new MemoryStream();
      Write(stream, section, cards, options, video, toolVersion);
      return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary><see cref="Json"/> written into <paramref name="stream"/> as UTF-8, as the page holds it: no text of it in memory.</summary>
    public static void Write(
      Stream stream,
      RunSection section,
      IReadOnlyList<PlaybackCard> cards,
      ReportOptions options,
      PlaybackVideo video,
      string toolVersion
    )
    {
      var run = section.Run;
      var data = section.Data;
      using (var json = new Utf8JsonWriter(stream))
      {
        json.WriteStartObject();
        json.WriteNumber("formatVersion", FormatVersion);
        json.WriteString("title", options.Title ?? RunHeadline.Title(run.Run));
        if (run.Run.StartTimeUtc is { } start)
          json.WriteString("startedUtc", start.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture));
        json.WriteString("toolVersion", toolVersion);

        json.WriteStartObject("section");
        json.WriteNumber("from", section.FromSeconds);
        json.WriteNumber("to", section.ToSeconds);
        json.WriteBoolean("whole", section.IsWholeRun);
        json.WriteEndObject();

        // A page's script counts in doubles, which hold every whole number up to 2^53: 104 days of nanoseconds. Every other time of the
        // data (the frame columns, the two captures below) counts from the run's first frame, so it is exact for any run shorter than
        // that. originNs alone is a time on the capture's clock, which a recording may start anywhere: the page only adds it to a time
        // that it turns into the video's seconds, never to the columns' whole numbers
        json.WriteNumber("periodNs", run.CapturePeriod.Nanoseconds);
        json.WriteNumber("originNs", data.Origin.Nanoseconds);
        // Two captures far apart (the run's first and last frames' first sightings) give the video frame of any time without the drift of
        // a rounded period: video frame = first + (time - its time) * (last - first) / (last's time - first's time)
        if (data.Frames.Count > 0)
        {
          var first = data.Frames[0];
          var last = data.Frames[^1];
          json.WriteStartObject("captures");
          json.WriteNumber("firstIndex", first.FirstCaptureIndex);
          json.WriteNumber("firstNs", (first.FirstSeenTime - data.Origin).Nanoseconds);
          json.WriteNumber("lastIndex", last.FirstCaptureIndex);
          json.WriteNumber("lastNs", (last.FirstSeenTime - data.Origin).Nanoseconds);
          json.WriteEndObject();
        }
        if (run.Run.Pacing is { } pacing)
          json.WriteNumber("refreshHz", pacing.RefreshHz);

        json.WriteStartArray("tiles");
        foreach (var tile in RunHeadline.Shown(section.Section, options))
        {
          json.WriteStartObject();
          json.WriteString("caption", tile.Caption);
          json.WriteString("value", tile.Value);
          json.WriteString("detail", tile.Detail);
          json.WriteBoolean("warning", tile.Warning);
          json.WriteString("explanation", tile.Explanation);
          json.WriteEndObject();
        }
        json.WriteEndArray();

        json.WriteStartArray("cards");
        foreach (var card in cards)
        {
          json.WriteStartObject();
          if (card.SecondsPerScreen is { } seconds)
            json.WriteNumber("secondsPerScreen", seconds);
          else
            json.WriteNull("secondsPerScreen");
          json.WriteNumber("width", card.Drawing.Width);
          json.WriteNumber("height", card.Drawing.Height);
          json.WriteStartArray("plots");
          // Every panel of the report card has the time across, each with its own range (the refresh strip may show only its first
          // seconds); a zoomed card's plots show its first screen, and move with its scrolling layers
          foreach (var plot in card.Drawing.Plots)
          {
            json.WriteStartObject();
            json.WriteString("id", plot.Id);
            json.WriteNumber("left", plot.Left);
            json.WriteNumber("top", plot.Top);
            json.WriteNumber("right", plot.Right);
            json.WriteNumber("bottom", plot.Bottom);
            json.WriteNumber("xFrom", plot.XFrom);
            json.WriteNumber("xTo", plot.XTo);
            json.WriteEndObject();
          }
          json.WriteEndArray();
          json.WriteEndObject();
        }
        json.WriteEndArray();

        WriteVideo(json, video);
        WriteFrames(json, section);
        WriteVideoFrames(json, section);
        json.WriteEndObject();
      }
    }

    private static void WriteVideo(Utf8JsonWriter json, PlaybackVideo video)
    {
      json.WriteStartObject("video");
      // A video the user named goes in as given: the browser resolves a relative one against the page's address
      if (video.Url is { } named)
        json.WriteString("url", named);
      else if (video.VideoFile is { } file)
        json.WriteString("url", Uri.EscapeDataString(file));
      else
        json.WriteNull("url");
      json.WriteString("name", video.SourceName);
      json.WriteString("kind", video.Kind.ToString().ToLowerInvariant());
      json.WriteBoolean("playable", video.Playable);
      json.WriteString("description", video.Description);
      if (!video.Playable && video.Problem != null)
      {
        json.WriteString("problem", video.Problem);
        json.WriteString("command", "mb-framepacing render <capture folder> --playback --playback-transcode yes");
      }
      json.WriteEndObject();
    }

    /// <summary>
    /// The section's frames, a column each: first seen (nanoseconds since the run's first frame), the application's frame index, the first
    /// capture that showed it (the video frame), its display time step (until the next frame; null for the run's last), the animation error
    /// of the step into it (null when not judged), the kind of its hold (<see cref="HoldKind"/>) and its flags
    /// (<see cref="PresentedFrameFlags"/>).
    /// </summary>
    private static void WriteFrames(Utf8JsonWriter json, RunSection section)
    {
      var data = section.Data;
      var frames = data.Frames;
      var holds = data.HoldKinds;
      int first = section.Start;
      long period = section.Run.CapturePeriod.Nanoseconds;
      long Time(int i) => (frames[i].FirstSeenTime - data.Origin).Nanoseconds;
      long CaptureStep(int i) => frames[i].FirstCaptureIndex - frames[i - 1].FirstCaptureIndex;
      long? Step(int i) => i + 1 < frames.Count ? frames[i + 1].DisplayDelta?.Nanoseconds : null;
      json.WriteStartObject("frames");
      // The page reads capture before t, and t before step: each is written against the one before it
      int end = section.End;
      WriteColumn(json, "capture", first, end, i => i == first ? frames[i].FirstCaptureIndex : CaptureStep(i));
      WriteColumn(
        json,
        "index",
        first,
        end,
        i => i == first ? (long)frames[i].FrameIndex : (long)frames[i].FrameIndex - (long)frames[i - 1].FrameIndex
      );
      WriteColumn(json, "t", first, end, i => i == first ? Time(i) : Time(i) - Time(i - 1) - (CaptureStep(i) * period));
      WriteColumn(json, "step", first, end, i => Step(i) is { } step ? step - (i + 1 < end ? Time(i + 1) - Time(i) : 0) : null);
      WriteColumn(json, "error", first, end, i => frames[i].AnimationError?.Nanoseconds);
      WriteColumn(json, "hold", first, end, i => (long)holds[i]);
      WriteColumn(json, "flags", first, end, i => (long)frames[i].Flags);
      json.WriteEndObject();

      json.WriteStartArray("holdKinds");
      foreach (var kind in Enum.GetValues<HoldKind>())
        json.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()));
      json.WriteEndArray();
      json.WriteStartObject("flagBits");
      foreach (var flag in Enum.GetValues<PresentedFrameFlags>().Where(f => f != PresentedFrameFlags.None))
        json.WriteNumber(JsonNamingPolicy.CamelCase.ConvertName(flag.ToString()), (int)flag);
      json.WriteEndObject();
    }

    /// <summary>
    /// What every video frame of the section's video shows, from captures.csv's rows (<see cref="ChartRun.Captures"/>): the captures from a
    /// capture period before the section's start to a capture period after its end, and every capture index between them, with a row or
    /// without one. The page shows what the QR says, never the frame the analysis holds on screen in its place. From the capture firstIndex
    /// on, a value per capture index:
    /// <list type="bullet">
    /// <item>qr: the main marker's frame index, as its step from the last capture that had one (the first one as it is), null without one;</item>
    /// <item>sync: a torn capture's sync marker's frame index, as its step from the capture's main marker's, else null;</item>
    /// <item>status: an index into videoFrameStatuses (a capture index without a row: noCapture).</item>
    /// </list>
    /// Left out when the run's capture rows are not known.
    /// </summary>
    private static void WriteVideoFrames(Utf8JsonWriter json, RunSection section)
    {
      var rows = section.Run.Captures;
      if (rows == null)
        return;
      long period = section.Run.CapturePeriod.Nanoseconds;
      long origin = section.Data.Origin.Nanoseconds;
      long from = (long)Math.Floor(section.FromSeconds * NanosecondTimeSpan.NanosecondsPerSecond) - period;
      long to = (long)Math.Ceiling(section.ToSeconds * NanosecondTimeSpan.NanosecondsPerSecond) + period;
      long? firstIndex = null;
      long lastIndex = 0;
      foreach (var row in rows)
      {
        if (row.CaptureTime is not { } time || time.Nanoseconds - origin < from || time.Nanoseconds - origin > to)
          continue;
        firstIndex = Math.Min(firstIndex ?? row.CaptureIndex, row.CaptureIndex);
        lastIndex = Math.Max(lastIndex, row.CaptureIndex);
      }
      if (firstIndex is not { } firstCapture)
        return;
      var byIndex = new Dictionary<long, CaptureCsvRow>();
      foreach (var row in rows)
      {
        if (row.CaptureIndex >= firstCapture && row.CaptureIndex <= lastIndex)
          byIndex[row.CaptureIndex] = row;
      }
      int count = (int)(lastIndex - firstCapture + 1);
      CaptureCsvRow? Row(int i) => byIndex.GetValueOrDefault(firstCapture + i);
      // The QR's frame index as a step from the last capture that had one, so a run of captures of one frame writes zeros
      var qr = new long?[count];
      long? last = null;
      for (int i = 0; i < count; ++i)
      {
        if (Row(i)?.FrameIndex is not { } index)
          continue;
        qr[i] = last is { } before ? (long)index - before : (long)index;
        last = (long)index;
      }
      var statuses = Enum.GetValues<CaptureStatus>();
      long Status(int i) =>
        Row(i) is { } row && Enum.TryParse<CaptureStatus>(row.CaptureStatus, out var status) ? Array.IndexOf(statuses, status) : statuses.Length;
      long? Sync(int i) =>
        Row(i) is { CaptureStatus: nameof(CaptureStatus.Torn), FrameIndex: { } main, SyncFrameIndex: { } sync } ? (long)sync - (long)main : null;

      json.WriteStartObject("videoFrames");
      json.WriteNumber("firstIndex", firstCapture);
      WriteColumn(json, "qr", 0, count, i => qr[i]);
      WriteColumn(json, "sync", 0, count, Sync);
      WriteColumn(json, "status", 0, count, i => Status(i));
      json.WriteEndObject();

      json.WriteStartArray("videoFrameStatuses");
      foreach (var status in statuses)
        json.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(status.ToString()));
      json.WriteStringValue("noCapture");
      json.WriteEndArray();
    }

    /// <summary>
    /// A column of what <paramref name="value"/> gives for <paramref name="start"/> up to <paramref name="end"/>: its values, or runs of equal
    /// values (value, count) when that is fewer numbers. The values are asked for twice (to count the runs, then to write), so nothing of an
    /// hour's column is kept.
    /// </summary>
    private static void WriteColumn(Utf8JsonWriter json, string name, int start, int end, Func<int, long?> value)
    {
      int count = end - start;
      int runs = 0;
      long? previous = null;
      for (int i = start; i < end; ++i)
      {
        long? current = value(i);
        if (i == start || current != previous)
          ++runs;
        previous = current;
      }

      void Number(long? number)
      {
        if (number is { } known)
          json.WriteNumberValue(known);
        else
          json.WriteNullValue();
        // The writer keeps what it wrote until it is flushed: an hour's columns go on to the stream as they are written
        if (json.BytesPending >= FlushBytes)
          json.Flush();
      }

      json.WriteStartObject(name);
      if (runs * 2 < count)
      {
        json.WriteStartArray("rle");
        int length = 0;
        for (int i = start; i < end; ++i)
        {
          long? current = value(i);
          if (length > 0 && current != previous)
          {
            Number(previous);
            Number(length);
            length = 0;
          }
          previous = current;
          ++length;
        }
        if (length > 0)
        {
          Number(previous);
          Number(length);
        }
      }
      else
      {
        json.WriteStartArray("values");
        for (int i = start; i < end; ++i)
          Number(value(i));
      }
      json.WriteEndArray();
      json.WriteEndObject();
    }

    /// <summary>How much the JSON writer holds before it hands it on to the stream.</summary>
    private const int FlushBytes = 1 << 16;
  }
}
